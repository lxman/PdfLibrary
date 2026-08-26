using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

/// <summary>What kind of PDF/A clause 6.3.3 annotation-appearance repair a candidate or refusal
/// describes. R1 (this file) strips the <c>/AP</c> keys 6.3.3-t2 rejects from a widget whose
/// appearance dictionary already validly contains <c>/N</c>. R2 -- writing a blank normal appearance
/// for a value-less <c>/Tx</c>/<c>/Ch</c> widget -- is a later task's addition to this same partial
/// class; <see cref="WriteBlankAppearance"/> exists so the report shape below never needs to change
/// when that lands.</summary>
public enum AnnotationAppearanceRepairKind
{
    /// <summary>R1: delete the <c>/AP</c> keys ISO 19005-2 6.3.3-t2 rejects (<c>/D</c>, <c>/R</c>)
    /// from a widget's appearance dictionary that already validly contains <c>/N</c>. Never a
    /// wildcard "everything that is not /N" -- see
    /// <see cref="PdfDocumentEditor.ClassifyAnnotationAppearance"/>.</summary>
    StripRejectedKeys,

    /// <summary>R2 (a later task): write a blank single-key <c>/AP /N</c> appearance for a
    /// <c>/Tx</c> or <c>/Ch</c> widget with no <c>/V</c>. Not yet produced by
    /// <see cref="PdfDocumentEditor.ClassifyAnnotationAppearance"/> -- that task adds both the
    /// classification and the write.</summary>
    WriteBlankAppearance,
}

/// <summary>One widget <see cref="PdfDocumentEditor.PreviewAnnotationAppearanceRepairs"/> found
/// repairable, and every repair kind that would apply to it.</summary>
public sealed record AnnotationAppearanceRepairCandidate(
    int ObjectNumber, IReadOnlyList<AnnotationAppearanceRepairKind> WouldApply);

/// <summary>One widget <see cref="PdfDocumentEditor.PreviewAnnotationAppearanceRepairs"/> found a
/// 6.3.3 defect on but declined to repair, with the reason a caller can surface verbatim.</summary>
public sealed record AnnotationAppearanceRefusal(
    int ObjectNumber, AnnotationAppearanceRepairKind Kind, string Reason);

/// <summary>What <see cref="PdfDocumentEditor.PreviewAnnotationAppearanceRepairs"/> found, read-only:
/// nothing has been written to the document.</summary>
public sealed record AnnotationAppearanceRepairPreview(
    IReadOnlyList<AnnotationAppearanceRepairCandidate> Candidates,
    IReadOnlyList<AnnotationAppearanceRefusal> Refused);

/// <summary>One widget <see cref="PdfDocumentEditor.RepairAnnotationAppearances"/> wrote to, and
/// every repair kind it actually applied -- past tense, unlike
/// <see cref="AnnotationAppearanceRepairCandidate.WouldApply"/>.</summary>
public sealed record AnnotationAppearanceRepair(
    int ObjectNumber, IReadOnlyList<AnnotationAppearanceRepairKind> Applied);

/// <summary>What <see cref="PdfDocumentEditor.RepairAnnotationAppearances"/> did and declined to do --
/// the ONE report shape for this whole remediation program (R1 and R2 together), so a later task's
/// domain has exactly one report to map into save-refusal entries rather than two. R2 never appears
/// in either list until a later task extends <see cref="PdfDocumentEditor.ClassifyAnnotationAppearance"/>
/// to produce it.</summary>
public sealed record AnnotationAppearanceRepairReport(
    IReadOnlyList<AnnotationAppearanceRepair> Repaired,
    IReadOnlyList<AnnotationAppearanceRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private static readonly PdfName ApNormalKey = new("N");
    private static readonly PdfName ApDownKey = new("D");
    private static readonly PdfName ApRolloverKey = new("R");

    /// <summary>The ONE classifier <see cref="PreviewAnnotationAppearanceRepairs"/> uses, and
    /// <see cref="RepairAnnotationAppearances"/> shares, so preview and repair can never disagree
    /// about what would happen to a given widget -- the same factoring
    /// <c>ClassifyImageDictionary</c>/<c>ClassifyAnnotationTypes</c> use for their own domains.
    ///
    /// <para>Scope is deliberately narrower than the rule itself: ISO 19005-2 6.3.3-t2
    /// (<c>AnnotationAppearanceRule.cs:48-55</c>) applies to any annotation with an <c>/AP</c>, but
    /// the measured population behind this repair (design doc
    /// 2026-08-26-annotation-appearance-remediation-design.md §2) is <b>entirely</b> <c>/Widget</c> --
    /// 108 findings, all <c>/Btn</c>. Staying within that measured shape rather than reasoning about
    /// every annotation subtype is deliberate, not an oversight; a non-widget annotation with the same
    /// <c>{/D, /N}</c> shape is left alone.</para>
    ///
    /// <para>Reuses <see cref="EnumerateIndirectAnnotations"/> (the same page-walk
    /// <c>ConformanceContext.CollectAnnotations</c> uses) rather than a fresh walk, so a Finding the
    /// rule raised always has a candidate or refusal here, keyed by the same object number.</para></summary>
    private void ClassifyAnnotationAppearance(
        PdfDictionary annot, List<AnnotationAppearanceRepairKind> repairs, List<AnnotationAppearanceRefusal> refusals)
    {
        if (ResolveObject(annot.Get("Subtype")) is not PdfName { Value: "Widget" })
            return; // out of the measured scope this repair targets -- see the method doc above

        if (ResolveObject(annot.Get("AP")) is not PdfDictionary appearance)
            return; // no /AP at all is 6.3.3-t1's business (a missing appearance), not t2's

        if (appearance.Count == 1 && appearance.ContainsKey(ApNormalKey))
            return; // already conforms to 6.3.3-t2 -- no gratuitous rewrite

        if (!appearance.ContainsKey(ApNormalKey))
        {
            // No /N to keep: deleting the other keys would not "fix" this appearance, it would
            // remove the only entry that could ever have been kept. Refuse rather than guess.
            refusals.Add(new AnnotationAppearanceRefusal(
                annot.ObjectNumber, AnnotationAppearanceRepairKind.StripRejectedKeys,
                "This widget's /AP has no /N (normal) appearance entry, so Pellucid will not delete "
                + "the other keys to force the dictionary into shape -- there is no normal appearance "
                + "to fall back to, and doing so would leave the annotation with no appearance at all."));
            return;
        }

        // /N is present and the dictionary carries at least one other key -- 6.3.3-t2's actual
        // violation. Only /D and /R are ever deleted: ISO 32000-1 Table 168 names /N, /R, and /D as
        // the ENTIRE key vocabulary an appearance dictionary can carry, so those two ARE "the keys
        // the rule rejects" here -- never a wildcard "everything that is not /N". A key this repair
        // does not recognize is left alone and the finding stays open for that object rather than
        // being guessed at (design doc §8, "Over-broad R1" -- degrade safely on unmeasured input).
        bool hasDown = appearance.ContainsKey(ApDownKey);
        bool hasRollover = appearance.ContainsKey(ApRolloverKey);

        if (hasDown || hasRollover)
        {
            repairs.Add(AnnotationAppearanceRepairKind.StripRejectedKeys);
            return;
        }

        refusals.Add(new AnnotationAppearanceRefusal(
            annot.ObjectNumber, AnnotationAppearanceRepairKind.StripRejectedKeys,
            "This widget's /AP fails PDF/A clause 6.3.3 (it carries a key other than /N), but the "
            + "extra key present is not /D or /R, so Pellucid does not recognize it as safe to delete "
            + "and leaves the dictionary alone."));
    }

    /// <summary>Read-only preview of every PDF/A 6.3.3 annotation-appearance defect this editor
    /// would repair right now, without writing anything. Calling it twice returns the same answer;
    /// there is no idempotency guard to trip because nothing here is ever written.</summary>
    public AnnotationAppearanceRepairPreview PreviewAnnotationAppearanceRepairs()
    {
        var candidates = new List<AnnotationAppearanceRepairCandidate>();
        var refusals = new List<AnnotationAppearanceRefusal>();

        foreach ((PdfDictionary annot, int _) in EnumerateIndirectAnnotations())
        {
            var repairs = new List<AnnotationAppearanceRepairKind>();
            ClassifyAnnotationAppearance(annot, repairs, refusals);
            if (repairs.Count > 0)
                candidates.Add(new AnnotationAppearanceRepairCandidate(annot.ObjectNumber, repairs));
        }

        return new AnnotationAppearanceRepairPreview(candidates, refusals);
    }

    /// <summary>Applies the PDF/A 6.3.3 annotation-appearance repairs
    /// <see cref="PreviewAnnotationAppearanceRepairs"/> would report, to the widgets named by
    /// <paramref name="objectNumbers"/> -- or to every offending widget in the document when it is
    /// null (the batch/CLI case, mirroring <c>RepairImageDictionaries</c>). Shares
    /// <see cref="EnumerateIndirectAnnotations"/> and <see cref="ClassifyAnnotationAppearance"/> with
    /// <see cref="PreviewAnnotationAppearanceRepairs"/>, so the write and the preview can never
    /// disagree about what would happen to a given widget.
    ///
    /// <para>R1 is purely a dictionary-key removal -- no page, content stream, or object graph is
    /// touched, unlike <c>RepairAnnotationTypes</c> -- so an optional filter (like
    /// <c>RepairImageDictionaries</c>'s) is the right risk level here, not the mandatory staged set
    /// <c>RepairAnnotationTypes</c> requires for its page-mutating flatten.</para></summary>
    public AnnotationAppearanceRepairReport RepairAnnotationAppearances(IReadOnlySet<int>? objectNumbers = null)
    {
        var repaired = new List<AnnotationAppearanceRepair>();
        var refusals = new List<AnnotationAppearanceRefusal>();

        foreach ((PdfDictionary annot, int _) in EnumerateIndirectAnnotations())
        {
            if (objectNumbers is not null && !objectNumbers.Contains(annot.ObjectNumber)) continue;

            var repairs = new List<AnnotationAppearanceRepairKind>();
            ClassifyAnnotationAppearance(annot, repairs, refusals);
            if (repairs.Count == 0) continue;

            foreach (AnnotationAppearanceRepairKind kind in repairs)
                switch (kind)
                {
                    case AnnotationAppearanceRepairKind.StripRejectedKeys:
                        PdfDictionary appearance = ResolveObject(annot.Get("AP")) as PdfDictionary
                            ?? throw new InvalidOperationException(
                                $"ClassifyAnnotationAppearance classified object {annot.ObjectNumber} "
                                + "as strippable (a resolvable /AP dictionary), but "
                                + "RepairAnnotationAppearances could not re-resolve it moments later "
                                + "on the same, unmutated-in-between annotation. This is a bug: the "
                                + "two must never disagree.");
                        appearance.Remove(ApDownKey);
                        appearance.Remove(ApRolloverKey);
                        break;

                    // Throw-on-unknown, the discipline this codebase already adopted at the
                    // ImageDictionaryRepairKind write switch (2026-08-21) and the 13-type DrawCommand
                    // canary before it: a kind added to AnnotationAppearanceRepairKind and to
                    // ClassifyAnnotationAppearance's `repairs` list but not to this switch would
                    // otherwise be reported as Applied while writing nothing at all. Unreachable
                    // today -- WriteBlankAppearance never enters `repairs` -- which is exactly why
                    // nothing would notice it becoming reachable without this.
                    default:
                        throw new NotSupportedException(
                            $"No write is implemented for annotation-appearance repair kind '{kind}' "
                            + $"on object {annot.ObjectNumber}. ClassifyAnnotationAppearance "
                            + "classified it as repairable, so either add the write here or make the "
                            + "kind refusal-only.");
                }

            repaired.Add(new AnnotationAppearanceRepair(annot.ObjectNumber, repairs));
        }

        return new AnnotationAppearanceRepairReport(repaired, refusals);
    }
}
