using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Security;
using PdfLibrary.Structure;

namespace PdfLibrary.Optimization;

/// <summary>
/// Optimizes a loaded PDF and writes the result. Runs model transforms over the in-memory
/// object graph, then serializes via PdfDocumentSerializer. Unencrypted documents only.
/// </summary>
public static class PdfOptimizer
{
    public static PdfOptimizationResult Optimize(PdfDocument document, Stream output, PdfOptimizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        options ??= PdfOptimizationOptions.Default;

        document.MaterializeAllObjects();
        int objectsBefore = document.Objects.Count;

        // Encrypted input -> unencrypted output: decrypt every stream in place and drop the decryptor
        // BEFORE any transform that reads stream data (compression / image / font passes all decode
        // without a decryptor). Strings are already plaintext (decrypted at parse time) and the
        // serializer omits /Encrypt, so the result is a valid unencrypted PDF.
        if (document.IsEncrypted)
            DecryptStreamsInPlace(document);

        int streamsCompressed = 0;
        if (options.CompressStreams)
            streamsCompressed = CompressUncompressedStreams(document);

        int imagesRecompressed = 0;
        if (options.RecompressImages)
            imagesRecompressed = RecompressImages(document, options);

        int fontsSubsetted = 0;
        if (options.SubsetFonts)
            fontsSubsetted = SubsetFonts(document, options);

        ISet<int>? live = options.RemoveUnusedObjects
            ? ObjectGraphWalker.CollectReachable(document)
            : null;

        long startPos = output.CanSeek ? output.Position : 0L;
        if (options.UseObjectStreams)
            ObjectStreamWriter.Write(document, output, live);
        else
            PdfDocumentSerializer.Write(document, output, live);

        return new PdfOptimizationResult
        {
            ObjectsBefore = objectsBefore,
            ObjectsAfter = live?.Count ?? objectsBefore,
            OutputBytes = output.CanSeek ? output.Position - startPos : 0L,
            StreamsCompressed = streamsCompressed,
            ImagesRecompressed = imagesRecompressed,
            FontsSubsetted = fontsSubsetted
        };
    }

    /// <summary>Optimizes a loaded PDF and writes the result to a file path.</summary>
    public static PdfOptimizationResult Optimize(PdfDocument document, string outputPath, PdfOptimizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(outputPath))
            throw new ArgumentException("Output path cannot be null or empty", nameof(outputPath));

        using FileStream stream = File.Create(outputPath);
        return Optimize(document, stream, options);
    }

    /// <summary>Flate-compresses every stream that currently has no filter (lossless). Already-encoded
    /// streams — images (DCTDecode), existing FlateDecode, filter arrays — are left untouched.</summary>
    internal static int CompressUncompressedStreams(PdfDocument document)
    {
        var count = 0;
        foreach (PdfObject obj in document.Objects.Values)
        {
            if (obj is not PdfStream s) continue;
            if (s.Dictionary.ContainsKey(PdfName.Filter)) continue; // already encoded
            s.SetEncodedData(s.GetDecodedData(), "FlateDecode");     // unfiltered -> raw bytes -> deflate
            count++;
        }
        return count;
    }

    internal static void CompressUncompressedStreamsForTest(PdfDocument document)
        => CompressUncompressedStreams(document);

    /// <summary>Turns an encrypted document into an equivalent unencrypted one: decrypts every indirect
    /// stream's raw data in place, then clears the decryptor. Must run after MaterializeAllObjects so
    /// every in-use stream is present. Strings are already decrypted at parse time, so only streams need
    /// handling; with the decryptor gone the serializer omits /Encrypt and the output is unencrypted.</summary>
    internal static void DecryptStreamsInPlace(PdfDocument document)
    {
        PdfDecryptor? decryptor = document.Decryptor;
        if (decryptor is null) return;

        foreach (PdfObject obj in document.Objects.Values)
        {
            if (obj is not PdfStream { IsIndirect: true } s) continue;
            s.Data = decryptor.Decrypt(s.Data, s.ObjectNumber, s.GenerationNumber);
        }

        document.ClearDecryptor();
    }

    /// <summary>Downsamples and re-compresses embedded image XObjects (Phase 3, image track):
    /// scans /Subtype /Image XObjects, downsamples via ImageLibrary's resampler and re-encodes,
    /// then writes the JPEG back via PdfStream.Data (DCTDecode has no SetEncodedData encoder) and
    /// patches the image dictionary. No-op stub — the real transform is built on the
    /// phase3-image branch.</summary>
    internal static int RecompressImages(PdfDocument document, PdfOptimizationOptions options)
    {
        var count = 0;
        foreach (PdfObject obj in document.Objects.Values)
        {
            if (obj is not PdfStream s) continue;
            if (!ImageRecompressor.IsImageRecompressible(s, document)) continue;
            try
            {
                if (ImageRecompressor.TryRecompress(s, document, options))
                    count++;
            }
            catch (Exception ex)
            {
                // Skip this image and continue — a corrupt or undecodable image stream
                // must not abort the entire Optimize pass.
                Logging.PdfLogger.Log(
                    Logging.LogCategory.Images,
                    $"RecompressImages: skipping object {s.ObjectNumber} due to error: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return count;
    }

    /// <summary>Subsets embedded TrueType (/FontFile2) programs to the glyphs actually used
    /// (Phase 3, font track): computes glyph usage from page content streams, subsets via
    /// FontParser's TrueTypeSubsetter, replaces /FontFile2 and keeps the font dictionary
    /// consistent.</summary>
    internal static int SubsetFonts(PdfDocument document, PdfOptimizationOptions options)
    {
        return FontSubsetter.Run(document, options);
    }
}
