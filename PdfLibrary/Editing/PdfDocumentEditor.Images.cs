using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

/// <summary>A repair <see cref="PdfDocumentEditor.PreviewImageDictionaryRepairs"/> would apply to an
/// image XObject dictionary, PDF/A clause 6.2.8 (ISO 19005-2/3 6.2.8; calibrated against veraPDF's
/// PDFA-2 rules — see <c>PdfLibrary.Conformance.Rules.ImageDictionaryRule</c>). The write side (Task 2)
/// applies these under the same names.</summary>
public enum ImageDictionaryRepairKind
{
    /// <summary>Remove the image dictionary's /Alternates array.</summary>
    RemoveAlternates,

    /// <summary>Remove the image dictionary's /OPI entry.</summary>
    RemoveOpi,

    /// <summary>Neutralize a true /Interpolate — veraPDF 1.28.1 rules DELETE the key (Task 0): deleting
    /// and setting false both clear 6.2.8-3, so deletion is the cleaner form and is what the write side
    /// implements. This kind name intentionally does not say "delete" or "set false" — it names the
    /// effect, not the mechanism, so the preview and the eventual write agree on what happened without
    /// this type needing to change when Task 2 picks the mechanism.</summary>
    NeutralizeInterpolate,
}

/// <summary>One image XObject <see cref="PdfDocumentEditor.PreviewImageDictionaryRepairs"/> found
/// repairable, and every repair kind that would apply to it (an image can carry more than one 6.2.8
/// defect at once — e.g. both /OPI and a true /Interpolate).</summary>
public sealed record ImageDictionaryRepairCandidate(
    int ObjectNumber, IReadOnlyList<ImageDictionaryRepairKind> WouldApply);

/// <summary>One image XObject <see cref="PdfDocumentEditor.PreviewImageDictionaryRepairs"/> found a
/// defect on but declined to repair, with the reason a caller can surface verbatim.</summary>
public sealed record ImageDictionaryRefusal(
    int ObjectNumber, ImageDictionaryRepairKind Kind, string Reason);

/// <summary>What <see cref="PdfDocumentEditor.PreviewImageDictionaryRepairs"/> found, read-only: nothing
/// has been written to the document.</summary>
public sealed record ImageDictionaryRepairPreview(
    IReadOnlyList<ImageDictionaryRepairCandidate> Candidates,
    IReadOnlyList<ImageDictionaryRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private static readonly PdfName ImageSubtype = new("Image");

    /// <summary>Every indirect image XObject in the document. Deliberately the same set
    /// <c>ImageDictionaryRule</c> walks (all streams filtered to /Subtype /Image), so a finding the
    /// preflighter raised always has a repair candidate here.</summary>
    private IEnumerable<PdfStream> EnumerateImageXObjects()
    {
        _document.MaterializeAllObjects();
        foreach (PdfObject obj in _document.Objects.Values)
            if (obj is PdfStream { IsIndirect: true } stream
                && ResolveObject(stream.Dictionary.Get("Subtype")) is PdfName name
                && name.Value == ImageSubtype.Value)
                yield return stream;
    }

    /// <summary>Upper bound on how many /Alternates entries <see cref="AlternatesSafeToRemove"/>
    /// inspects — real alternate-image arrays are always small (a handful of resolutions at most); this
    /// only guards against a malformed or hostile document declaring an enormous array.</summary>
    private const int MaxAlternatesEntriesChecked = 10_000;

    /// <summary>ISO 32000-2 8.9.5.4 gives TWO routes by which /Alternates can override the base image
    /// instead of merely supplementing it, and deleting the array is unsafe on either:
    /// <list type="bullet">
    /// <item>(a)-(c), the optional-content route: when the base image carries /OC saying it is NOT
    /// visible, the first eligible entry in /Alternates renders INSTEAD.</item>
    /// <item>(d), the printing route: even with NO /OC at all, "if the base image does not contain an OC
    /// key and the PDF is being printed then the first entry in the Alternates array ... that has
    /// DefaultForPrinting set to true shall be selected" — printing uses that alternate INSTEAD of the
    /// base image.</item>
    /// </list>
    /// Deleting /Alternates on an image either route selects from would change what appears on screen or
    /// in print, possibly from something to nothing (route a-c) or from a designated print master to a
    /// lower-resolution proxy (route d).
    ///
    /// <para>Both checks are deliberately more conservative than the spec's precise wording, for the same
    /// reason: measured population of /Alternates in the 708-document corpus is ZERO, so a precise
    /// evaluator would buy nothing today while taking on real complexity — optional-content
    /// default-configuration visibility semantics for route (a)-(c), and print-eligibility filtering
    /// (e.g. resolution suitability) for route (d) — that no real document exists yet to validate against.
    /// Route (a)-(c) refuses whenever /OC is present at all, without evaluating visibility under the
    /// default configuration. Route (d) refuses whenever ANY /Alternates entry carries
    /// /DefaultForPrinting true, without regard to whether printing would actually select that particular
    /// entry. If a real document ever needs either loosened, tighten this predicate — each refusal reason
    /// names the condition that triggered it.</para>
    ///
    /// <para>A malformed /Alternates (present but not an array, once resolved) is treated as contributing
    /// no /DefaultForPrinting entries rather than thrown on or treated as an automatic refusal: this
    /// method cannot know what a non-array value means, so it degrades to the /OC-only check rather than
    /// guessing. That mirrors how the rest of this file's walks skip a node that doesn't resolve to the
    /// expected type (e.g. <see cref="EnumerateEmbeddedFilesTree"/>) instead of throwing.</para></summary>
    private bool AlternatesSafeToRemove(PdfDictionary imageDict, out string? reason)
    {
        if (imageDict.Get("OC") is not null)
        {
            reason = "This image's alternates cannot be removed safely: the image is governed by "
                     + "optional content, so a viewer may render one of the alternates in place of the "
                     + "image itself.";
            return false;
        }

        if (ResolveObject(imageDict.Get("Alternates")) is PdfArray alternates)
        {
            int count = Math.Min(alternates.Count, MaxAlternatesEntriesChecked);
            for (var i = 0; i < count; i++)
            {
                if (ResolveObject(alternates[i]) is PdfDictionary alt
                    && ResolveObject(alt.Get("DefaultForPrinting")) is PdfBoolean { Value: true })
                {
                    reason = "This image's alternates cannot be removed safely: one of them is marked "
                             + "/DefaultForPrinting true, so printing would select it in place of the "
                             + "image itself.";
                    return false;
                }
            }
        }

        reason = null;
        return true;
    }

    /// <summary>The ONE predicate both the preview and the eventual write (Task 2) use, so they can
    /// never disagree about what would happen to a given image — the same factoring
    /// <c>EnumerateFileSpecs</c>/<c>ClassifyFileSpecName</c> gives
    /// <c>RepairFileSpecNames</c>/<c>PreviewFileSpecNameRepairs</c> (PdfDocumentEditor.EmbeddedFiles.cs).</summary>
    private void ClassifyImageDictionary(
        PdfStream image, List<ImageDictionaryRepairKind> repairs, List<ImageDictionaryRefusal> refusals)
    {
        PdfDictionary dict = image.Dictionary;

        if (dict.Get("Alternates") is not null)
        {
            if (AlternatesSafeToRemove(dict, out string? reason))
                repairs.Add(ImageDictionaryRepairKind.RemoveAlternates);
            else
                refusals.Add(new ImageDictionaryRefusal(
                    image.ObjectNumber, ImageDictionaryRepairKind.RemoveAlternates, reason!));
        }

        if (dict.Get("OPI") is not null)
            repairs.Add(ImageDictionaryRepairKind.RemoveOpi);

        if (ResolveObject(dict.Get("Interpolate")) is PdfBoolean { Value: true })
            repairs.Add(ImageDictionaryRepairKind.NeutralizeInterpolate);
    }

    /// <summary>Read-only preview of every PDF/A 6.2.8 image-dictionary defect this editor would repair
    /// right now, without writing anything — the read side of this remediation program (a later task
    /// adds the write and a Pellucid domain that calls this). Calling it twice returns the same answer;
    /// there is no idempotency guard to trip because nothing here is ever written.</summary>
    public ImageDictionaryRepairPreview PreviewImageDictionaryRepairs()
    {
        var candidates = new List<ImageDictionaryRepairCandidate>();
        var refusals = new List<ImageDictionaryRefusal>();

        foreach (PdfStream image in EnumerateImageXObjects())
        {
            var repairs = new List<ImageDictionaryRepairKind>();
            ClassifyImageDictionary(image, repairs, refusals);
            if (repairs.Count > 0)
                candidates.Add(new ImageDictionaryRepairCandidate(image.ObjectNumber, repairs));
        }

        return new ImageDictionaryRepairPreview(candidates, refusals);
    }
}
