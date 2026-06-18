using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Optimization;

/// <summary>
/// Optimizes a loaded PDF and writes the result. Runs model transforms over the in-memory
/// object graph, then serializes via PdfDocumentSerializer. Unencrypted documents only.
/// </summary>
public static class PdfOptimizer
{
    public static void Optimize(PdfDocument document, Stream output, PdfOptimizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        options ??= PdfOptimizationOptions.Default;

        if (document.IsEncrypted)
            throw new NotSupportedException("Optimizing encrypted documents is not yet supported.");

        document.MaterializeAllObjects();

        if (options.CompressStreams)
            CompressUncompressedStreams(document);

        if (options.RecompressImages)
            RecompressImages(document, options);

        if (options.SubsetFonts)
            SubsetFonts(document, options);

        ISet<int>? live = options.RemoveUnusedObjects
            ? ObjectGraphWalker.CollectReachable(document)
            : null;

        if (options.UseObjectStreams)
            ObjectStreamWriter.Write(document, output, live);
        else
            PdfDocumentSerializer.Write(document, output, live);
    }

    /// <summary>Flate-compresses every stream that currently has no filter (lossless). Already-encoded
    /// streams — images (DCTDecode), existing FlateDecode, filter arrays — are left untouched.</summary>
    internal static void CompressUncompressedStreams(PdfDocument document)
    {
        foreach (PdfObject obj in document.Objects.Values)
        {
            if (obj is not PdfStream s) continue;
            if (s.Dictionary.ContainsKey(PdfName.Filter)) continue; // already encoded
            s.SetEncodedData(s.GetDecodedData(), "FlateDecode");     // unfiltered -> raw bytes -> deflate
        }
    }

    internal static void CompressUncompressedStreamsForTest(PdfDocument document)
        => CompressUncompressedStreams(document);

    /// <summary>Downsamples and re-compresses embedded image XObjects (Phase 3, image track):
    /// scans /Subtype /Image XObjects, downsamples via ImageLibrary's resampler and re-encodes,
    /// then writes the JPEG back via PdfStream.Data (DCTDecode has no SetEncodedData encoder) and
    /// patches the image dictionary. No-op stub — the real transform is built on the
    /// phase3-image branch.</summary>
    internal static void RecompressImages(PdfDocument document, PdfOptimizationOptions options)
    {
        // Intentionally empty until the Phase 3 image-recompression transform lands.
    }

    /// <summary>Subsets embedded TrueType (/FontFile2) programs to the glyphs actually used
    /// (Phase 3, font track): computes glyph usage from page content streams, subsets via
    /// FontParser's TrueTypeSubsetter, replaces /FontFile2 and keeps the font dictionary
    /// consistent.</summary>
    internal static void SubsetFonts(PdfDocument document, PdfOptimizationOptions options)
    {
        FontSubsetter.Run(document, options);
    }
}
