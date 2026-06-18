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
        => throw new NotImplementedException();

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
}
