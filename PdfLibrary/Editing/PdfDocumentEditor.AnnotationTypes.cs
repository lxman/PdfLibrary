using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Editing;

/// <summary>One annotation <see cref="PdfDocumentEditor.PreviewAnnotationTypeRepairs"/> found
/// flattenable: its <c>/Subtype</c> is one ISO 19005-2 6.3.1 prohibits, and its <c>/AP /N</c> resolves
/// to a Form XObject that can be baked onto <see cref="PageIndex"/> — the page it is currently an
/// annotation of — before the annotation itself is removed from that page's <c>/Annots</c>. Task 3's
/// write side (<c>RepairAnnotationTypes</c>) resolves the owning page again at apply time rather than
/// trusting this value, so <see cref="PageIndex"/> here is for reporting only.</summary>
public sealed record AnnotationTypeRepairCandidate(int ObjectNumber, string Subtype, int PageIndex);

/// <summary>One annotation <see cref="PdfDocumentEditor.PreviewAnnotationTypeRepairs"/> found a 6.3.1
/// defect on but declined to repair, with the reason a caller can surface verbatim.
/// <see cref="Subtype"/> is null exactly when the annotation has no <c>/Subtype</c> at all — the
/// rule's own "no appearance-bearing type to reason about" case.</summary>
public sealed record AnnotationTypeRefusal(int ObjectNumber, string? Subtype, string Reason);

/// <summary>What <see cref="PdfDocumentEditor.PreviewAnnotationTypeRepairs"/> found, read-only: nothing
/// has been written to the document.</summary>
public sealed record AnnotationTypeRepairPreview(
    IReadOnlyList<AnnotationTypeRepairCandidate> Candidates,
    IReadOnlyList<AnnotationTypeRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    /// <summary>Every INDIRECT annotation dictionary in the document, paired with the (0-based) index
    /// of the page whose <c>/Annots</c> array it was found in. Deliberately the same walk
    /// <c>ConformanceContext.CollectAnnotations</c> uses — <c>Document.GetPages()</c> in document
    /// order, each page's own (resolved) <c>/Annots</c> array, an annotation shared across pages
    /// inspected once (the first page wins, matching the rule's own dedup) — so a Finding
    /// <c>AnnotationTypeRule.Check</c> raised always has a candidate or a refusal here, keyed by the
    /// same object number.
    ///
    /// <para>A non-indirect (direct) entry is skipped here rather than classified: the rule still
    /// raises a Finding for one (with <c>ObjectNumber</c> null, since <c>Finding.ObjectNumber</c> can
    /// only ever name an indirect object), but this editor's per-object candidate/refusal contract has
    /// no object number to key such a finding on. Pellucid's domain hard-blocks it instead (spec
    /// 2026-08-24-annotation-type-remediation-design.md §6, last table row) — the same addressless
    /// branch <c>ImageDictionaryDomain</c> and <c>StreamFiltersDomain</c> already have.</para></summary>
    private IEnumerable<(PdfDictionary Annotation, int PageIndex)> EnumerateIndirectAnnotations()
    {
        var seen = new HashSet<int>();
        List<PdfPage> pages = _document.GetPages();
        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            if (pages[pageIndex].GetAnnotations() is not { } annots)
                continue;

            foreach (PdfObject entry in annots)
            {
                if (ResolveObject(entry) is not PdfDictionary annot)
                    continue; // does not resolve to a dictionary -- CollectAnnotations skips it too,
                              // so the rule never raises a Finding for it either.
                if (!annot.IsIndirect)
                    continue; // no object number to classify by; see the doc comment above.
                if (!seen.Add(annot.ObjectNumber))
                    continue; // an annotation shared across pages is inspected once (first page wins)

                yield return (annot, pageIndex);
            }
        }
    }

    /// <summary>Resolves a state-keyed <c>/AP /N</c> sub-dictionary (check boxes, radio buttons, and —
    /// per ISO 32000-1 12.5.5 — any annotation whose normal appearance depends on its current state) to
    /// the stream its <c>/AS</c> names, mirroring how <c>FormFlattener.FlattenField</c> resolves the
    /// same shape for widgets. Deliberately does NOT default a missing <c>/AS</c> to <c>"Off"</c> the
    /// way <c>FormFlattener</c> does: that default is a widget-specific convention (an unchecked
    /// checkbox has no <c>/AS</c>), and a general annotation has no such convention to fall back on. A
    /// missing <c>/AS</c>, or one that names an entry <paramref name="stateDict"/> does not have, is
    /// "no current appearance" — a refusal, not a guess.</summary>
    private PdfStream? ResolveStateKeyedStream(PdfDictionary annot, PdfDictionary stateDict)
    {
        if (ResolveObject(annot.Get("AS")) is not PdfName { Value: { } state })
            return null;
        return ResolveObject(stateDict.Get(state)) as PdfStream;
    }

    /// <summary>The ONE classifier <see cref="PreviewAnnotationTypeRepairs"/> uses, and the one Task
    /// 3's <c>RepairAnnotationTypes</c> will share, so preview and repair can never disagree about what
    /// would happen to a given annotation — the same factoring <see cref="ClassifyImageDictionary"/>
    /// and <c>ClassifyStreamFilters</c> use for their own domains (the discipline
    /// <c>ImageDictionaryDomain</c> was corrected into having after a sibling domain learned its answer
    /// by calling the mutating write from <c>Propose</c>, graded Critical).
    ///
    /// <para>Every branch below is exactly one row of the classification table in
    /// <c>docs/superpowers/specs/2026-08-24-annotation-type-remediation-design.md</c> §6, except one:
    /// that table also lists "owning page not found (orphaned annotation)" as a refusal. It is not
    /// produced here, and cannot be — <paramref name="pageIndex"/> comes from
    /// <see cref="EnumerateIndirectAnnotations"/>, which only ever yields an annotation together with
    /// the page whose own (already-resolved) <c>/Annots</c> array it was found in, so a page is known
    /// by construction for everything this method is called on. <c>AnnotationTypeRule.Check</c>'s own
    /// violation set is drawn from the identical walk (<c>ConformanceContext.CollectAnnotations</c>),
    /// so an annotation the rule can raise a Finding for is, by that same construction, never orphaned
    /// either — the row describes a defensive category this rule's own discovery mechanism cannot
    /// reach, not a gap in this classifier. See the Task 2 implementer report for the verification.</para>
    ///
    /// <para>The allowlist is not copied here: <see cref="AnnotationTypeRule.Allowed"/> was widened
    /// from <c>private</c> to <c>internal</c> for exactly this reuse, so there is exactly one 22-name
    /// list in the assembly, not two that could drift apart.</para></summary>
    private void ClassifyAnnotationTypes(
        PdfDictionary annot, int pageIndex,
        List<AnnotationTypeRepairCandidate> candidates, List<AnnotationTypeRefusal> refusals)
    {
        string? subtype = ResolveObject(annot.Get("Subtype")) is PdfName sn ? sn.Value : null;

        if (subtype is null)
        {
            refusals.Add(new AnnotationTypeRefusal(annot.ObjectNumber, null,
                "This annotation has no /Subtype; there is no appearance-bearing type to reason "
                + "about, so Pellucid leaves it alone."));
            return;
        }

        if (AnnotationTypeRule.Allowed.Contains(subtype))
            return; // a permitted subtype -- not this rule's business

        // Prohibited subtype past here. Resolve /AP /N to a Form XObject, handling the state-keyed
        // case FormFlattener.FlattenField also handles: /N may be a single stream, or a sub-dictionary
        // of appearance states keyed by name, with /AS naming the one currently visible.
        if (ResolveObject(annot.Get("AP")) is not PdfDictionary apDict)
        {
            refusals.Add(new AnnotationTypeRefusal(annot.ObjectNumber, subtype,
                $"This '{subtype}' annotation has no /AP appearance dictionary, so there is no "
                + "appearance to bake onto the page before it is removed."));
            return;
        }

        PdfObject? nRaw = apDict.Get("N");
        if (nRaw is null)
        {
            refusals.Add(new AnnotationTypeRefusal(annot.ObjectNumber, subtype,
                $"This '{subtype}' annotation's /AP has no /N (normal) appearance entry, so there is "
                + "no appearance to bake onto the page before it is removed."));
            return;
        }

        PdfStream? formStream;
        switch (ResolveObject(nRaw))
        {
            case PdfStream direct:
                formStream = direct;
                break;

            case PdfDictionary stateDict:
                formStream = ResolveStateKeyedStream(annot, stateDict);
                if (formStream is null)
                {
                    refusals.Add(new AnnotationTypeRefusal(annot.ObjectNumber, subtype,
                        $"This '{subtype}' annotation's /AP /N is a state-keyed appearance "
                        + "dictionary, but its /AS does not name a stream in it, so there is no "
                        + "current appearance to bake."));
                    return;
                }
                break;

            default:
                refusals.Add(new AnnotationTypeRefusal(annot.ObjectNumber, subtype,
                    $"This '{subtype}' annotation's /AP /N does not resolve to a Form XObject, so "
                    + "there is no appearance to bake onto the page before it is removed."));
                return;
        }

        if (ResolveObject(formStream.Dictionary.Get("Subtype")) is not PdfName { Value: "Form" })
        {
            refusals.Add(new AnnotationTypeRefusal(annot.ObjectNumber, subtype,
                $"This '{subtype}' annotation's /AP /N does not resolve to a Form XObject, so there "
                + "is no appearance to bake onto the page before it is removed."));
            return;
        }

        candidates.Add(new AnnotationTypeRepairCandidate(annot.ObjectNumber, subtype, pageIndex));
    }

    /// <summary>Read-only preview of every PDF/A 6.3.1 annotation-type defect this editor would repair
    /// right now, without writing anything — the read side of this remediation program (Task 3 adds
    /// the write, and a Pellucid domain that calls this). Calling it twice returns the same answer;
    /// there is no idempotency guard to trip because nothing here is ever written.</summary>
    public AnnotationTypeRepairPreview PreviewAnnotationTypeRepairs()
    {
        var candidates = new List<AnnotationTypeRepairCandidate>();
        var refusals = new List<AnnotationTypeRefusal>();

        foreach ((PdfDictionary annot, int pageIndex) in EnumerateIndirectAnnotations())
            ClassifyAnnotationTypes(annot, pageIndex, candidates, refusals);

        return new AnnotationTypeRepairPreview(candidates, refusals);
    }
}
