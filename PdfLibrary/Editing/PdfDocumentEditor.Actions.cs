using System.Linq;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

/// <summary>What kind of PDF/A clause 6.5.1 prohibited-action a <see cref="ProhibitedActionSite"/>
/// carries -- ISO 19005-2 6.5.1's two tests (<c>PdfLibrary.Conformance.Rules.ActionTypeRule</c>):
/// test 1 rejects any <c>/S</c> outside GoTo/GoToR/GoToE/Thread/URI/Named/SubmitForm (Launch and
/// JavaScript broken out as their own kinds -- the two largest measured populations after Named -- with
/// every other rejected type folded into <see cref="OtherProhibited"/>), and test 2 rejects a Named
/// action whose <c>/N</c> is not NextPage/PrevPage/FirstPage/LastPage.</summary>
public enum ProhibitedActionKind { Launch, JavaScript, DisallowedNamed, NoActionType, OtherProhibited }

/// <summary>One place PDF/A clause 6.5.1 requires removal: either an annotation host, addressed by
/// <see cref="HostObjectNumber"/> (Link/Widget <c>/A</c> this task; Widget <c>/AA</c> triggers in
/// Task 2 -- every host annotation in the measured population is indirect, so this is never null for a
/// host site), or a <c>/Names /JavaScript</c> name-tree entry, addressed by
/// <see cref="JavaScriptEntryName"/> (Task 2 -- these carry no object number of their own).
/// <see cref="HostDescription"/> is the human-readable label a caller can surface verbatim (e.g.
/// <c>"Link /A"</c>, <c>"Widget /A"</c>).</summary>
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
/// <see cref="PdfDocumentEditor.RefuseReason"/> BEFORE ever adding a site to its candidate list, so a
/// site this method repairs was never anything but fully repairable -- the same discipline
/// <c>AnnotationAppearanceRepair</c>'s own invariant relies on (task 1 review finding on that program:
/// the invariant shipped unenforced for one shape and only the whole-branch review caught it).</para></summary>
public sealed record ProhibitedActionRepairReport(
    IReadOnlyList<ProhibitedActionRepair> Repaired,
    IReadOnlyList<ProhibitedActionRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private static readonly PdfName ActionKey = new("A");
    private static readonly PdfName ActionTypeKey = new("S");
    private static readonly PdfName NamedActionNameKey = new("N");
    private static readonly PdfName NextActionKey = new("Next");
    private static readonly PdfName SubtypeKey = new("Subtype");

    private static readonly HashSet<string> AllowedActions =
        ["GoTo", "GoToR", "GoToE", "Thread", "URI", "Named", "SubmitForm"];
    private static readonly HashSet<string> AllowedNamedActions =
        ["NextPage", "PrevPage", "FirstPage", "LastPage"];

    /// <summary>Null when the action is permitted; otherwise the kind that makes it prohibited. Mirrors
    /// <c>PdfLibrary.Conformance.Rules.ActionTypeRule</c>'s two tests (6.5.1-t1, -t2) exactly -- verified
    /// against that rule at plan time (task brief Step 0). A divergence between this and the rule would
    /// mean the repair and the rule disagree about what PDF/A prohibits, which is the sharpest bug this
    /// program's predecessor (annotation-flags) had.</summary>
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

    /// <summary>A reason to refuse this site, or null to proceed. Refusing here -- rather than repairing
    /// and hoping -- is what makes an entry in <see cref="ProhibitedActionRepairReport.Repaired"/> mean
    /// the site raises no finding afterwards: <see cref="ClassifyProhibitedActions"/> calls this BEFORE
    /// ever adding a site to its candidate list, never after.</summary>
    private string? RefuseReason(PdfDictionary action, string hostDescription)
    {
        if (action.ContainsKey(NextActionKey))
            return $"The action on {hostDescription} carries a /Next chain; removing the reference would "
                 + "also remove chained actions that PDF/A permits.";
        return null;
    }

    /// <summary>The ONE classifier <see cref="PreviewProhibitedActionRepairs"/> and
    /// <see cref="RepairProhibitedActions"/> both call, so preview and repair can never disagree about
    /// what would happen at a given site -- the same factoring
    /// <c>PdfDocumentEditor.AnnotationAppearances.cs</c>'s <c>ClassifyAnnotationAppearance</c> uses for
    /// its own domain. Walks every annotation host's <c>/A</c> (this task, Link and Widget alike --
    /// <see cref="ProhibitedActionKind"/> and <see cref="RefuseReason"/> do not distinguish subtype, only
    /// <see cref="ProhibitedActionSite.HostDescription"/> does, built from the annotation's own
    /// <c>/Subtype</c>). Task 2 adds Widget <c>/AA</c> triggers and the <c>/Names /JavaScript</c> name
    /// tree inside this SAME method rather than a second walk, so a second host kind is a new branch
    /// here, not a new caller of <see cref="EnumerateIndirectAnnotations"/>.
    ///
    /// <para>Candidates carry the resolved host dictionary alongside the public
    /// <see cref="ProhibitedActionSite"/> record -- never exposed on the record itself, which only ever
    /// carries the object number a caller can address -- so <see cref="RepairProhibitedActions"/> can
    /// write directly to the same, already-resolved dictionary this method classified, with no second
    /// annotation walk to re-locate it.</para></summary>
    private (List<(PdfDictionary Host, ProhibitedActionSite Site)> Candidates, List<ProhibitedActionRefusal> Refused)
        ClassifyProhibitedActions()
    {
        var candidates = new List<(PdfDictionary Host, ProhibitedActionSite Site)>();
        var refusals = new List<ProhibitedActionRefusal>();

        foreach ((PdfDictionary annot, int _) in EnumerateIndirectAnnotations())
        {
            if (ResolveObject(annot.Get(ActionKey)) is not PdfDictionary action)
                continue; // no /A, or it does not resolve to a dictionary -- nothing for 6.5.1 to flag

            ProhibitedActionKind? kind = ClassifyAction(action);
            if (kind is null)
                continue; // a permitted action -- leave it untouched, no rewrite

            string subtype = ResolveObject(annot.Get(SubtypeKey)) is PdfName { Value: { } sub } ? sub : "?";
            var site = new ProhibitedActionSite(annot.ObjectNumber, null, $"{subtype} /A", kind.Value);

            string? refuseReason = RefuseReason(action, site.HostDescription);
            if (refuseReason is not null)
            {
                refusals.Add(new ProhibitedActionRefusal(site, refuseReason));
                continue;
            }

            candidates.Add((annot, site));
        }

        return (candidates, refusals);
    }

    /// <summary>True when <paramref name="site"/> was asked for: a host-addressed site (Link/Widget
    /// <c>/A</c> this task) is selected when <paramref name="hostObjectNumbers"/> is null (no filter --
    /// everything) or contains its <see cref="ProhibitedActionSite.HostObjectNumber"/>; a name-tree site
    /// (Task 2) is selected the same way against <paramref name="javaScriptEntryNames"/>. The two filters
    /// are independent because the two site kinds are addressed differently -- a caller selecting hosts
    /// says nothing about which name-tree entries it wants, and vice versa.</summary>
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
        (List<(PdfDictionary Host, ProhibitedActionSite Site)> candidates, List<ProhibitedActionRefusal> refused) =
            ClassifyProhibitedActions();

        return new ProhibitedActionRepairPreview(candidates.Select(c => c.Site).ToList(), refused);
    }

    /// <summary>Applies the PDF/A 6.5.1 prohibited-action repairs
    /// <see cref="PreviewProhibitedActionRepairs"/> would report -- to the host annotations named by
    /// <paramref name="hostObjectNumbers"/> and the name-tree entries named by
    /// <paramref name="javaScriptEntryNames"/> (Task 2), or to every offending site in the document when
    /// both are null (the batch/CLI case, mirroring <c>RepairAnnotationAppearances</c>). Shares
    /// <see cref="EnumerateIndirectAnnotations"/> and <see cref="ClassifyProhibitedActions"/> with
    /// <see cref="PreviewProhibitedActionRepairs"/>, so the write and the preview can never disagree
    /// about what would happen to a given site.
    ///
    /// <para>Removes the REFERENCE, never the action object: <c>annot.Remove(new PdfName("A"))</c>
    /// deletes the host's own <c>/A</c> key. The action dictionary it pointed at is left in the object
    /// graph -- deleting it outright could take a sibling host's <c>/A</c> or <c>/AA</c> trigger with it
    /// (measured fact: one corpus document shares an action object between two widgets); an orphaned
    /// action object is the writer's reachability walk's job to collect, not this repair's.</para>
    ///
    /// <para>A site not selected by either filter is simply absent from the returned report -- neither
    /// <see cref="ProhibitedActionRepairReport.Repaired"/> nor
    /// <see cref="ProhibitedActionRepairReport.Refused"/> -- the same "only tell me about what I asked
    /// for" semantics <c>RepairAnnotationAppearances</c>'s <c>objectNumbers</c> filter already uses.</para></summary>
    public ProhibitedActionRepairReport RepairProhibitedActions(
        IReadOnlySet<int>? hostObjectNumbers = null, IReadOnlySet<string>? javaScriptEntryNames = null)
    {
        (List<(PdfDictionary Host, ProhibitedActionSite Site)> candidates, List<ProhibitedActionRefusal> refused) =
            ClassifyProhibitedActions();

        var repaired = new List<ProhibitedActionRepair>();
        foreach ((PdfDictionary host, ProhibitedActionSite site) in candidates)
        {
            if (!IsSelected(site, hostObjectNumbers, javaScriptEntryNames))
                continue;

            host.Remove(ActionKey);
            repaired.Add(new ProhibitedActionRepair(site, ActionsRemoved: 1));
        }

        List<ProhibitedActionRefusal> selectedRefusals = refused
            .Where(r => IsSelected(r.Site, hostObjectNumbers, javaScriptEntryNames))
            .ToList();

        return new ProhibitedActionRepairReport(repaired, selectedRefusals);
    }
}
