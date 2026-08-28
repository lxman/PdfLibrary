using PdfLibrary.Conformance;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

/// <summary>One indirect page-content stream whose inline-image interpolation flags can be changed
/// from true to false without re-emitting any content operators.</summary>
public sealed record InlineImageRepairCandidate(
    int ObjectNumber,
    IReadOnlyList<int> PageNumbers,
    int ImageCount);

/// <summary>An inline-image defect that the deliberately narrow repair cannot safely rewrite.</summary>
public sealed record InlineImageRepairRefusal(int? ObjectNumber, string Reason);

/// <summary>Read-only classification of the document's reachable inline-image defects.</summary>
public sealed record InlineImageRepairPreview(
    IReadOnlyList<InlineImageRepairCandidate> Candidates,
    IReadOnlyList<InlineImageRepairRefusal> Refused);

/// <summary>One content stream changed by <see cref="PdfDocumentEditor.RepairInlineImages"/>.</summary>
public sealed record InlineImageRepair(int ObjectNumber, int ImageCount);

/// <summary>What <see cref="PdfDocumentEditor.RepairInlineImages"/> changed and declined.</summary>
public sealed record InlineImageRepairReport(
    IReadOnlyList<InlineImageRepair> Applied,
    IReadOnlyList<InlineImageRepairRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private static readonly HashSet<string> PermittedInlineImageFilters = new(StringComparer.Ordinal)
    {
        "ASCIIHexDecode", "ASCII85Decode", "FlateDecode", "RunLengthDecode", "CCITTFaxDecode", "DCTDecode",
        "AHx", "A85", "Fl", "RL", "CCF", "DCT",
    };

    private sealed record InlineImageStreamRepair(
        PdfStream Stream,
        IReadOnlyList<int> PageNumbers,
        byte[] Decoded,
        IReadOnlyList<int> TrueTokenOffsets);

    private sealed record InlineImageClassification(
        IReadOnlyList<InlineImageStreamRepair> Repairs,
        IReadOnlyList<InlineImageRepairRefusal> Refused);

    /// <summary>Classifies against individual page streams, rather than the page's concatenated
    /// content. This is the safety boundary: a token offset is meaningful only in the exact decoded
    /// byte array that will be written back. Form XObjects, split BI/ID/EI constructs, direct streams,
    /// and non-Flate encodings are therefore refused rather than reconstructed.</summary>
    private InlineImageClassification ClassifyInlineImages()
    {
        _document.MaterializeAllObjects();
        var context = new ConformanceContext(_document, ConformanceProfile.PdfA2b);
        List<InlineImageOperator> reachable = ContentWalk.ReachableOperators(context)
            .OfType<InlineImageOperator>()
            .ToList();
        int reachableInterpolateViolations = reachable.Count(image => image.Interpolate);
        int reachableFilterViolations = reachable.Sum(image => InlineImageFilterNames(context, image.Parameters)
            .Count(name => !PermittedInlineImageFilters.Contains(name)));
        if (reachableInterpolateViolations == 0 && reachableFilterViolations == 0)
            return new InlineImageClassification([], []);

        if (HasSignatureProtection(context))
            return new InlineImageClassification([], [new InlineImageRepairRefusal(
                null,
                "Inline images were left unchanged because the document carries a signed signature "
                + "or DocMDP permission. Pellucid performs a full rewrite and does not claim to preserve it.")]);

        var owners = new Dictionary<PdfStream, List<int>>(ReferenceEqualityComparer.Instance);
        for (var pageIndex = 0; pageIndex < context.Pages.Count; pageIndex++)
        {
            foreach (PdfStream stream in context.Pages[pageIndex].GetContents())
            {
                if (!owners.TryGetValue(stream, out List<int>? pages))
                    owners.Add(stream, pages = []);
                if (!pages.Contains(pageIndex + 1))
                    pages.Add(pageIndex + 1);
            }
        }

        var repairs = new List<InlineImageStreamRepair>();
        var refusals = new List<InlineImageRepairRefusal>();
        var locallySeenInterpolateViolations = 0;
        var locallySeenFilterViolations = 0;

        foreach ((PdfStream stream, List<int> pages) in owners)
        {
            byte[] decoded;
            List<InlineImageOperator> images;
            try
            {
                decoded = stream.GetDecodedData(_document.Decryptor);
                images = PdfContentParser.Parse(decoded).OfType<InlineImageOperator>().ToList();
            }
            catch
            {
                // A reachable violation in an unparseable individual stream is caught by the
                // reachable-vs-local accounting check below; conforming malformed content is not
                // this repair program's concern.
                continue;
            }

            if (images.Count == 0) continue;

            var offsets = new List<int>();
            foreach (InlineImageOperator image in images)
            {
                List<string> forbiddenOccurrences = InlineImageFilterNames(context, image.Parameters)
                    .Where(name => !PermittedInlineImageFilters.Contains(name))
                    .ToList();
                locallySeenFilterViolations += forbiddenOccurrences.Count;
                if (forbiddenOccurrences.Count > 0)
                {
                    List<string> forbidden = forbiddenOccurrences.Distinct(StringComparer.Ordinal).ToList();
                    refusals.Add(new InlineImageRepairRefusal(
                        stream.IsIndirect ? stream.ObjectNumber : null,
                        $"An inline image in page content uses {string.Join(", ", forbidden.Select(n => "/" + n))}. "
                        + "This program repairs /I true only; it will not transcode inline-image payloads."));
                }

                if (!image.Interpolate) continue;
                locallySeenInterpolateViolations++;

                if (image.InterpolateKeyCount != 1
                    || image.InterpolateValueOffset is not long offset
                    || image.InterpolateValueLength != 4
                    || offset < 0
                    || offset > decoded.Length - 4
                    || !decoded.AsSpan((int)offset, 4).SequenceEqual("true"u8))
                {
                    refusals.Add(new InlineImageRepairRefusal(
                        stream.IsIndirect ? stream.ObjectNumber : null,
                        "An inline image sets interpolation true, but its source has duplicate aliases or "
                        + "does not expose one exact Boolean token. Rewriting it would require re-serializing content."));
                    continue;
                }

                offsets.Add((int)offset);
            }

            if (offsets.Count == 0) continue;

            string? unsupported = stream switch
            {
                { IsIndirect: false } => "The containing page-content stream is direct rather than independently addressable.",
                _ when stream.Dictionary.Get("Filter") is not PdfName { Value: "FlateDecode" } =>
                    "The containing page-content stream is not encoded by one direct /FlateDecode filter.",
                _ when stream.Dictionary.Get("DecodeParms") is not null =>
                    "The containing page-content stream has /DecodeParms, so its exact Ford-proven encoding shape does not apply.",
                _ => null,
            };

            if (unsupported is not null)
            {
                refusals.Add(new InlineImageRepairRefusal(stream.IsIndirect ? stream.ObjectNumber : null, unsupported));
                continue;
            }

            repairs.Add(new InlineImageStreamRepair(stream, pages, decoded, offsets));
        }

        if (reachableInterpolateViolations != locallySeenInterpolateViolations
            || reachableFilterViolations != locallySeenFilterViolations)
            refusals.Add(new InlineImageRepairRefusal(
                null,
                "Some reachable inline-image defects are not owned by one independently parseable page-content "
                + "stream (for example, they may be in an invoked Form or span a stream boundary), so they were left unchanged."));

        return new InlineImageClassification(repairs, refusals);
    }

    private static IEnumerable<string> InlineImageFilterNames(
        ConformanceContext context,
        PdfDictionary parameters)
    {
        PdfObject? filter = context.Resolve(parameters.Get("F")) ?? context.Resolve(parameters.Get("Filter"));
        switch (filter)
        {
            case PdfName name:
                yield return name.Value;
                break;
            case PdfArray array:
                foreach (PdfObject element in array)
                    if (context.Resolve(element) is PdfName elementName)
                        yield return elementName.Value;
                break;
        }
    }

    /// <summary>Reports every safely patchable /I true stream and every unsupported reachable defect,
    /// without mutating the document.</summary>
    public InlineImageRepairPreview PreviewInlineImageRepairs()
    {
        InlineImageClassification classification = ClassifyInlineImages();
        return new InlineImageRepairPreview(
            classification.Repairs.Select(repair => new InlineImageRepairCandidate(
                repair.Stream.ObjectNumber, repair.PageNumbers, repair.TrueTokenOffsets.Count)).ToList(),
            classification.Refused);
    }

    /// <summary>Changes only the source bytes of exact interpolation Boolean tokens from
    /// <c>true</c> to <c>false</c>, re-encoding the otherwise byte-identical decoded stream with
    /// /FlateDecode. Classification is repeated against the live graph at write time.</summary>
    public InlineImageRepairReport RepairInlineImages(IReadOnlySet<int>? objectNumbers = null)
    {
        InlineImageClassification classification = ClassifyInlineImages();
        var applied = new List<InlineImageRepair>();

        foreach (InlineImageStreamRepair repair in classification.Repairs)
        {
            if (objectNumbers is not null && !objectNumbers.Contains(repair.Stream.ObjectNumber)) continue;

            byte[] changed = ReplaceTrueTokens(repair.Decoded, repair.TrueTokenOffsets);
            repair.Stream.SetEncodedData(changed, "FlateDecode");
            applied.Add(new InlineImageRepair(repair.Stream.ObjectNumber, repair.TrueTokenOffsets.Count));
        }

        var refused = classification.Refused.ToList();
        if (objectNumbers is not null)
        {
            HashSet<int> liveCandidates = classification.Repairs.Select(r => r.Stream.ObjectNumber).ToHashSet();
            foreach (int requested in objectNumbers.Where(number => !liveCandidates.Contains(number)))
                refused.Add(new InlineImageRepairRefusal(
                    requested,
                    "This stream is no longer an inline-image repair candidate in the live document; it was not changed."));
        }

        return new InlineImageRepairReport(applied, refused);
    }

    private static byte[] ReplaceTrueTokens(byte[] source, IReadOnlyList<int> offsets)
    {
        int[] ordered = offsets.Order().ToArray();
        var result = new byte[source.Length + ordered.Length];
        var sourceAt = 0;
        var targetAt = 0;
        foreach (int offset in ordered)
        {
            int unchanged = offset - sourceAt;
            source.AsSpan(sourceAt, unchanged).CopyTo(result.AsSpan(targetAt));
            targetAt += unchanged;
            "false"u8.CopyTo(result.AsSpan(targetAt));
            targetAt += 5;
            sourceAt = offset + 4;
        }
        source.AsSpan(sourceAt).CopyTo(result.AsSpan(targetAt));
        return result;
    }
}
