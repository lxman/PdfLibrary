using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Editing;

/// <summary>What kind of PDF/A clause 6.5.1 prohibited-action a <see cref="ProhibitedActionSite"/>
/// carries -- ISO 19005-2 6.5.1's two tests (<c>PdfLibrary.Conformance.Rules.ActionTypeRule</c>):
/// test 1 rejects any <c>/S</c> outside GoTo/GoToR/GoToE/Thread/URI/Named/SubmitForm (Launch and
/// JavaScript broken out as their own kinds -- the two largest measured populations after Named -- with
/// every other rejected type folded into <see cref="OtherProhibited"/>), and test 2 rejects a Named
/// action whose <c>/N</c> is not NextPage/PrevPage/FirstPage/LastPage.</summary>
public enum ProhibitedActionKind { Launch, JavaScript, DisallowedNamed, NoActionType, OtherProhibited }

/// <summary>One place PDF/A clause 6.5.1 requires removal: either an annotation host, addressed by
/// <see cref="HostObjectNumber"/> (Link/Widget <c>/A</c> and Widget <c>/AA</c> triggers -- every host
/// annotation in the measured population is indirect, so this is never null for a host site), or a
/// <c>/Names /JavaScript</c> name-tree entry, addressed by <see cref="JavaScriptEntryName"/> (these
/// carry no object number of their own).
///
/// <para><b>Both null</b> means a site no caller can address, and that is deliberate rather than a
/// degenerate case: it is how the five unmeasured hosts (catalog <c>/OpenAction</c>, catalog
/// <c>/AA</c>, page <c>/AA</c>, outline <c>/A</c>, a pure field dictionary) report themselves. They are
/// only ever refused, never repaired, and an unaddressable site is one a caller's staged set cannot
/// filter away -- see <see cref="PdfDocumentEditor.IsSelected"/>. Their identity lives in
/// <see cref="HostDescription"/> instead (<c>"catalog /OpenAction"</c>, <c>"page 3 /AA /O"</c>,
/// <c>"outline item 41 /A"</c>, <c>"field 12 /AA /K"</c>).</para>
///
/// <para><see cref="HostDescription"/> is the human-readable label a caller can surface verbatim (e.g.
/// <c>"Link /A"</c>, <c>"Widget /AA /E"</c>, <c>"Names/JavaScript"</c>).</para></summary>
public sealed record ProhibitedActionSite(
    int? HostObjectNumber,
    string? JavaScriptEntryName,
    string HostDescription,
    ProhibitedActionKind Kind);

/// <summary>One site <see cref="PdfDocumentEditor.RepairProhibitedActions"/> actually removed the
/// reference at. <see cref="ActionsRemoved"/> counts the reference removals this repair performed at
/// this site -- never the action objects themselves, which are never deleted here (the writer's
/// reachability walk collects any that become orphaned).</summary>
public sealed record ProhibitedActionRepair(ProhibitedActionSite Site, int ActionsRemoved);

/// <summary>One site <see cref="PdfDocumentEditor.PreviewProhibitedActionRepairs"/> or
/// <see cref="PdfDocumentEditor.RepairProhibitedActions"/> found a 6.5.1 defect on but declined to
/// repair, with the reason a caller can surface verbatim.</summary>
public sealed record ProhibitedActionRefusal(ProhibitedActionSite Site, string Reason);

/// <summary>What <see cref="PdfDocumentEditor.PreviewProhibitedActionRepairs"/> found, read-only:
/// nothing has been written to the document.</summary>
public sealed record ProhibitedActionRepairPreview(
    IReadOnlyList<ProhibitedActionSite> Candidates, IReadOnlyList<ProhibitedActionRefusal> Refused);

/// <summary>What <see cref="PdfDocumentEditor.RepairProhibitedActions"/> did and declined to do.
///
/// <para><b>Invariant, enforced not merely documented:</b> a site in <see cref="Repaired"/> raises no
/// <c>action-type</c> finding once this call returns -- never a partially fixed entry. This holds
/// because <see cref="PdfDocumentEditor.ClassifyProhibitedActions"/> checks
/// <see cref="PdfDocumentEditor.EvaluateAction"/> BEFORE ever adding a site to its candidate list, so a
/// site this method repairs was never anything but fully repairable -- the same discipline
/// <c>AnnotationAppearanceRepair</c>'s own invariant relies on (task 1 review finding on that program:
/// the invariant shipped unenforced for one shape and only the whole-branch review caught it).</para>
///
/// <para>The invariant is <b>per site</b>, and a host can carry several: a widget whose <c>/AA</c> has
/// one repairable trigger and one refusable one contributes a <see cref="Repaired"/> entry
/// (<c>"Widget /AA /X"</c>) and a <see cref="Refused"/> entry (<c>"Widget /AA /E"</c>) that share an
/// object number but are different sites, each fully resolved. No single site is ever split across both
/// lists.</para></summary>
public sealed record ProhibitedActionRepairReport(
    IReadOnlyList<ProhibitedActionRepair> Repaired,
    IReadOnlyList<ProhibitedActionRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private static readonly PdfName ActionKey = new("A");
    private static readonly PdfName AdditionalActionsKey = new("AA");
    private static readonly PdfName ActionTypeKey = new("S");
    private static readonly PdfName NamedActionNameKey = new("N");
    private static readonly PdfName NextActionKey = new("Next");
    private static readonly PdfName SubtypeKey = new("Subtype");
    // /Names names two different things and both are used here: the catalog's name-tree container
    // dictionary, and the flat key/value pair array inside one name-tree node. One PdfName serves both.
    private static readonly PdfName NamesKey = new("Names");
    private static readonly PdfName JavaScriptTreeKey = new("JavaScript");
    // Likewise /Kids: a name-tree node's children and an AcroForm field's children.
    private static readonly PdfName KidsKey = new("Kids");
    private static readonly PdfName OpenActionKey = new("OpenAction");
    private static readonly PdfName OutlinesKey = new("Outlines");
    private static readonly PdfName OutlineFirstKey = new("First");
    private static readonly PdfName AcroFormKey = new("AcroForm");
    private static readonly PdfName AcroFormFieldsKey = new("Fields");

    private static readonly HashSet<string> AllowedActions =
        ["GoTo", "GoToR", "GoToE", "Thread", "URI", "Named", "SubmitForm"];
    private static readonly HashSet<string> AllowedNamedActions =
        ["NextPage", "PrevPage", "FirstPage", "LastPage"];

    /// <summary>Bounds every unbounded walk in this file (name tree, <c>/Next</c> chain, outline tree,
    /// AcroForm <c>/Fields</c> tree) the way <see cref="EnumerateNameTree"/> already bounds its own: a
    /// cycle guard cannot help against a document that is merely enormous rather than cyclic.</summary>
    private const int ActionWalkBudget = 100_000;

    /// <summary>One repairable site plus everything <see cref="RepairProhibitedActions"/> needs to
    /// perform the removal, so the write side never walks the document a second time to re-locate what
    /// this classifier already resolved. Exactly one of three shapes is populated:
    /// <list type="bullet">
    ///   <item><b>annotation <c>/A</c></b> -- <see cref="Annotation"/> set, <see cref="Triggers"/> null:
    ///     remove <c>/A</c> from the annotation.</item>
    ///   <item><b>annotation <c>/AA</c> trigger</b> -- <see cref="Annotation"/> and
    ///     <see cref="Triggers"/> both set: remove <see cref="TriggerKey"/> from the <c>/AA</c>
    ///     dictionary, then remove <c>/AA</c> from the annotation if that emptied it.</item>
    ///   <item><b><c>/Names /JavaScript</c> entry</b> -- <see cref="Annotation"/> null: remove the pair
    ///     named by <see cref="ProhibitedActionSite.JavaScriptEntryName"/> from the tree.</item>
    /// </list>
    /// The five unmeasured hosts never produce a candidate at all -- they only ever produce a refusal --
    /// which is why this record has no shape for them.
    ///
    /// <para><b>Exclusivity is not expressible in the type</b> -- all three payload members are
    /// optional, so nothing here stops a future producer building, say, <see cref="Triggers"/> without
    /// <see cref="TriggerKey"/>. <see cref="RepairProhibitedActions"/>'s write switch answers that with
    /// a throw on any shape outside the three above, rather than letting it fall through to the wrong
    /// write and be reported as <c>Repaired</c>.</para>
    ///
    /// <para><see cref="Action"/> is the resolved action dictionary this site was CLASSIFIED against,
    /// carried on every shape. The name-tree write needs it: an entry name alone is not enough to find
    /// the right pair when the tree is malformed, so the removal matches on name AND on reference
    /// identity with this exact object.</para></summary>
    private sealed record ProhibitedActionCandidate(
        ProhibitedActionSite Site,
        PdfDictionary Action,
        PdfDictionary? Annotation = null,
        PdfDictionary? Triggers = null,
        PdfName? TriggerKey = null);

    /// <summary>Null when the action is permitted; otherwise the kind that makes it prohibited. Mirrors
    /// <c>PdfLibrary.Conformance.Rules.ActionTypeRule</c>'s two tests (6.5.1-t1, -t2) exactly -- verified
    /// against that rule at plan time (task brief Step 0). A divergence between this and the rule would
    /// mean the repair and the rule disagree about what PDF/A prohibits, which is the sharpest bug this
    /// program's predecessor (annotation-flags) had.
    ///
    /// <para>Reads the HEAD action only. A permitted head can still hide a prohibited action down its
    /// <c>/Next</c> chain, which the rule collects and flags; <see cref="EvaluateAction"/> is what closes
    /// that gap, and every host walk goes through it rather than calling this directly.</para></summary>
    private ProhibitedActionKind? ClassifyAction(PdfDictionary action)
    {
        if (ResolveObject(action.Get(ActionTypeKey)) is not PdfName s)
            return ProhibitedActionKind.NoActionType;
        if (!AllowedActions.Contains(s.Value))
            return s.Value switch
            {
                "Launch" => ProhibitedActionKind.Launch,
                "JavaScript" => ProhibitedActionKind.JavaScript,
                _ => ProhibitedActionKind.OtherProhibited,
            };
        if (s.Value != "Named") return null;
        return ResolveObject(action.Get(NamedActionNameKey)) is PdfName n && AllowedNamedActions.Contains(n.Value)
            ? null
            : ProhibitedActionKind.DisallowedNamed;
    }

    /// <summary>What <paramref name="head"/>'s <c>/Next</c> chain actually contains -- BOTH facts from
    /// ONE traversal, because both questions get asked about every chain and a second walker would be a
    /// second chance to disagree with this one. The head itself is never examined; the caller has
    /// already classified it.
    ///
    /// <para><c>/Next</c> is legally either a single action dictionary or an array of them, and both
    /// shapes are followed, exactly as <c>ActionTypeRule.CollectActions</c> does (<c>:98-103</c>): a
    /// chain the rule walks but this does not is a finding the repair reports as absent.</para>
    ///
    /// <para>Unlike the rule's walk this one cannot stop early -- <c>CarriesPermitted</c> is only known
    /// once the whole chain has been seen. The budget still bounds it.</para>
    ///
    /// <para>Cycle-guarded on object number, which is the only cycle a real chain can form -- a direct
    /// dictionary cannot contain itself, so a loop must pass through an indirect reference. The budget
    /// bounds the merely-enormous case the cycle guard cannot.</para>
    ///
    /// <para>The <c>ContainsKey</c> fast path is not just an optimisation: almost no action carries a
    /// <c>/Next</c> at all (zero in the measured corpus), and this runs once per action on every host
    /// walk, so the common case must not allocate a stack and a visited set to discover nothing.</para></summary>
    private (bool CarriesProhibited, bool CarriesPermitted) InspectNextChain(PdfDictionary head)
    {
        if (!head.ContainsKey(NextActionKey))
            return (false, false);

        var visited = new HashSet<int>();
        var stack = new Stack<PdfObject?>();
        var carriesProhibited = false;
        var carriesPermitted = false;
        PushChain(head);

        for (int budget = ActionWalkBudget; stack.Count > 0 && budget > 0; budget--)
        {
            if (ResolveObject(stack.Pop()) is not PdfDictionary action)
                continue; // a destination array, a name or a null is not an action -- the rule skips it too
            if (action.IsIndirect && !visited.Add(action.ObjectNumber))
                continue;
            if (ClassifyAction(action) is null)
                carriesPermitted = true;
            else
                carriesProhibited = true;
            PushChain(action);
        }

        return (carriesProhibited, carriesPermitted);

        void PushChain(PdfDictionary action)
        {
            switch (ResolveObject(action.Get(NextActionKey)))
            {
                case PdfArray chain:
                    foreach (PdfObject link in chain) stack.Push(link);
                    break;
                case PdfDictionary next:
                    stack.Push(next);
                    break;
            }
        }
    }

    /// <summary>The single per-action decision every host walk shares: null when there is nothing to
    /// report at this site, otherwise the kind plus a refusal reason (null reason = repairable).
    /// Factored out so a new host kind cannot accidentally get different semantics from the ones already
    /// here -- the failure mode being that a shape the rule flags reaches neither list, which is silence
    /// indistinguishable from success.
    ///
    /// <para><b>A <c>/Next</c> chain is refused only when it holds something worth protecting.</b> The
    /// original test was "is the <c>/Next</c> key present?", which was wrong in both directions: it
    /// refused a chain whose every action is ITSELF prohibited -- where removing the host reference makes
    /// the whole chain unreachable and cleans the document -- while telling the user we did it to protect
    /// "chained actions that PDF/A permits" that were not there. A refusal reason that can be false is
    /// worse than no reason. (Note for the shape that reads most naturally as the other half of that
    /// defect, <c>/Next null</c>: it cannot occur. <c>PdfDictionary.Set</c> drops a
    /// <c>PdfNull</c> value outright, per ISO 32000-1 7.3.9, and the parser assigns through the same
    /// indexer -- so the key never reaches a dictionary, here or in the rule.)</para></summary>
    private (ProhibitedActionKind Kind, string? RefuseReason)? EvaluateAction(
        PdfDictionary action, string hostDescription)
    {
        ProhibitedActionKind? kind = ClassifyAction(action);
        (bool chainCarriesProhibited, bool chainCarriesPermitted) = InspectNextChain(action);

        if (kind is not null)
        {
            return (kind.Value, chainCarriesPermitted
                ? $"The action on {hostDescription} carries a /Next chain that reaches an action PDF/A "
                + "permits; removing the reference would remove that permitted action too."
                : null);
        }

        // Head permitted, but the /Next chain can still hide an action the rule WILL flag. Reading the
        // head alone landed such a site in neither list.
        if (chainCarriesProhibited)
            return (ProhibitedActionKind.OtherProhibited,
                    $"A permitted action on {hostDescription} carries a /Next chain containing an action "
                  + "PDF/A prohibits; removing the reference would also remove the permitted head.");

        return null;
    }

    /// <summary>The ONE classifier <see cref="PreviewProhibitedActionRepairs"/> and
    /// <see cref="RepairProhibitedActions"/> both call, so preview and repair can never disagree about
    /// what would happen at a given site -- the same factoring
    /// <c>PdfDocumentEditor.AnnotationAppearances.cs</c>'s <c>ClassifyAnnotationAppearance</c> uses for
    /// its own domain. A new host kind is a new branch HERE, never a second walk and never a second
    /// entry point.
    ///
    /// <para>Four passes, in this order because the later ones depend on the first:
    /// <list type="number">
    ///   <item>every INDIRECT annotation's <c>/A</c> and <c>/AA</c> triggers
    ///     (<see cref="EnumerateIndirectAnnotations"/>) -- the whole measured population, and the only
    ///     pass that produces repairs on a host;</item>
    ///   <item>the catalog's <c>/Names /JavaScript</c> name tree -- document-level scripts, addressed by
    ///     entry name because they have no object number of their own;</item>
    ///   <item>the DIRECT annotations pass 1 skips (<see cref="ClassifyDirectAnnotations"/>) -- refused,
    ///     never repaired, because they have no object number for a caller to stage;</item>
    ///   <item>the five hosts this repair refuses (catalog <c>/OpenAction</c>, catalog <c>/AA</c>, page
    ///     <c>/AA</c>, outline <c>/A</c>, a pure field dictionary), which is why pass 1 records the
    ///     object number of every annotation it saw: a field dictionary is "pure" precisely when it is
    ///     not one of them.</item>
    /// </list></para>
    ///
    /// <para><b>Why passes 3 and 4 walk their hosts rather than merely declaring them.</b> Not one of
    /// those hosts is reachable from <see cref="EnumerateIndirectAnnotations"/> -- a direct annotation
    /// least of all, since that walk skips it on purpose. A refusal that fires only when some other walk
    /// happens to reach the host would never fire at all: the shape would pass in silence and the report
    /// would say "nothing to refuse" about a document with a 6.5.1 defect we deliberately chose not to
    /// repair. Refusing is supposed to make an unrepairable shape surface loudly, so the classifier has
    /// to go and look.</para>
    ///
    /// <para>Candidates carry the resolved host dictionary and the resolved action alongside the public
    /// <see cref="ProhibitedActionSite"/> record -- never exposed on the record itself, which only ever
    /// carries the object number a caller can address -- so <see cref="RepairProhibitedActions"/> can
    /// write directly to the same, already-resolved dictionaries this method classified, with no second
    /// annotation walk to re-locate them.</para></summary>
    private (List<ProhibitedActionCandidate> Candidates, List<ProhibitedActionRefusal> Refused)
        ClassifyProhibitedActions()
    {
        var candidates = new List<ProhibitedActionCandidate>();
        var refusals = new List<ProhibitedActionRefusal>();
        var annotationObjectNumbers = new HashSet<int>();

        // Runs before the loop, not inside it: whether a container is shared is a fact about the whole
        // document, never about the annotation currently in hand.
        HashSet<PdfDictionary> sharedTriggers = SharedTriggerDictionaries();

        foreach ((PdfDictionary annot, int _) in EnumerateIndirectAnnotations())
        {
            annotationObjectNumbers.Add(annot.ObjectNumber);
            string subtype = ResolveObject(annot.Get(SubtypeKey)) is PdfName { Value: { } sub } ? sub : "?";

            ClassifyAnnotationAction(annot, subtype, candidates, refusals);
            ClassifyAnnotationTriggers(annot, subtype, sharedTriggers, candidates, refusals);
        }

        ClassifyJavaScriptNameTree(candidates, refusals);
        ClassifyDirectAnnotations(refusals);
        ClassifyUnmeasuredHosts(annotationObjectNumbers, refusals);

        return (candidates, refusals);
    }

    /// <summary>Every <c>/AA</c> dictionary that more than one annotation reaches, by REFERENCE identity
    /// on the resolved dictionary -- which is exact here, because
    /// <c>PdfDocument.GetObject</c> caches, so two references to one object resolve to one instance.
    ///
    /// <para>Two annotations may legally share an indirect <c>/AA</c>. Removing a trigger from it on
    /// behalf of the widget a caller staged removes it from the widget it did not, leaving that second
    /// annotation pointing at a container this repair emptied while the report said nothing about it --
    /// the same hazard the program already closed for shared ACTION objects ("deleting an object would
    /// affect hosts the caller never selected"), missed for the shared CONTAINER.</para>
    ///
    /// <para>Whether any corpus document actually has a shared <c>/AA</c> is <b>unmeasured</b>; the 67
    /// measured widget-<c>/AA</c> findings were never checked for it. So this deliberately refuses only
    /// the shared ones and leaves the ordinary unshared case repairing exactly as before -- widget
    /// <c>/AA</c> is 67 of 1365 findings across 4 documents and the board movement depends on them.
    /// Task 5's re-preflight of the saved bytes is where a corpus that does contain one would show
    /// up.</para>
    ///
    /// <para><b>Every annotation entry counts, direct as well as indirect</b>, which is why this walks
    /// the pages itself instead of taking <see cref="EnumerateIndirectAnnotations"/>'s output. A direct
    /// annotation is only ever refused (<see cref="ClassifyDirectAnnotations"/>) -- so if the scan
    /// skipped it, an <c>/AA</c> it shares with an indirect widget would read as unshared, the widget's
    /// repair would strip the trigger, and the direct annotation's refusal would have told the caller it
    /// was "left in place" moments before it was removed. A FALSE report is worse than a silent
    /// one.</para>
    ///
    /// <para>The annotation itself is deduped by reference first: one annotation listed on two pages is
    /// one host, and counting its <c>/AA</c> twice would flag every such document as shared. This
    /// deliberately does NOT consider an <c>/AA</c> shared with a non-annotation host (a pure field, a
    /// page, the catalog) -- those hosts are refused wholesale so nothing repairs through them today,
    /// and no such sharing has been measured; it is written here rather than left to be
    /// rediscovered.</para></summary>
    private HashSet<PdfDictionary> SharedTriggerDictionaries()
    {
        var seenAnnotations = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var shared = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        foreach (PdfPage page in _document.GetPages())
        {
            if (page.GetAnnotations() is not { } annots)
                continue;

            foreach (PdfObject entry in annots)
            {
                if (ResolveObject(entry) is not PdfDictionary annot || !seenAnnotations.Add(annot))
                    continue;
                if (ResolveObject(annot.Get(AdditionalActionsKey)) is PdfDictionary triggers && !seen.Add(triggers))
                    shared.Add(triggers);
            }
        }

        return shared;
    }

    /// <summary>An annotation's own <c>/A</c> -- Link and Widget alike. Neither
    /// <see cref="ProhibitedActionKind"/> nor <see cref="EvaluateAction"/> distinguishes subtype; only
    /// <see cref="ProhibitedActionSite.HostDescription"/> does, built from the annotation's own
    /// <c>/Subtype</c>.</summary>
    private void ClassifyAnnotationAction(
        PdfDictionary annot,
        string subtype,
        List<ProhibitedActionCandidate> candidates,
        List<ProhibitedActionRefusal> refusals)
    {
        if (ResolveObject(annot.Get(ActionKey)) is not PdfDictionary action)
            return; // no /A, or it does not resolve to a dictionary -- nothing for 6.5.1 to flag

        string hostDescription = $"{subtype} /A";
        if (EvaluateAction(action, hostDescription) is not { } evaluation)
            return; // a permitted action -- leave it untouched, no rewrite

        var site = new ProhibitedActionSite(annot.ObjectNumber, null, hostDescription, evaluation.Kind);
        if (evaluation.RefuseReason is { } reason)
            refusals.Add(new ProhibitedActionRefusal(site, reason));
        else
            candidates.Add(new ProhibitedActionCandidate(site, action, Annotation: annot));
    }

    /// <summary>An annotation's <c>/AA</c> additional-actions dictionary, one site per trigger. Each
    /// trigger is its own site because each is independently removable: a widget with a prohibited
    /// <c>/E</c> and a permitted <c>/X</c> loses only <c>/E</c>.
    ///
    /// <para>The trigger keys are snapshotted before anything is classified. Nothing here mutates
    /// <c>/AA</c> -- the write happens later, in <see cref="RepairProhibitedActions"/>, against this
    /// already-resolved dictionary -- but enumerating a dictionary's own <c>Keys</c> while the same
    /// dictionary is being edited is the obvious bug in this shape, and the snapshot means a future
    /// edit here cannot introduce it.</para>
    ///
    /// <para>A trigger inside an <c>/AA</c> that <paramref name="sharedTriggers"/> says two annotations
    /// reach is REFUSED rather than repaired -- see <see cref="SharedTriggerDictionaries"/> for why, and
    /// for why this is not solved by cloning the container on write. When a site is refusable for both
    /// that reason and <see cref="EvaluateAction"/>'s own, the chain reason wins: it is the more specific
    /// of the two, and the site is a refusal either way, which is all the invariant asks.</para></summary>
    private void ClassifyAnnotationTriggers(
        PdfDictionary annot,
        string subtype,
        HashSet<PdfDictionary> sharedTriggers,
        List<ProhibitedActionCandidate> candidates,
        List<ProhibitedActionRefusal> refusals)
    {
        if (ResolveObject(annot.Get(AdditionalActionsKey)) is not PdfDictionary triggers)
            return;

        bool shared = sharedTriggers.Contains(triggers);

        foreach (PdfName triggerKey in triggers.Keys.ToList())
        {
            if (ResolveObject(triggers.Get(triggerKey)) is not PdfDictionary action)
                continue;

            string hostDescription = $"{subtype} /AA /{triggerKey.Value}";
            if (EvaluateAction(action, hostDescription) is not { } evaluation)
                continue;

            var site = new ProhibitedActionSite(annot.ObjectNumber, null, hostDescription, evaluation.Kind);
            if (evaluation.RefuseReason is { } reason)
                refusals.Add(new ProhibitedActionRefusal(site, reason));
            else if (shared)
                refusals.Add(new ProhibitedActionRefusal(
                    site,
                    $"The prohibited action on {hostDescription} was left in place: that annotation's "
                  + "/AA dictionary is shared between hosts, so removing the trigger would also alter an "
                  + "annotation the caller did not select, with nothing in this report to say so. Remove "
                  + "it by hand, or give each annotation its own /AA first."));
            else
                candidates.Add(new ProhibitedActionCandidate(site, action, Annotation: annot,
                                                             Triggers: triggers, TriggerKey: triggerKey));
        }
    }

    /// <summary>The catalog's <c>/Names /JavaScript</c> name tree -- document-level scripts, which the
    /// rule reaches through <c>EnqueueJavaScriptNames</c>. Sites are addressed by entry NAME: the tree's
    /// values are usually indirect actions, but the thing a caller stages and this repair removes is the
    /// name/value pair, and two entries can legally share one action object.
    ///
    /// <para><b>Two malformed shapes are refused rather than skipped, and both carry NEITHER address</b>
    /// (see <see cref="IsSelected"/>) because neither has one a caller could stage:
    /// <list type="bullet">
    ///   <item><b>an unreadable entry name</b> -- <see cref="EnumerateNameTree"/> yields a null key when
    ///     the pair's key slot is not a <c>PdfString</c>. Skipping it was silence:
    ///     <c>ConformanceContext.EnumerateNameTree</c> yields VALUES only, so the rule flags the action
    ///     whatever the key slot holds;</item>
    ///   <item><b>a duplicated entry name</b> -- malformed under ISO 32000-1 7.9.6. A name that
    ///     addresses two entries is not an address: a caller staging it cannot say which it meant, and
    ///     removing the first pair whose key text matches could delete an action PDF/A PERMITS while
    ///     leaving the prohibited one standing and reporting it as repaired.</item>
    /// </list>
    /// Both checks run AFTER <see cref="EvaluateAction"/>, never before: a malformed key on a PERMITTED
    /// action raises no finding, so refusing it would be a false alarm the user cannot act on.</para></summary>
    private void ClassifyJavaScriptNameTree(
        List<ProhibitedActionCandidate> candidates, List<ProhibitedActionRefusal> refusals)
    {
        if (CatalogNamesDictionary() is not { } names)
            return;

        PdfObject? tree = names.Get(JavaScriptTreeKey);
        HashSet<string> duplicated = DuplicatedNameTreeEntryNames(tree);

        foreach ((string? entryName, PdfObject value) in EnumerateNameTree(tree))
        {
            if (ResolveObject(value) is not PdfDictionary action) continue;

            bool isDuplicate = entryName is not null && duplicated.Contains(entryName);
            string hostDescription = entryName is null
                ? "Names/JavaScript (unreadable entry name)"
                : isDuplicate
                    ? $"Names/JavaScript (duplicate entry name '{entryName}')"
                    : "Names/JavaScript";

            if (EvaluateAction(action, hostDescription) is not { } evaluation)
                continue;

            if (entryName is null)
            {
                refusals.Add(new ProhibitedActionRefusal(
                    new ProhibitedActionSite(null, null, hostDescription, evaluation.Kind),
                    "A prohibited document-level script was left in place: its /Names /JavaScript entry "
                  + "key is not a string, so the entry has no name for a caller to stage and none this "
                  + "repair could remove it by. Fix the key, or remove the entry by hand."));
                continue;
            }

            if (isDuplicate)
            {
                refusals.Add(new ProhibitedActionRefusal(
                    new ProhibitedActionSite(null, null, hostDescription, evaluation.Kind),
                    $"The /Names /JavaScript entry name '{entryName}' occurs more than once in the tree, "
                  + "which ISO 32000-1 7.9.6 does not permit. A name addressing two entries is not an "
                  + "address: this repair cannot tell which one a caller staging it meant, and removing "
                  + "by name could delete an action PDF/A permits. Left in place; de-duplicate the "
                  + "tree."));
                continue;
            }

            var site = new ProhibitedActionSite(null, entryName, hostDescription, evaluation.Kind);
            if (evaluation.RefuseReason is { } reason)
                refusals.Add(new ProhibitedActionRefusal(site, reason));
            else
                candidates.Add(new ProhibitedActionCandidate(site, action));
        }
    }

    /// <summary>Every entry name that occurs more than once anywhere in <paramref name="tree"/>.
    /// Counts EVERY entry -- including ones whose value is not an action and ones whose action PDF/A
    /// permits -- because what is ambiguous is the NAME, and a permitted twin is exactly the entry a
    /// name-matching removal would destroy.</summary>
    private HashSet<string> DuplicatedNameTreeEntryNames(PdfObject? tree)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicated = new HashSet<string>(StringComparer.Ordinal);

        foreach ((string? entryName, PdfObject _) in EnumerateNameTree(tree))
            if (entryName is not null && !seen.Add(entryName))
                duplicated.Add(entryName);

        return duplicated;
    }

    /// <summary>The annotations <see cref="EnumerateIndirectAnnotations"/> drops: a page <c>/Annots</c>
    /// entry that resolves to a DIRECT dictionary. That walk skips them deliberately (its own doc
    /// comment says so, and two other remediation programs share it), but
    /// <c>ConformanceContext.CollectAnnotations</c> does NOT -- it only dedups indirect ones -- so
    /// <c>ActionTypeRule</c> flags a direct Link whose <c>/A</c> is a Launch and, until this pass
    /// existed, we reported it in neither list. The pure-field walk does not catch it either: a plain
    /// Link is not in the AcroForm <c>/Fields</c> tree.
    ///
    /// <para>Refused, never repaired, with BOTH addresses null so the refusal is unfilterable
    /// (see <see cref="IsSelected"/>): there is no object number for a caller to stage, which is the
    /// same reason the five unmeasured hosts report themselves that way. <c>"direct"</c> in the
    /// description carries the whole identity, because there is no number to name.</para>
    ///
    /// <para><b>Do not "fix" this shape by hard-blocking on a null <c>ObjectNumber</c> anywhere.</b> For
    /// this rule a Finding's <c>ObjectNumber</c> is the ACTION's, not the host's, and 444 of the 1365
    /// measured findings have none because their actions are direct dictionaries inline in an INDIRECT
    /// annotation -- those are repairable, and they are this program's single largest lever. "Null
    /// object number" and "unaddressable host" are different facts here, and only this walk can tell
    /// them apart, which is why the refusal has to be raised here rather than in the layer above.</para>
    ///
    /// <para>Deduped by reference: one direct dictionary listed on two pages is one host, and the rule
    /// dedups the action it reaches the same way (<c>CollectActions</c>' <c>directSeen</c>).</para></summary>
    private void ClassifyDirectAnnotations(List<ProhibitedActionRefusal> refusals)
    {
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        foreach (PdfPage page in _document.GetPages())
        {
            if (page.GetAnnotations() is not { } annots)
                continue;

            foreach (PdfObject entry in annots)
            {
                if (ResolveObject(entry) is not PdfDictionary annot)
                    continue;   // does not resolve to a dictionary -- the rule skips it too
                if (annot.IsIndirect)
                    continue;   // the indirect walk owns it, and repairs it
                if (!seen.Add(annot))
                    continue;

                string subtype = ResolveObject(annot.Get(SubtypeKey)) is PdfName { Value: { } sub } ? sub : "?";
                RefuseDirectAnnotationHost(annot.Get(ActionKey), $"direct {subtype} /A", refusals);

                if (ResolveObject(annot.Get(AdditionalActionsKey)) is not PdfDictionary triggers)
                    continue;
                foreach (PdfName triggerKey in triggers.Keys.ToList())
                {
                    RefuseDirectAnnotationHost(
                        triggers.Get(triggerKey), $"direct {subtype} /AA /{triggerKey.Value}", refusals);
                }
            }
        }
    }

    /// <summary>Records the refusal for one direct-annotation site, or nothing at all when the value is
    /// not an action dictionary or the action it names is permitted -- a direct host is not itself a
    /// finding, so a refusal for a permitted action on one would be a false alarm.</summary>
    private void RefuseDirectAnnotationHost(
        PdfObject? actionValue, string hostDescription, List<ProhibitedActionRefusal> refusals) =>
        RefuseAddresslessSite(
            actionValue, hostDescription, refusals,
            $"The prohibited action on {hostDescription} was left in place. Its host annotation is a "
          + "direct dictionary in a page's /Annots array, so it has no object number for a caller to "
          + "stage and this repair has no address to write against; remove it by hand, or make the "
          + "annotation an indirect object first.");

    /// <summary>Walks the five hosts this repair refuses and records a refusal at each one that actually
    /// carries a prohibited action. See <see cref="ClassifyProhibitedActions"/> for why they are walked
    /// at all rather than left to a passive check that could never fire.
    ///
    /// <para>Two rules apply to all five. <b>Only a prohibited action produces a refusal</b> -- a
    /// permitted <c>GoTo</c> on the catalog <c>/OpenAction</c> raises no finding, so refusing it would
    /// be a false alarm the user cannot act on. And <b>never a repair</b>, whatever
    /// <see cref="EvaluateAction"/> says about repairability: the host being unmeasured is itself the
    /// reason.</para></summary>
    private void ClassifyUnmeasuredHosts(
        HashSet<int> annotationObjectNumbers, List<ProhibitedActionRefusal> refusals)
    {
        PdfDictionary? catalog = _document.CatalogDictionary;
        if (catalog is not null)
        {
            // The catalog /OpenAction may legally be a destination array rather than an action
            // dictionary; RefuseUnmeasuredHost's resolve-to-dictionary check filters that out, exactly
            // as ActionTypeRule.CollectActions does when it dequeues the same value.
            RefuseUnmeasuredHost(catalog.Get(OpenActionKey), "catalog /OpenAction", refusals);
            RefuseTriggers(catalog.Get(AdditionalActionsKey), "catalog /AA", refusals);
        }

        IReadOnlyList<PdfDictionary> pages = PageTreeOps.PageDicts(_document);
        for (var i = 0; i < pages.Count; i++)
            RefuseTriggers(pages[i].Get(AdditionalActionsKey), $"page {i + 1} /AA", refusals);

        RefuseOutlineActions(catalog, refusals);
        RefusePureFieldActions(catalog, annotationObjectNumbers, refusals);
    }

    /// <summary>Records a refusal for one unmeasured host, or nothing at all when the value is not an
    /// action dictionary or the action it names is permitted.</summary>
    private void RefuseUnmeasuredHost(
        PdfObject? actionValue, string hostDescription, List<ProhibitedActionRefusal> refusals) =>
        RefuseAddresslessSite(
            actionValue, hostDescription, refusals,
            $"The prohibited action on {hostDescription} was left in place. That host carries no "
          + "prohibited action anywhere in the measured corpus, so this repair has no tested behaviour "
          + "for it and reports it rather than guessing; remove it by hand, or raise it so the host can "
          + "be measured and handled.");

    /// <summary>The shared core of every refusal whose site carries NEITHER address: resolve, evaluate,
    /// and record only when the action is actually prohibited.
    ///
    /// <para><c>HostObjectNumber</c> and <c>JavaScriptEntryName</c> are both null on purpose: an
    /// unaddressable site is one a caller's staged set cannot filter away (see <see cref="IsSelected"/>),
    /// and this refusal is the caller's only signal that a shape this repair will not touch is present
    /// in its document.</para>
    ///
    /// <para><paramref name="reason"/> is built by the caller before the prohibited-action test, so it
    /// is composed for hosts that turn out permitted too. These walks are the catalog, the page list,
    /// the outline tree and the field tree -- small enough that the interpolation is not worth an
    /// indirection to defer.</para></summary>
    private void RefuseAddresslessSite(
        PdfObject? actionValue, string hostDescription, List<ProhibitedActionRefusal> refusals, string reason)
    {
        if (ResolveObject(actionValue) is not PdfDictionary action)
            return;
        if (EvaluateAction(action, hostDescription) is not { } evaluation)
            return;

        refusals.Add(new ProhibitedActionRefusal(
            new ProhibitedActionSite(null, null, hostDescription, evaluation.Kind), reason));
    }

    /// <summary>Every trigger in an unmeasured host's <c>/AA</c> dictionary, described as
    /// <c>"&lt;host&gt; /&lt;trigger&gt;"</c> -- e.g. <c>"catalog /AA /WC"</c>,
    /// <c>"page 3 /AA /O"</c>.</summary>
    private void RefuseTriggers(
        PdfObject? additionalActions, string hostDescription, List<ProhibitedActionRefusal> refusals)
    {
        if (ResolveObject(additionalActions) is not PdfDictionary triggers)
            return;

        foreach (PdfName triggerKey in triggers.Keys.ToList())
            RefuseUnmeasuredHost(triggers.Get(triggerKey), $"{hostDescription} /{triggerKey.Value}", refusals);
    }

    /// <summary>Each outline item's <c>/A</c>, walking the <c>/First</c> + <c>/Next</c> tree. Mirrors
    /// <c>ActionTypeRule.EnqueueOutlineActions</c> (<c>:117-136</c>): same cycle guard on object number,
    /// same two pushes (sibling then child), reached through the catalog's own <c>/Outlines</c> key --
    /// the access <c>DestinationRepairer.RepairOutlines</c> (<c>:102</c>) uses.</summary>
    private void RefuseOutlineActions(PdfDictionary? catalog, List<ProhibitedActionRefusal> refusals)
    {
        if (catalog is null || ResolveObject(catalog.Get(OutlinesKey)) is not PdfDictionary outlines)
            return;

        var visited = new HashSet<int>();
        var stack = new Stack<PdfObject?>();
        stack.Push(outlines.Get(OutlineFirstKey));

        for (int budget = ActionWalkBudget; stack.Count > 0 && budget > 0; budget--)
        {
            if (ResolveObject(stack.Pop()) is not PdfDictionary item)
                continue;
            if (item.IsIndirect && !visited.Add(item.ObjectNumber))
                continue;

            RefuseUnmeasuredHost(item.Get(ActionKey), $"outline item {Describe(item)} /A", refusals);
            stack.Push(item.Get(NextActionKey));    // sibling
            stack.Push(item.Get(OutlineFirstKey));  // child
        }
    }

    /// <summary>Each PURE AcroForm field's <c>/A</c> and <c>/AA</c> triggers. Mirrors
    /// <c>ConformanceContext.CollectFormFields</c> (<c>:390-414</c>): a stack over the AcroForm
    /// <c>/Fields</c> array following <c>/Kids</c>, cycle-guarded on object number.
    ///
    /// <para>A field is <b>pure</b> when it is not also an annotation, which is tested directly rather
    /// than guessed at through <c>/Subtype</c>: <paramref name="annotationObjectNumbers"/> holds every
    /// object number the annotation pass already saw, and every one of the 78 measured form-hosted
    /// prohibited actions is on a merged field+widget dictionary that pass already returned. A
    /// <i>direct</i> field dictionary has no object number to match, so it falls out as pure and is
    /// refused -- which is the loud answer, and the right one for a shape nothing has measured.</para></summary>
    private void RefusePureFieldActions(
        PdfDictionary? catalog, HashSet<int> annotationObjectNumbers, List<ProhibitedActionRefusal> refusals)
    {
        if (catalog is null
            || ResolveObject(catalog.Get(AcroFormKey)) is not PdfDictionary acroForm
            || ResolveObject(acroForm.Get(AcroFormFieldsKey)) is not PdfArray fields)
        {
            return;
        }

        var visited = new HashSet<int>();
        var stack = new Stack<PdfObject>(fields);

        for (int budget = ActionWalkBudget; stack.Count > 0 && budget > 0; budget--)
        {
            if (ResolveObject(stack.Pop()) is not PdfDictionary field)
                continue;
            if (field.IsIndirect && !visited.Add(field.ObjectNumber))
                continue; // already visited -- guards against a cyclic /Kids graph

            if (ResolveObject(field.Get(KidsKey)) is PdfArray kids)
                foreach (PdfObject kid in kids)
                    stack.Push(kid);

            if (field.IsIndirect && annotationObjectNumbers.Contains(field.ObjectNumber))
                continue; // a merged field+widget -- the annotation pass owns it, and repaired it

            string description = $"field {Describe(field)}";
            RefuseUnmeasuredHost(field.Get(ActionKey), $"{description} /A", refusals);
            RefuseTriggers(field.Get(AdditionalActionsKey), $"{description} /AA", refusals);
        }
    }

    /// <summary>How a host dictionary names itself inside a <see cref="ProhibitedActionSite.HostDescription"/>
    /// -- its object number, or <c>"(direct)"</c> for a direct dictionary, which has none.</summary>
    private static string Describe(PdfDictionary dictionary) =>
        dictionary.IsIndirect ? dictionary.ObjectNumber.ToString() : "(direct)";

    /// <summary>The catalog's <c>/Names</c> dictionary -- the parent the <c>/JavaScript</c> tree hangs
    /// off. Needed on the read side to find the tree and on the write side to prune the
    /// <c>/JavaScript</c> key once the tree empties.</summary>
    private PdfDictionary? CatalogNamesDictionary() =>
        _document.CatalogDictionary is { } catalog
            ? ResolveObject(catalog.Get(NamesKey)) as PdfDictionary
            : null;

    /// <summary>Removes one name/value pair from a name tree, returning whether it was found. Follows
    /// <c>PdfNamedDestinations.RemoveFromNameArray</c> (<c>:233</c>) -- scan the node's flat <c>/Names</c>
    /// pair array for the key, remove both elements, otherwise recurse into <c>/Kids</c> -- with one
    /// addition: a cycle guard on object number. <see cref="EnumerateNameTree"/> guards its read, so
    /// without this a cyclic tree would classify cleanly and then blow the stack on the write.
    ///
    /// <para>Matches on <c>GetText()</c>, which is how <see cref="EnumerateNameTree"/> produced the name
    /// this repair is removing. Matching on anything else would let a site be reported under one
    /// spelling and looked up under another.</para>
    ///
    /// <para><b>And on identity</b>: the pair's VALUE must resolve to the very
    /// <paramref name="action"/> dictionary the classifier judged, which is exact because
    /// <c>PdfDocument.GetObject</c> caches, so two references to one object resolve to one instance.
    /// The name alone was enough to delete the WRONG pair in a tree with a duplicated key, which is
    /// malformed but not impossible. <see cref="ClassifyJavaScriptNameTree"/> now refuses a duplicated
    /// name outright, so on today's code paths this second lock never turns -- it is kept because it
    /// costs one comparison and because "the write removes exactly what the classifier chose" should be
    /// a property of this method, not a consequence of a check somewhere else. It is deliberately NOT an
    /// index carried from classification: indices shift when an earlier pair in the same array is
    /// removed in the same batch.</para></summary>
    private bool RemoveFromNameTree(PdfObject? node, string name, PdfDictionary action, HashSet<int> visited)
    {
        if (ResolveObject(node) is not PdfDictionary dictionary)
            return false;
        if (dictionary.IsIndirect && !visited.Add(dictionary.ObjectNumber))
            return false;

        if (ResolveObject(dictionary.Get(NamesKey)) is PdfArray pairs)
        {
            for (var i = 0; i + 1 < pairs.Count; i += 2)
            {
                if (ResolveObject(pairs[i]) is not PdfString key
                    || !string.Equals(key.GetText(), name, StringComparison.Ordinal)
                    || !ReferenceEquals(ResolveObject(pairs[i + 1]), action))
                {
                    continue;
                }

                pairs.RemoveAt(i + 1);
                pairs.RemoveAt(i);
                return true;
            }
        }

        if (ResolveObject(dictionary.Get(KidsKey)) is PdfArray kids)
            foreach (PdfObject kid in kids)
                if (RemoveFromNameTree(kid, name, action, visited))
                    return true;

        return false;
    }

    /// <summary>True when <paramref name="site"/> was asked for. Three branches, and the third is
    /// load-bearing rather than a catch-all:
    /// <list type="bullet">
    ///   <item>a host-addressed site (annotation <c>/A</c> or <c>/AA</c> trigger) is selected when
    ///     <paramref name="hostObjectNumbers"/> is null (no filter -- everything) or contains its
    ///     <see cref="ProhibitedActionSite.HostObjectNumber"/>;</item>
    ///   <item>a <c>/Names /JavaScript</c> site is selected the same way against
    ///     <paramref name="javaScriptEntryNames"/>. The two filters are independent because the two site
    ///     kinds are addressed differently -- a caller selecting hosts says nothing about which
    ///     name-tree entries it wants, and vice versa;</item>
    ///   <item><b>a site with neither address is ALWAYS selected.</b> That is the whole of how the five
    ///     unmeasured-host refusals stay unfilterable: they carry no address precisely so that a filter
    ///     the caller never asked to apply to them cannot hide them. A caller that staged one Link and
    ///     nothing else still learns its document has a prohibited catalog <c>/OpenAction</c>. Do not
    ///     "tidy" this branch into the two above -- deleting it would make an unmeasured shape pass in
    ///     silence, which is exactly what refusing is meant to prevent.</item>
    /// </list></summary>
    private static bool IsSelected(
        ProhibitedActionSite site, IReadOnlySet<int>? hostObjectNumbers, IReadOnlySet<string>? javaScriptEntryNames)
    {
        if (site.HostObjectNumber is { } hostNumber)
            return hostObjectNumbers is null || hostObjectNumbers.Contains(hostNumber);
        if (site.JavaScriptEntryName is { } entryName)
            return javaScriptEntryNames is null || javaScriptEntryNames.Contains(entryName);
        return true;
    }

    /// <summary>Read-only preview of every PDF/A 6.5.1 prohibited-action defect this editor would repair
    /// right now, without writing anything. Calling it twice returns the same answer; there is no
    /// idempotency guard to trip because nothing here is ever written.</summary>
    public ProhibitedActionRepairPreview PreviewProhibitedActionRepairs()
    {
        (List<ProhibitedActionCandidate> candidates, List<ProhibitedActionRefusal> refused) =
            ClassifyProhibitedActions();

        return new ProhibitedActionRepairPreview(candidates.Select(c => c.Site).ToList(), refused);
    }

    /// <summary>Applies the PDF/A 6.5.1 prohibited-action repairs
    /// <see cref="PreviewProhibitedActionRepairs"/> would report -- to the host annotations named by
    /// <paramref name="hostObjectNumbers"/> and the <c>/Names /JavaScript</c> entries named by
    /// <paramref name="javaScriptEntryNames"/>, or to every offending site in the document when both are
    /// null (the batch/CLI case, mirroring <c>RepairAnnotationAppearances</c>). Shares
    /// <see cref="EnumerateIndirectAnnotations"/> and <see cref="ClassifyProhibitedActions"/> with
    /// <see cref="PreviewProhibitedActionRepairs"/>, so the write and the preview can never disagree
    /// about what would happen to a given site.
    ///
    /// <para>Removes the REFERENCE, never the action object: <c>/A</c> or one <c>/AA</c> trigger key on
    /// the host, or one name/value pair in the JavaScript name tree. The action dictionary it pointed at
    /// is left in the object graph -- deleting it outright could take a sibling host's <c>/A</c> or
    /// <c>/AA</c> trigger with it (measured fact: one corpus document shares an action object between two
    /// widgets); an orphaned action object is the writer's reachability walk's job to collect, not this
    /// repair's.</para>
    ///
    /// <para>Two containers are pruned when they empty, and only then: an annotation's <c>/AA</c> once
    /// its last trigger is gone, and <c>/Names /JavaScript</c> once the tree holds no entries. Emptiness
    /// is re-read from the document after the removals rather than predicted from the candidate list, so
    /// a container holding an entry this call was never asked to touch keeps it and keeps itself.</para>
    ///
    /// <para>A site not selected by either filter is simply absent from the returned report -- neither
    /// <see cref="ProhibitedActionRepairReport.Repaired"/> nor
    /// <see cref="ProhibitedActionRepairReport.Refused"/> -- the same "only tell me about what I asked
    /// for" semantics <c>RepairAnnotationAppearances</c>'s <c>objectNumbers</c> filter already uses. The
    /// five unmeasured-host refusals are the deliberate exception; see <see cref="IsSelected"/>.</para></summary>
    public ProhibitedActionRepairReport RepairProhibitedActions(
        IReadOnlySet<int>? hostObjectNumbers = null, IReadOnlySet<string>? javaScriptEntryNames = null)
    {
        (List<ProhibitedActionCandidate> candidates, List<ProhibitedActionRefusal> refused) =
            ClassifyProhibitedActions();

        var repaired = new List<ProhibitedActionRepair>();
        var removedAnyJavaScriptEntry = false;

        foreach (ProhibitedActionCandidate candidate in candidates)
        {
            if (!IsSelected(candidate.Site, hostObjectNumbers, javaScriptEntryNames))
                continue;

            // Each case matches ONE of ProhibitedActionCandidate's three shapes exhaustively -- every
            // payload member named in every case, so a shape outside them cannot fall through to a
            // neighbour's write. That is what the default arm is for: the record makes all three members
            // optional, so a future producer building (say) Triggers with a null TriggerKey would
            // otherwise have removed the host's /A instead and been reported as Repaired. Throw-on-
            // unknown is the discipline this codebase already adopted at the AnnotationAppearances write
            // switch (:404-416) and at five DrawCommand walk sites before it.
            switch (candidate)
            {
                case { Annotation: { } annotation, Triggers: { } triggers, TriggerKey: { } triggerKey }:
                    triggers.Remove(triggerKey);
                    if (triggers.Count == 0)
                        annotation.Remove(AdditionalActionsKey);
                    break;

                case { Annotation: { } annotation, Triggers: null, TriggerKey: null }:
                    annotation.Remove(ActionKey);
                    break;

                case { Annotation: null, Triggers: null, TriggerKey: null,
                       Site.JavaScriptEntryName: { } entryName }:
                    if (!RemoveFromNameTree(
                            CatalogNamesDictionary()?.Get(JavaScriptTreeKey),
                            entryName,
                            candidate.Action,
                            []))
                    {
                        continue; // never found -- report nothing rather than a repair that did not happen
                    }

                    removedAnyJavaScriptEntry = true;
                    break;

                default:
                    throw new NotSupportedException(
                        "No write is implemented for a prohibited-action candidate of this shape "
                      + $"({candidate.Site.HostDescription}): Annotation="
                      + $"{(candidate.Annotation is null ? "null" : "set")}, Triggers="
                      + $"{(candidate.Triggers is null ? "null" : "set")}, TriggerKey="
                      + $"{candidate.TriggerKey?.Value ?? "null"}, JavaScriptEntryName="
                      + $"{candidate.Site.JavaScriptEntryName ?? "null"}. ClassifyProhibitedActions "
                      + "produced it as repairable, so either add the write here or make the shape "
                      + "refusal-only.");
            }

            repaired.Add(new ProhibitedActionRepair(candidate.Site, ActionsRemoved: 1));
        }

        if (removedAnyJavaScriptEntry
            && CatalogNamesDictionary() is { } names
            && !EnumerateNameTree(names.Get(JavaScriptTreeKey)).Any())
        {
            names.Remove(JavaScriptTreeKey);
        }

        List<ProhibitedActionRefusal> selectedRefusals = refused
            .Where(r => IsSelected(r.Site, hostObjectNumbers, javaScriptEntryNames))
            .ToList();

        return new ProhibitedActionRepairReport(repaired, selectedRefusals);
    }
}
