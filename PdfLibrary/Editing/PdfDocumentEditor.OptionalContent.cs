using System.Globalization;
using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

/// <summary>One optional-content configuration dictionary
/// <see cref="PdfDocumentEditor.PreviewOptionalContentRepairs"/> found repairable under ISO 19005-2/3
/// 6.9 (PDF/UA-1 7.10 states the same two of the three tests).
///
/// <para><see cref="Configuration"/> addresses the configuration WITHIN the catalog's
/// <c>/OCProperties</c> -- <c>"/D"</c> for the default one, <c>"/Configs[0]"</c> and so on for the
/// alternates -- deliberately NOT an object number. <c>OptionalContentRule</c>'s findings carry no
/// object number at all, and cannot: on seven of the eight corpus documents that need this repair,
/// <c>/OCProperties</c> and its <c>/D</c> are DIRECT dictionaries inside the catalog and have no object
/// number to name. A path is the only address that exists for every shape.</para>
///
/// <para><see cref="NameToWrite"/> is the <c>/Name</c> that WOULD be written -- conditional tense, the
/// same distinction <see cref="FileSpecNameRepairCandidate"/> draws against
/// <see cref="FileSpecNameRepair"/> -- or <see langword="null"/> when this configuration's
/// <c>/Name</c> already satisfies 6.9 and only its <c>/AS</c> is at fault.</para></summary>
public sealed record OptionalContentRepairCandidate(
    string Configuration, string? NameToWrite, bool WouldDeleteAutoState);

/// <summary>One 6.9 defect on the configuration at <see cref="Configuration"/> that this editor will
/// NOT repair, with the user-facing sentence saying why. A plain reason string rather than a
/// refusal-kind enum, for the same reason <see cref="StreamFilterRefusal"/> is one.</summary>
public sealed record OptionalContentRefusal(string Configuration, string Reason);

/// <summary>Read-only classification of every optional-content configuration in the document against
/// ISO 19005-2/3 6.9. Nothing has been written.</summary>
public sealed record OptionalContentRepairPreview(
    IReadOnlyList<OptionalContentRepairCandidate> Candidates,
    IReadOnlyList<OptionalContentRefusal> Refused);

/// <summary>One configuration <see cref="PdfDocumentEditor.RepairOptionalContent"/> actually edited.
/// Past tense, against <see cref="OptionalContentRepairCandidate"/>'s conditional.</summary>
public sealed record OptionalContentRepair(
    string Configuration, string? NameWritten, bool DeletedAutoState);

/// <summary>What <see cref="PdfDocumentEditor.RepairOptionalContent"/> did and declined to do.</summary>
public sealed record OptionalContentRepairReport(
    IReadOnlyList<OptionalContentRepair> Applied,
    IReadOnlyList<OptionalContentRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    /// <summary>One configuration's edit as the shared classifier decided it: the LIVE dictionary to
    /// write to, its path, the <c>/Name</c> to write (null = leave <c>/Name</c> alone) and whether to
    /// delete <c>/AS</c>. This is the classifier's OUTPUT, not a hint --
    /// <see cref="RepairOptionalContent"/> applies exactly these and re-derives nothing, so the preview
    /// does not merely share a predicate with the repair, it shares the plan.
    ///
    /// <para><see cref="Config"/> being the live dictionary is what makes the direct-vs-indirect
    /// distinction disappear: it is the same instance the catalog's own <c>/OCProperties</c> entry
    /// holds (directly, on seven of the eight corpus documents) or that the object table holds (on the
    /// eighth), so writing to it is writing to the graph the serializer walks. A repair that instead
    /// looked the configuration up by object number would have nothing to look up on those seven and
    /// would silently no-op while every in-memory assertion still passed.</para></summary>
    private readonly record struct OptionalContentEdit(
        PdfDictionary Config, string Path, PdfString? NameToWrite, bool DeleteAutoState);

    /// <summary>One configuration as pass 1 of <see cref="ClassifyOptionalContent"/> found it: the live
    /// dictionary, its path, the base name a synthesized <c>/Name</c> would start from, the <c>/Name</c>
    /// it already carries (<see langword="null"/> when that <c>/Name</c> fails 6.9-t1) and whether the
    /// rule would count it as a 6.9-t2 duplicate.
    ///
    /// <para>This exists so the classifier can reserve EVERY name the rule accepts before it
    /// synthesizes any -- see <see cref="ClassifyOptionalContent"/> for why a single walk-order pass
    /// silently renamed conforming configurations.</para></summary>
    private readonly record struct OptionalContentNaming(
        PdfDictionary Config, string Path, string BaseName, PdfString? Existing, bool IsDuplicate);

    private static readonly PdfName ConfigNameKey = new("Name");
    private static readonly PdfName AutoStateKey = new("AS");

    /// <summary>Every optional-content configuration dictionary in the document, paired with its path
    /// inside <c>/OCProperties</c>, in the SAME order and by the SAME walk
    /// <c>OptionalContentRule.Configurations</c> uses: the default <c>/D</c> first, then each
    /// <c>/Configs</c> entry that resolves to a dictionary, in array order. The order is load-bearing,
    /// not incidental -- 6.9-t2's uniqueness test is first-occurrence-wins, so a repair that renamed a
    /// different member of a duplicate pair than the rule flagged would leave the finding open and
    /// rename a configuration that was never at fault.
    ///
    /// <para>Each entry also carries the BASE NAME a synthesized <c>/Name</c> starts from, derived from
    /// the configuration's position rather than from a running counter, so the name a given
    /// configuration gets does not depend on what happened to the ones before it.</para>
    ///
    /// <para>Returns a list rather than yielding: the caller writes to these dictionaries while
    /// walking, and one of them is reached through the object table.</para></summary>
    private List<(PdfDictionary Config, string Path, string BaseName)> CollectOptionalContentConfigurations()
    {
        var result = new List<(PdfDictionary, string, string)>();
        if (_document.CatalogDictionary is not { } catalog) return result;
        if (ResolveObject(catalog.Get("OCProperties")) is not PdfDictionary ocProperties) return result;

        if (ResolveObject(ocProperties.Get("D")) is PdfDictionary defaultConfig)
            result.Add((defaultConfig, "/D", "Default"));

        if (ResolveObject(ocProperties.Get("Configs")) is PdfArray configs)
            for (var i = 0; i < configs.Count; i++)
                if (ResolveObject(configs[i]) is PdfDictionary config)
                    result.Add((
                        config,
                        string.Create(CultureInfo.InvariantCulture, $"/Configs[{i}]"),
                        string.Create(CultureInfo.InvariantCulture, $"Configuration {i + 1}")));

        return result;
    }

    /// <summary>The bytes <c>OptionalContentRule</c> keys its uniqueness test on --
    /// <c>Convert.ToHexString(name.Bytes)</c> over a configuration's <c>/Name</c>. Uniqueness is decided
    /// on BYTES, not on decoded text, because that is what the rule (and veraPDF's 6.9-t2) compares:
    /// two names that decode to the same characters from different encodings are distinct to it, and a
    /// repair reserving decoded text would think a name was taken when the rule does not.</summary>
    private static string NameKey(PdfString name) => Convert.ToHexString(name.Bytes);

    /// <summary>A configuration's <c>/Name</c> if it satisfies 6.9-t1, else <see langword="null"/>.
    /// Mirrors the rule's own read exactly: <c>Resolve(...) as PdfString</c>, then a length check. All
    /// three failures -- absent, present but not a string, present but zero-length -- read the same
    /// here as they do there, which matters because only the first has a corpus witness (all eight
    /// documents are missing the key outright; none has an empty or wrongly-typed one).</summary>
    private PdfString? ValidName(PdfDictionary config) =>
        ResolveObject(config.Get(ConfigNameKey)) is PdfString { Bytes.Length: > 0 } name ? name : null;

    /// <summary>A name not already spoken for by <paramref name="reserved"/>, starting from
    /// <paramref name="baseName"/> and appending <c>" (2)"</c>, <c>" (3)"</c>… until one is free.
    /// Deterministic: the same document always produces the same names, so a re-run is a no-op rather
    /// than a churn of new labels.
    ///
    /// <para>Uniqueness is the point, not decoration. 6.9-t2 forbids duplicate configuration names, and
    /// NO corpus document can catch a breach of it -- all eight have exactly one configuration and none
    /// has <c>/Configs</c> at all -- so a repair that stamped one literal name into several
    /// configurations would MANUFACTURE a t2 violation while every corpus gate stayed green. Reserving
    /// each name as it is handed out is what stops that; the synthetic <c>/Configs</c> tests are what
    /// prove it.</para>
    ///
    /// <para><paramref name="reserved"/> must ALREADY hold every name that is staying put by the time
    /// this is first called -- <see cref="ClassifyOptionalContent"/>'s pass 1 is what guarantees it.
    /// Passing a set filled in step with the synthesis is what made a synthesized name collide with,
    /// and then rename, a conforming configuration.</para></summary>
    private static PdfString UniqueName(string baseName, HashSet<string> reserved)
    {
        PdfString candidate = PdfString.FromText(baseName);
        for (var suffix = 2; !reserved.Add(NameKey(candidate)); suffix++)
            candidate = PdfString.FromText(
                string.Create(CultureInfo.InvariantCulture, $"{baseName} ({suffix})"));
        return candidate;
    }

    /// <summary>The ONE classifier <see cref="PreviewOptionalContentRepairs"/> and
    /// <see cref="RepairOptionalContent"/> share, so the preview and the repair can never disagree
    /// about what would happen to a document. Like <c>ClassifyGraphicsState</c> it emits the write's
    /// actual plan rather than a predicate the write then re-decides for itself -- the invariant
    /// <c>ClassifyAnnotationTypes</c> had to be retrofitted with (engine b85d661) after an apply-time
    /// refusal reached no surface at all.
    ///
    /// <para>Every branch is one row of the <c>OptionalContentRule</c> table in
    /// <c>docs/superpowers/specs/2026-08-24-graphics-state-and-optional-content-design.md</c> §4, and
    /// between them they cover all three tests the rule implements: t1 (<c>/Name</c> absent, empty or
    /// not a string) and t2 (duplicated <c>/Name</c>) become a written name; t4 (<c>/AS</c> present)
    /// becomes a deletion. There is one refusal, and it is not in the spec's table because the spec
    /// reasoned about configurations and this is about OBJECTS: a single configuration dictionary
    /// listed twice -- <c>/D</c> and a <c>/Configs</c> entry pointing at the same indirect object, or
    /// the same reference twice in <c>/Configs</c> -- is yielded twice by the rule's walk and so trips
    /// t2 against itself. It cannot be repaired, because giving one dictionary two different names is
    /// not a thing a document can express. No corpus document has <c>/Configs</c> at all, so this too
    /// is synthetic-only; it is here because the closure contract says a violation must land in a
    /// candidate or a refusal rather than in neither, which is how a caller reading only those lists
    /// gets told "nothing wrong" about a document that is not.</para>
    ///
    /// <para><b>Why it is two passes and not one.</b> Reserving names in walk order AS they are
    /// synthesized only ever holds the names seen BEFORE the current configuration, and that silently
    /// renames conforming configurations (whole-branch review, 2026-08-24). Concretely: a <c>/D</c>
    /// carrying no <c>/Name</c> and a <c>/Configs[0]</c> legitimately named <c>Default</c> raises
    /// exactly ONE finding (t1 on <c>/D</c>), but the one-pass form gave <c>/D</c> the name
    /// <c>Default</c>, then read <c>/Configs[0]</c>'s own <c>Default</c> as a t2 duplicate and relabelled
    /// a configuration the rule never flagged -- with nothing on any warning channel to say so. Pass 1
    /// therefore reserves every name that passes t1 across the WHOLE walk and records which occurrences
    /// the rule would call duplicates (first-wins, matching <c>OptionalContentRule</c>'s own
    /// <c>seenNames</c>); pass 2 synthesizes only for those. <c>/D</c> then gets <c>Default (2)</c> --
    /// uglier, and correct: the conforming configuration keeps its label.</para>
    ///
    /// <para><b>Profile scope: this classifier is deliberately profile-BLIND, and that is a known
    /// residual rather than an exact match to the rule.</b> <c>OptionalContentRule</c> gates 6.9-t2 off
    /// for PDF/UA-1 (veraPDF's UA 7.10 has no uniqueness test), and nothing here takes a
    /// <c>ConformanceProfile</c>, so under a PDF/UA-1 target a document whose UA-legal duplicate pair
    /// sits alongside some OTHER repairable configuration has the duplicate renamed as a side effect of
    /// the repair that target did ask for. Reachable -- the rule applies to
    /// <c>AllPdfA | PdfUA1</c> -- but population zero: no corpus document has <c>/Configs</c> at all
    /// (re-measured over all 708, 2026-08-24), so no document can carry a duplicate pair. It stays a
    /// residual rather than a fix because the profile is not available to thread: no remediation domain
    /// in Pellucid receives one, neither <c>IRemediationDomain.Propose</c> nor
    /// <c>SaveStageContribution.Apply</c> carries one, so honouring the distinction means changing that
    /// interface for every domain -- a program, not a guard. Pinned by
    /// <c>OptionalContentRepairTests.Renaming_a_duplicate_under_PDF_UA_is_a_documented_residual</c>, so
    /// the day a profile does arrive here there is a test to flip.</para>
    ///
    /// <para><b>What the two repairs cost.</b> Writing <c>/Name</c> is a pure addition: it is the label
    /// a viewer shows for the configuration in its layers panel, and nothing renders differently.
    /// Deleting <c>/AS</c> is NOT: the auto-state array drives automatic visibility changes on
    /// View/Print/Export (ISO 32000-1 8.11.4.4), so removing it stops those changes happening and a
    /// layer whose usage said "hide me when printing" will now print. Whether that is visible depends
    /// entirely on the document -- in the one corpus file that has <c>/AS</c> every state the machinery
    /// would apply is already the base state, so there it happens to be a measured no-op -- but that is
    /// a fact about that file and this comment must not be read as a fact about <c>/AS</c>.</para></summary>
    private void ClassifyOptionalContent(
        List<OptionalContentEdit> edits, List<OptionalContentRefusal> refusals)
    {
        var naming = new List<OptionalContentNaming>();

        // Names the rule would ACCEPT, reserved across the whole walk before a single name is
        // synthesized. Populated only from names that PASS t1 -- the rule's seenNames is populated in
        // exactly that branch, so an empty or missing /Name reserves nothing there either.
        var reserved = new HashSet<string>(StringComparer.Ordinal);
        var seenConfigs = new HashSet<PdfDictionary>();

        // PASS 1. Reserve, and record who the rule would call a duplicate. Nothing is synthesized here.
        foreach ((PdfDictionary config, string path, string baseName) in CollectOptionalContentConfigurations())
        {
            if (!seenConfigs.Add(config))
            {
                refusals.Add(new OptionalContentRefusal(path,
                    "This optional-content configuration is the same dictionary as one listed earlier "
                    + "in /OCProperties, so PDF/A's requirement that configuration names be unique "
                    + "cannot be met: one object cannot carry two different names. Pellucid leaves the "
                    + "document alone and the finding stays open."));
                continue;
            }

            PdfString? existing = ValidName(config);

            // Reserving and duplicate-detection are ONE operation, exactly as they are in the rule:
            // the first occurrence of a name keeps it and every later one is the t2 the rule flags.
            bool isDuplicate = existing is not null && !reserved.Add(NameKey(existing));
            naming.Add(new OptionalContentNaming(config, path, baseName, existing, isDuplicate));
        }

        // PASS 2. Synthesize only where 6.9 needs it -- a /Name that fails t1, or one pass 1 recorded
        // as a t2 duplicate. A conforming, unique name is never touched.
        foreach ((PdfDictionary config, string path, string baseName, PdfString? existing, bool isDuplicate)
                 in naming)
        {
            // t1 (absent, empty or not a string) and t2 (duplicated) are answered the same way: a
            // synthesized name. The default configuration starts from "Default" because that is what it
            // is; an alternate starts from its 1-based position in /Configs, which is stable across
            // runs. On t2 the FIRST occurrence keeps the name -- the rule flags the later one, and
            // renaming the other member of the pair would leave the finding open while relabelling a
            // configuration that was never at fault.
            PdfString? nameToWrite =
                existing is null || isDuplicate ? UniqueName(baseName, reserved) : null;

            // t4. Presence is the violation; the value is never inspected.
            bool deleteAutoState = config.ContainsKey(AutoStateKey);

            if (nameToWrite is not null || deleteAutoState)
                edits.Add(new OptionalContentEdit(config, path, nameToWrite, deleteAutoState));
        }
    }

    /// <summary>Read-only preview of every ISO 19005-2/3 6.9 (PDF/UA-1 7.10) optional-content defect
    /// this editor would repair right now, without writing anything. Calling it twice returns the same
    /// answer; there is no idempotency guard to trip because nothing here is ever written. This is what
    /// a Pellucid domain's <c>Propose</c> calls -- <c>Propose</c> must never call a mutating write
    /// counterpart to learn its answer, which a sibling domain once did and had graded Critical.</summary>
    public OptionalContentRepairPreview PreviewOptionalContentRepairs()
    {
        var edits = new List<OptionalContentEdit>();
        var refusals = new List<OptionalContentRefusal>();
        ClassifyOptionalContent(edits, refusals);

        return new OptionalContentRepairPreview(
            [.. edits.Select(e => new OptionalContentRepairCandidate(
                e.Path, e.NameToWrite?.GetText(), e.DeleteAutoState))],
            refusals);
    }

    /// <summary>Applies every edit <see cref="PreviewOptionalContentRepairs"/> reports: writes a
    /// <c>/Name</c> where 6.9-t1 or 6.9-t2 is breached, and deletes <c>/AS</c> where 6.9-t4 is. Shares
    /// <see cref="CollectOptionalContentConfigurations"/> and <see cref="ClassifyOptionalContent"/> with
    /// the preview, and applies the classifier's own plan rather than re-deriving one, so the write and
    /// the preview cannot disagree.
    ///
    /// <para><b>No staged-set parameter, deliberately.</b> Every sibling repair in this family takes
    /// object numbers because its rule addresses findings by object number. <c>OptionalContentRule</c>
    /// does not -- its <c>Finding</c>s carry no <c>ObjectNumber</c> at all, and could not: on seven of
    /// the eight corpus documents needing this repair the whole <c>/OCProperties</c> subtree is DIRECT
    /// inside the catalog and has no object number to carry. This repair is therefore document-scoped,
    /// like the rule, and a caller stages it rule-wide.</para>
    ///
    /// <para><b>The direct-vs-indirect trap, and why nothing here defends against it specially.</b>
    /// <c>/OCProperties</c> and <c>/D</c> are direct dictionaries inside the catalog on seven of the
    /// eight documents and indirect objects only on <c>Transcript_MICHAELJORDAN.pdf</c> (measured,
    /// engine 61cd7e6). The defence is structural rather than conditional: the classifier hands this
    /// method the LIVE dictionary it resolved, so a write lands on the instance the catalog's own entry
    /// holds and the serializer -- which walks the object graph and re-emits the catalog from it --
    /// carries it out either way. What would have failed is the shape this method does not have: a
    /// repair that took an object number, or that copied the configuration out and edited the copy. It
    /// would have reported success, left every in-memory assertion passing, and written a file the
    /// catalog still pointed past. That is why the tests re-read the SAVED BYTES on a document of each
    /// shape rather than asserting against editor state.</para>
    ///
    /// <para>Deleting <c>/AS</c> can orphan the usage-application dictionaries it named -- objects 58
    /// and 59 in <c>Transcript_MICHAELJORDAN.pdf</c>. Nothing deletes them explicitly: once unreachable
    /// they are dropped by the writer's own reachability walk (<c>ObjectGraphWalker</c>, the default
    /// <c>RemoveOrphans</c>), the same mechanism <c>RepairAnnotationTypes</c> relies on for a removed
    /// annotation's <c>/3DD</c>.</para></summary>
    public OptionalContentRepairReport RepairOptionalContent()
    {
        var edits = new List<OptionalContentEdit>();
        var refusals = new List<OptionalContentRefusal>();
        ClassifyOptionalContent(edits, refusals);

        var applied = new List<OptionalContentRepair>(edits.Count);
        foreach (OptionalContentEdit edit in edits)
        {
            if (edit.NameToWrite is not null)
                edit.Config.Set(ConfigNameKey, edit.NameToWrite);
            if (edit.DeleteAutoState)
                edit.Config.Remove(AutoStateKey);

            applied.Add(new OptionalContentRepair(
                edit.Path, edit.NameToWrite?.GetText(), edit.DeleteAutoState));
        }

        return new OptionalContentRepairReport(applied, refusals);
    }
}
