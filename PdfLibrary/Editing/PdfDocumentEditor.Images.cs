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

    /// <summary>ISO 32000-2 8.9.5.4: when a base image carries /OC saying it is NOT visible, the first
    /// eligible entry in /Alternates renders INSTEAD. Deleting the array on such an image would change
    /// what is on the page, possibly from something to nothing.
    ///
    /// <para>This refuses whenever /OC is present at all, rather than evaluating visibility under the
    /// default configuration. That is deliberately more conservative than the spec's wording: measured
    /// population of /Alternates in the 708-document corpus is ZERO, so a precise evaluator would buy
    /// nothing today while taking a dependency on optional-content default-configuration semantics. If a
    /// real document ever needs it, tighten this predicate — the refusal reason names the condition.</para></summary>
    private bool AlternatesSafeToRemove(PdfDictionary imageDict, out string? reason)
    {
        if (imageDict.Get("OC") is null) { reason = null; return true; }
        reason = "This image's alternates cannot be removed safely: the image is governed by optional "
                 + "content, so a viewer may render one of the alternates in place of the image itself.";
        return false;
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
