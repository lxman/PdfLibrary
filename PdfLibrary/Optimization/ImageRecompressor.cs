using ImageResampling;
using JpegCodec;
using Logging;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Optimization;

/// <summary>
/// Eligibility predicate and re-encode logic for image XObject recompression
/// (Phase 3, image track). Isolated here so it can be unit-tested independently
/// of the full optimizer pipeline.
/// </summary>
internal static class ImageRecompressor
{
    // Accepted color-space names and their channel counts.
    private static readonly HashSet<string> RgbNames  = ["DeviceRGB", "RGB"];
    private static readonly HashSet<string> GrayNames = ["DeviceGray", "G"];

    // ── Public surface ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when <paramref name="s"/> is safe to re-encode as JPEG.
    /// ALL of the following must hold:
    /// <list type="bullet">
    ///   <item>The stream is an image XObject (<c>/Subtype /Image</c>).</item>
    ///   <item><c>/BitsPerComponent</c> is exactly 8.</item>
    ///   <item><c>/Filter</c> is a single <see cref="PdfName"/> equal to
    ///         <c>DCTDecode</c> or <c>FlateDecode</c> (arrays are rejected).</item>
    ///   <item><c>/ColorSpace</c> (indirect references resolved) is a bare
    ///         <see cref="PdfName"/> in the DeviceRGB/RGB or DeviceGray/G sets.</item>
    ///   <item>No <c>/SMask</c>, <c>/ImageMask</c>, or <c>/Decode</c> entry.</item>
    ///   <item><c>Width × Height ≥ 16384</c> (images smaller than 128×128 are skipped).</item>
    /// </list>
    /// </summary>
    internal static bool IsImageRecompressible(PdfStream s, PdfDocument? document)
    {
        // 1. Must be an image XObject.
        if (!PdfImage.IsImageXObject(s)) return false;

        // 2. BitsPerComponent must be exactly 8.
        if (!s.Dictionary.TryGetValue(PdfName.BitsPerComponent, out PdfObject bpcObj) ||
            bpcObj is not PdfInteger { Value: 8 })
            return false;

        // 3. Filter must be a single PdfName equal to DCTDecode or FlateDecode.
        if (!s.Dictionary.TryGetValue(PdfName.Filter, out PdfObject filterObj))
            return false;
        if (filterObj is not PdfName filterName)
            return false; // reject arrays
        if (filterName.Value is not ("DCTDecode" or "FlateDecode"))
            return false;

        // 4. ColorSpace must resolve to a bare PdfName in the accepted sets.
        if (!s.Dictionary.TryGetValue(PdfName.ColorSpace, out PdfObject? csObj))
            return false;

        // Resolve indirect reference if a document was provided.
        if (csObj is PdfIndirectReference csRef && document is not null)
            csObj = document.ResolveReference(csRef);

        if (csObj is not PdfName csName) return false; // reject array color spaces
        if (!RgbNames.Contains(csName.Value) && !GrayNames.Contains(csName.Value))
            return false;

        // 5. No soft mask, image mask, color-key/stencil mask, or decode array.
        if (s.Dictionary.ContainsKey(new PdfName("SMask")))    return false;
        if (s.Dictionary.ContainsKey(new PdfName("ImageMask"))) return false;
        if (s.Dictionary.ContainsKey(new PdfName("Mask")))     return false;
        if (s.Dictionary.ContainsKey(new PdfName("Decode")))   return false;

        // 6. Pixel count must be at least 16384 (128×128).
        if (!s.Dictionary.TryGetValue(PdfName.Width,  out PdfObject wObj) || wObj  is not PdfInteger wInt) return false;
        if (!s.Dictionary.TryGetValue(PdfName.Height, out PdfObject hObj) || hObj is not PdfInteger hInt) return false;
        if ((long)wInt.Value * hInt.Value < 16384) return false;

        return true;
    }

    /// <summary>
    /// Attempts to re-encode <paramref name="s"/> as a smaller JPEG.
    /// Returns <c>true</c> if the stream was replaced; <c>false</c> if it was
    /// left unchanged (source is DCTDecode with no cap triggered, size guard, or codec error).
    /// </summary>
    internal static bool TryRecompress(PdfStream s, PdfDocument? document,
        PdfOptimizationOptions options)
    {
        // Determine channel count.
        s.Dictionary.TryGetValue(PdfName.ColorSpace, out PdfObject? csObj);
        if (csObj is PdfIndirectReference csRef && document is not null)
            csObj = document.ResolveReference(csRef);
        int channels = csObj is PdfName csName && GrayNames.Contains(csName.Value) ? 1 : 3;

        // Read original dimensions.
        int w = ((PdfInteger)s.Dictionary[PdfName.Width]).Value;
        int h = ((PdfInteger)s.Dictionary[PdfName.Height]).Value;

        // For DCTDecode sources, only proceed if a pixel-cap downsample will actually happen.
        // Re-encoding an already-JPEG stream at the same quality is a pointless second-generation
        // quality loss; skip it unless the image also needs downsampling.
        if (s.Dictionary.TryGetValue(PdfName.Filter, out PdfObject? srcFilterObj) &&
            srcFilterObj is PdfName { Value: "DCTDecode" })
        {
            bool willDownsample = options.MaxImagePixelDimension > 0 &&
                                  Math.Max(w, h) > options.MaxImagePixelDimension;
            if (!willDownsample)
                return false;
        }

        // Record original encoded size BEFORE decoding (avoids touching the data setter).
        int originalLen = s.Length;

        // Decode to raw pixels.
        byte[] pixels = s.GetDecodedData();

        // Optional absolute pixel-cap downsample.
        if (options.MaxImagePixelDimension > 0)
        {
            int cap = options.MaxImagePixelDimension;
            int larger = Math.Max(w, h);
            if (larger > cap)
            {
                double scale = (double)cap / larger;
                int nw = Math.Max(1, (int)Math.Round(w * scale));
                int nh = Math.Max(1, (int)Math.Round(h * scale));
                pixels = ImageResampler.Resample(pixels, w, h, channels, nw, nh);
                w = nw;
                h = nh;
            }
        }

        // Encode as JPEG.
        ChromaSubsampling subsampling = channels == 3
            ? ChromaSubsampling.Yuv420
            : ChromaSubsampling.Yuv444;

        byte[] jpeg = new JpegStreamEncoder().Encode(pixels, new JpegEncodeOptions
        {
            Width               = w,
            Height              = h,
            NumberOfComponents  = channels,
            Quality             = options.ImageJpegQuality,
            ChromaSubsampling   = subsampling,
        });

        // Size guard: only replace when the JPEG is strictly smaller.
        if (jpeg.Length >= originalLen) return false;

        // Write-back: assign encoded bytes, patch the dictionary.
        s.Data = jpeg;
        s.Dictionary[PdfName.Filter]           = new PdfName("DCTDecode");
        s.Dictionary.Remove(PdfName.DecodeParms);
        s.Dictionary[PdfName.Width]            = new PdfInteger(w);
        s.Dictionary[PdfName.Height]           = new PdfInteger(h);

        return true;
    }
}
