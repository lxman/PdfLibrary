using System.Globalization;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Editing.Stamping;

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

/// <summary>One annotation <see cref="PdfDocumentEditor.RepairAnnotationTypes"/> actually flattened:
/// its <c>/AP /N</c> appearance was baked onto <see cref="PageIndex"/>'s content (the page it was
/// found on when the repair ran -- resolved fresh, never trusted from an
/// <see cref="AnnotationTypeRepairCandidate.PageIndex"/> a caller might be holding from an earlier
/// <see cref="PdfDocumentEditor.PreviewAnnotationTypeRepairs"/> call), and the annotation itself was
/// removed from that page's <c>/Annots</c>.</summary>
public sealed record AnnotationTypeRepair(int ObjectNumber, string Subtype, int PageIndex);

/// <summary>What <see cref="PdfDocumentEditor.RepairAnnotationTypes"/> actually did and declined to
/// do, restricted to the staged set it was given.</summary>
public sealed record AnnotationTypeRepairReport(
    IReadOnlyList<AnnotationTypeRepair> Applied,
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

    /// <summary>Reads a PDF number array (e.g. <c>/Rect</c>, <c>/BBox</c>, <c>/Matrix</c>) into a
    /// <c>double[]</c> of exactly <paramref name="count"/> elements, resolving indirect entries.
    /// Returns <see langword="null"/> when <paramref name="raw"/> is absent, does not resolve to an
    /// array, is shorter than <paramref name="count"/>, or contains any entry that is not a PDF
    /// number — every one of those means "cannot be placed," never "assume zero." Distinct from
    /// <see cref="AppearancePlacement.ComputeAA"/>'s own <see langword="null"/> (a well-formed but
    /// geometrically degenerate box): this is the read from the PDF failing before that algorithm
    /// even runs.</summary>
    private double[]? ReadNumberArray(PdfObject? raw, int count)
    {
        if (ResolveObject(raw) is not PdfArray array || array.Count < count) return null;

        var result = new double[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = ResolveObject(array[i]) switch
            {
                PdfInteger n => n.Value,
                PdfReal r => r.Value,
                _ => double.NaN,
            };
            if (double.IsNaN(result[i])) return null;
        }
        return result;
    }

    /// <summary>The ISO 32000-1 §12.5.5 placement matrix AA for <paramref name="annot"/>'s appearance
    /// <paramref name="form"/>, or <see langword="null"/> having added exactly one refusal to
    /// <paramref name="refusals"/> saying which piece of geometry made the placement impossible.
    ///
    /// <para>These checks are PURE READS, and they live here — shared by
    /// <see cref="ClassifyAnnotationTypes"/> and <see cref="RepairAnnotationTypes"/> — rather than on
    /// the write side alone, which is where they started. On the write side alone they were a refusal
    /// the preview could not predict, and the app half is not built to receive one: Pellucid's
    /// <c>AnnotationTypeDomain.CollectForSave</c> calls <c>RepairAnnotationTypes(staged)</c> and
    /// discards the returned report, so an apply-time refusal reached nobody. The desktop row flipped
    /// to "fix applied", the file was rewritten, the finding was still there on reload, and no surface
    /// said why; <c>pellucid fix</c> produced no <c>needsDecision</c> row at all. Running them in the
    /// shared classifier makes the preview refuse what the repair would refuse, which is the invariant
    /// the whole Preview/Repair pairing claims.</para>
    ///
    /// <para>The present-but-malformed vs absent distinction on <c>/Matrix</c> is load-bearing:
    /// absent has a spec-defined default (identity, §8.3.4) and malformed does not, so treating the
    /// second as the first would place the appearance somewhere the document never asked for. And
    /// <see cref="AppearancePlacement.ComputeAA"/> returning <see langword="null"/> is a refusal,
    /// never a fallback matrix — baking a garbage placement would change the page, the one outcome
    /// this whole program exists to prove does not happen.</para></summary>
    private double[]? ComputeAppearancePlacement(
        PdfDictionary annot, PdfStream form, string subtype, List<AnnotationTypeRefusal> refusals)
    {
        double[]? rect = ReadNumberArray(annot.Get("Rect"), 4);
        if (rect is null)
        {
            refusals.Add(new AnnotationTypeRefusal(annot.ObjectNumber, subtype,
                $"This '{subtype}' annotation's /Rect is missing or malformed, so Pellucid could "
                + "not compute where to place its appearance on the page."));
            return null;
        }

        double[]? bbox = ReadNumberArray(form.Dictionary.Get("BBox"), 4);
        if (bbox is null)
        {
            refusals.Add(new AnnotationTypeRefusal(annot.ObjectNumber, subtype,
                $"This '{subtype}' annotation's appearance /BBox is missing or malformed, so "
                + "Pellucid could not compute where to place it on the page."));
            return null;
        }

        // /Matrix defaults to identity only when ABSENT (ISO 32000-1 §8.3.4). A /Matrix that IS
        // present but malformed is a different problem -- a genuinely broken appearance stream,
        // not "no opinion" -- so it refuses rather than silently falling back to identity.
        PdfObject? matrixRaw = form.Dictionary.Get("Matrix");
        double[]? matrix = matrixRaw is null ? [1, 0, 0, 1, 0, 0] : ReadNumberArray(matrixRaw, 6);
        if (matrix is null)
        {
            refusals.Add(new AnnotationTypeRefusal(annot.ObjectNumber, subtype,
                $"This '{subtype}' annotation's appearance /Matrix is present but malformed, so "
                + "Pellucid could not compute where to place it on the page."));
            return null;
        }

        double[]? aa = AppearancePlacement.ComputeAA(bbox, matrix, rect);
        if (aa is null)
        {
            refusals.Add(new AnnotationTypeRefusal(annot.ObjectNumber, subtype,
                $"This '{subtype}' annotation's appearance cannot be placed onto its /Rect -- its "
                + "transformed /BBox is degenerate -- so Pellucid left it alone rather than bake "
                + "a corrupted placement onto the page."));
            return null;
        }

        return aa;
    }

    /// <summary>The <c>/F</c> bits ISO 32000-1 §12.5.3 Table 165 defines as concealing an annotation
    /// from a reader: Invisible (bit 1), Hidden (bit 2), NoView (bit 6), and ToggleNoView (bit 9,
    /// which "invert[s] the interpretation of the NoView flag for certain events" — so the annotation
    /// is concealed for some of them whichever way NoView itself is set). Spelled through
    /// <see cref="PdfAnnotationFlags"/>, the assembly's public Table 165 transcription, rather than
    /// re-declared as local masks that could drift from it.</summary>
    private const PdfAnnotationFlags HidingFlags =
        PdfAnnotationFlags.Invisible | PdfAnnotationFlags.Hidden
        | PdfAnnotationFlags.NoView | PdfAnnotationFlags.ToggleNoView;

    /// <summary>An annotation's <c>/F</c> flag word, or <see langword="null"/> when <c>/F</c> is absent
    /// or does not carry a number. Mirrors <c>AnnotationFlagsRule</c>'s own read deliberately: a
    /// <see cref="PdfReal"/> is taken for its integer value (a producer that wrote <c>/F 2.0</c> still
    /// meant Hidden), and a non-numeric <c>/F</c> — a name, a string, an array — is malformed rather
    /// than a value worth interpreting, so it reads the same as an absent one, which is also how
    /// veraPDF treats it. Neither reads as "hidden": a flag word that cannot be read cannot be said to
    /// conceal anything, and refusing on one would decline a repair over a defect this rule does not
    /// own.</summary>
    private PdfAnnotationFlags? ReadAnnotationFlags(PdfDictionary annot) =>
        ResolveObject(annot.Get("F")) switch
        {
            PdfInteger i => (PdfAnnotationFlags)i.Value,
            PdfReal r => (PdfAnnotationFlags)(int)r.Value,
            _ => null,
        };

    /// <summary>The ONE classifier <see cref="PreviewAnnotationTypeRepairs"/> uses, and the one Task
    /// 3's <c>RepairAnnotationTypes</c> will share, so preview and repair can never disagree about what
    /// would happen to a given annotation — the same factoring <see cref="ClassifyImageDictionary"/>
    /// and <c>ClassifyStreamFilters</c> use for their own domains (the discipline
    /// <c>ImageDictionaryDomain</c> was corrected into having after a sibling domain learned its answer
    /// by calling the mutating write from <c>Propose</c>, graded Critical).
    ///
    /// <para>Every branch below is one row of the two classification tables in
    /// <c>docs/superpowers/specs/2026-08-24-annotation-type-remediation-design.md</c> §6 — the
    /// subtype/appearance one, and the geometry one, whose rows this method reaches through
    /// <see cref="ComputeAppearancePlacement"/> — with two exceptions. The first is an ADDITION
    /// neither table has: a hiding <c>/F</c> refuses (see
    /// that branch's own comment). It was found by the pre-merge whole-branch review, after the spec
    /// was written, in the same way §6's own geometry table was found by Task 3 — the spec reasoned
    /// about which annotations have a bakeable appearance and never about which ones a reader is
    /// meant to draw at all. The second is a row the spec HAS that this method cannot produce:
    /// that table lists "owning page not found (orphaned annotation)" as a refusal. It is not
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

        // A hiding /F refuses BEFORE the appearance is even looked at, deliberately. What makes this
        // whole repair safe is that a reader without support for the subtype is ALREADY REQUIRED to
        // draw the /AP /N appearance (ISO 32000-1 12.5.5, NOTE 3), so baking it changes nothing a
        // person sees. Table 165 is where that premise fails: Invisible is the spec's own documented
        // exception to that very sentence, and Hidden and NoView suppress display outright,
        // "regardless of its annotation type or whether an annotation handler is available". Bake one
        // of those and the repair does not preserve the page -- it REVEALS, permanently and without
        // asking, content the author concealed. Our own renderer already honours Hidden and NoView
        // (PdfRenderer skips such an annotation), so on those two the reveal is not hypothetical: the
        // page really does render differently afterwards.
        //
        // ToggleNoView is in the set for a reason of its own. It inverts NoView "for certain events",
        // so the annotation is concealed for some of them whichever way NoView is set -- and a baked
        // appearance has exactly one visibility state, which can only be "always shown". That is the
        // revealing direction again, chosen automatically for an annotation whose author asked for
        // something conditional.
        //
        // This is also the same answer AnnotationsDomain gives one save stage EARLIER: it owns
        // annotation-flags, refuses to clear these four bits, and says why in Pellucid's
        // docs/REMEDIATION-CHOICES.md entry 5 -- revealing a concealed annotation is a person's
        // decision, not the tool's. Without this branch that decision was declined at
        // SaveOrder.Annotations = 100 and then taken automatically at SaveOrder.AnnotationType = 110,
        // ten points later, on the same annotation in the same save.
        if (ReadAnnotationFlags(annot) is { } flags && (flags & HidingFlags) != 0)
        {
            refusals.Add(new AnnotationTypeRefusal(annot.ObjectNumber, subtype,
                $"This '{subtype}' annotation's /F flags hide it from view ({flags & HidingFlags}, "
                + $"/F 0x{(int)flags:X}). Baking its appearance into the page would make content the "
                + "author concealed permanently visible, and that is a person's decision rather than "
                + "Pellucid's, so Pellucid leaves it alone and the finding stays open."));
            return;
        }

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

        // The geometry rows of the spec's second classification table, run HERE rather than only on
        // the write side. They are pure reads, so nothing about them ever required the write path,
        // and leaving them there made the preview report as a candidate something the repair would
        // then refuse -- a refusal arriving after the caller had already been told the fix applied.
        if (ComputeAppearancePlacement(annot, formStream, subtype, refusals) is null)
            return; // ComputeAppearancePlacement added the refusal naming which piece of geometry

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

    // ── Task 3: RepairAnnotationTypes — the write side ──────────────────────────────────────────

    /// <summary>Re-resolves <c>/AP /N</c> to its Form XObject stream and the RAW entry that names it
    /// (an indirect reference, or the stream itself when it is embedded directly) — needed because
    /// <see cref="AnnotationTypeRepairCandidate"/> only reports <c>ObjectNumber</c>/<c>Subtype</c>/
    /// <c>PageIndex</c>, not this. Deliberately does NOT register a direct stream as an indirect
    /// object here: <see cref="RepairAnnotationTypes"/> defers that to just before the invocation is
    /// emitted, after every refusal check has passed, so a candidate refused for degenerate or
    /// malformed geometry never allocates an object number as a side effect of being looked at.
    ///
    /// <para>Reuses <see cref="ResolveStateKeyedStream"/> for the state-keyed case rather than
    /// re-implementing its <c>/AS</c> lookup, and is only ever called after
    /// <see cref="ClassifyAnnotationTypes"/> has already confirmed this exact annotation resolves to
    /// a Form XObject — every branch below is a re-tread of an already-proven-successful resolution,
    /// not a second chance to fail differently. <see cref="RepairAnnotationTypes"/> treats a
    /// <see langword="null"/> result as a bug, not a refusal.</para></summary>
    private (PdfObject RawEntry, PdfStream Form)? ResolveFormEntry(PdfDictionary annot)
    {
        if (ResolveObject(annot.Get("AP")) is not PdfDictionary apDict) return null;
        PdfObject? nRaw = apDict.Get("N");
        if (nRaw is null) return null;

        switch (ResolveObject(nRaw))
        {
            case PdfStream direct:
                return (nRaw, direct);

            case PdfDictionary stateDict:
                PdfStream? stateStream = ResolveStateKeyedStream(annot, stateDict);
                if (stateStream is null) return null;
                PdfObject? stateRaw = ResolveObject(annot.Get("AS")) is PdfName { Value: { } state }
                    ? stateDict.Get(state)
                    : null;
                return stateRaw is null ? null : (stateRaw, stateStream);

            default:
                return null;
        }
    }

    /// <summary>Builds the <c>q AA cm /Name Do Q</c> invocation ISO 32000-1 §12.5.5 describes,
    /// where <paramref name="aa"/> is the six-element matrix <see cref="AppearancePlacement.ComputeAA"/>
    /// computed. Mirrors <c>FormFlattener.FlattenField</c>'s own invocation formatting exactly
    /// (<see cref="CultureInfo.InvariantCulture"/>, <c>"G"</c> number format, Latin-1 bytes,
    /// trailing newline) so a baked annotation appearance and a baked widget appearance look the same
    /// kind of content stream, not two dialects.</summary>
    private static byte[] BuildAppearanceInvocation(double[] aa, string xobjectName)
    {
        string invocation = string.Format(
            CultureInfo.InvariantCulture,
            "q {0:G} {1:G} {2:G} {3:G} {4:G} {5:G} cm /{6} Do Q\n",
            aa[0], aa[1], aa[2], aa[3], aa[4], aa[5], xobjectName);
        return Encoding.Latin1.GetBytes(invocation);
    }

    /// <summary>Removes the annotation named by <paramref name="objectNumber"/> from
    /// <paramref name="page"/>'s <c>/Annots</c> array, dropping the key entirely once the array is
    /// empty. Matches an entry by indirect-reference object number, or — mirroring
    /// <c>FormFlattener.RemoveWidgetFromAnnots</c>'s belt-and-suspenders match — by the resolved
    /// dictionary's own object number, in case the array entry reaches it through another layer of
    /// indirection than a direct reference to it.</summary>
    private void RemoveAnnotationFromAnnots(PdfPage page, int objectNumber)
    {
        PdfArray? annots = page.GetAnnotations();
        if (annots is null) return;

        for (int i = annots.Count - 1; i >= 0; i--)
        {
            PdfObject entry = annots[i];
            bool match = entry is PdfIndirectReference ir && ir.ObjectNumber == objectNumber
                         || ResolveObject(entry) is PdfDictionary { IsIndirect: true } d
                            && d.ObjectNumber == objectNumber;
            if (match) annots.RemoveAt(i);
        }

        if (annots.Count == 0)
            page.Dictionary.Remove(new PdfName("Annots"));
    }

    /// <summary>Bakes every staged candidate's <c>/AP /N</c> appearance onto its owning page at the
    /// ISO 32000-1 §12.5.5 placement (<see cref="AppearancePlacement.ComputeAA"/>), then removes the
    /// annotation from that page's <c>/Annots</c> — the write side of this remediation program.
    /// Shares <see cref="EnumerateIndirectAnnotations"/> and <see cref="ClassifyAnnotationTypes"/>
    /// with <see cref="PreviewAnnotationTypeRepairs"/>, so preview and repair can never disagree about
    /// which annotations are structurally repairable (a resolvable Form XObject appearance, on a page
    /// that was found).
    ///
    /// <para><paramref name="objectNumbers"/> is the staged set, taken explicitly — unlike
    /// <c>RepairStreamFilters</c>/<c>RepairImageDictionaries</c>, there is no null-means-everything
    /// overload here (spec 2026-08-24-annotation-type-remediation-design.md §6): a caller that
    /// resolved <see langword="null"/> to "every offending annotation" would flatten and remove
    /// annotations the user never staged, or explicitly undid, and unlike a stream-filter conversion
    /// or a dictionary-key edit, this repair deletes content a viewer without 3D support was already
    /// hiding from the user — the wrong thing to do to an un-staged annotation is worse here than
    /// elsewhere in this family.</para>
    ///
    /// <para><b>This method refuses nothing <see cref="PreviewAnnotationTypeRepairs"/> did not
    /// already refuse.</b> Appearance geometry — <c>/Rect</c>, <c>/BBox</c>, <c>/Matrix</c>, and the
    /// transformed box <see cref="AppearancePlacement.ComputeAA"/> derives from them — is checked in
    /// the shared classifier via <see cref="ComputeAppearancePlacement"/>, so a document whose
    /// geometry cannot be placed is refused at preview time and never reported as a candidate at all.
    /// That property is not decoration: Pellucid's <c>AnnotationTypeDomain.CollectForSave</c> calls
    /// this method and discards the returned report, so a refusal only this method could produce
    /// reached no surface anywhere — the desktop row flipped to "fix applied", the file was rewritten,
    /// the finding survived the reload, and nothing said why. An earlier revision of this comment
    /// described that as "a second, later-arriving refusal reason, not a disagreement"; it was a
    /// disagreement, and it is closed.</para>
    ///
    /// <para>No page is touched — no XObject registered, no content appended, nothing removed from
    /// <c>/Annots</c> — for any annotation this method refuses; every mutation below happens only
    /// after every refusal check for that annotation has already passed.</para>
    ///
    /// <para>The owning page is resolved HERE, fresh, from the same
    /// <see cref="EnumerateIndirectAnnotations"/> call this method makes — never trusted from a
    /// cached <see cref="AnnotationTypeRepairCandidate.PageIndex"/> a caller might be holding from an
    /// earlier <see cref="PreviewAnnotationTypeRepairs"/> call.</para>
    ///
    /// <para>The enumeration is materialized into a list BEFORE any mutation begins, deliberately:
    /// <see cref="EnumerateIndirectAnnotations"/> is a generator that walks a page's live
    /// <c>/Annots</c> array while yielding from it, and <see cref="RemoveAnnotationFromAnnots"/>
    /// below removes entries from that very array. Mutating an array a <c>List&lt;T&gt;</c>-backed
    /// enumerator is still in the middle of walking — e.g. a page carrying two staged candidates —
    /// would throw, or silently skip an annotation, the same hazard any <c>foreach</c> over a
    /// collection has if the collection is edited mid-walk.</para>
    ///
    /// <para>The <c>/3DD</c> stream a real <c>/3D</c> annotation carries is never explicitly deleted:
    /// once the annotation itself is unreachable (nothing else in a clean document points at it), the
    /// <c>/3DD</c> stream it alone referenced becomes unreachable too, and the writer's own
    /// reachability walk (<c>ObjectGraphWalker</c>, <see cref="Save(System.IO.Stream, PdfSaveOptions?)"/>'s
    /// default <c>RemoveOrphans</c>) drops it on save — proven in
    /// <c>AnnotationTypeRepairTests</c>, not assumed here.</para></summary>
    public AnnotationTypeRepairReport RepairAnnotationTypes(IReadOnlySet<int> objectNumbers)
    {
        ArgumentNullException.ThrowIfNull(objectNumbers);

        List<(PdfDictionary Annotation, int PageIndex)> staged = EnumerateIndirectAnnotations()
            .Where(t => objectNumbers.Contains(t.Annotation.ObjectNumber))
            .ToList();

        var applied = new List<AnnotationTypeRepair>();
        var refusals = new List<AnnotationTypeRefusal>();
        List<PdfPage> pages = _document.GetPages();

        foreach ((PdfDictionary annot, int pageIndex) in staged)
        {
            var candidates = new List<AnnotationTypeRepairCandidate>();
            ClassifyAnnotationTypes(annot, pageIndex, candidates, refusals);
            if (candidates.Count == 0) continue; // refused by the shared classifier above, or (rare)
                                                   // no longer a violation at all -- either way, nothing
                                                   // to apply and nothing further to report here.

            AnnotationTypeRepairCandidate candidate = candidates[0];
            string subtype = candidate.Subtype;

            (PdfObject rawFormEntry, PdfStream form) = ResolveFormEntry(annot)
                ?? throw new InvalidOperationException(
                    $"ClassifyAnnotationTypes classified object {annot.ObjectNumber} as a repairable "
                    + "candidate (a resolvable /AP /N Form XObject), but RepairAnnotationTypes could "
                    + "not re-resolve it moments later on the same, unmutated-in-between annotation. "
                    + "This is a bug: the two must never disagree about what is resolvable.");

            // Defence in depth, and the value this method actually needs: ClassifyAnnotationTypes
            // above has ALREADY run these same checks (they are shared, so the two cannot say
            // different things), which is why reaching here at all means they passed. This call
            // re-runs them only because AA itself is what the invocation below is built from, and
            // AnnotationTypeRepairCandidate deliberately does not carry it. A refusal here would
            // therefore be a bug rather than a second opinion -- but it is reported rather than
            // thrown, and it happens before any mutation, so even that lands as a stated refusal on
            // an untouched page.
            double[]? aa = ComputeAppearancePlacement(annot, form, subtype, refusals);
            if (aa is null) continue;

            PdfPage page = pages[pageIndex];
            PdfIndirectReference formRef = rawFormEntry as PdfIndirectReference
                                            ?? _document.RegisterObject(form);
            string xobjectName = PageContentComposer.RegisterXObject(_document, page.Dictionary, formRef);
            byte[] invocation = BuildAppearanceInvocation(aa, xobjectName);
            PdfArray contents = PageContentComposer.EnsureContentsArray(_document, page.Dictionary);
            PageContentComposer.AddInvocation(_document, contents, invocation, underlay: false);

            RemoveAnnotationFromAnnots(page, annot.ObjectNumber);

            applied.Add(new AnnotationTypeRepair(annot.ObjectNumber, subtype, pageIndex));
        }

        return new AnnotationTypeRepairReport(applied, refusals);
    }
}
