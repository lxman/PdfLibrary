using JpegCodec;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Optimization;
using Xunit;

namespace PdfLibrary.Tests.Optimization;

/// <summary>
/// Unit tests for ImageRecompressor.TryRecompress.
/// </summary>
public class ImageRecompressorReencodeTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a smooth RGB gradient (horizontal sweep R, vertical G, B=128)
    /// to give the JPEG encoder enough variation to beat a raw FlateDecode.
    /// </summary>
    private static byte[] MakeRgbGradient(int w, int h)
    {
        var pixels = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                int i = (y * w + x) * 3;
                pixels[i]     = (byte)(x * 255 / (w - 1)); // R ramp
                pixels[i + 1] = (byte)(y * 255 / (h - 1)); // G ramp
                pixels[i + 2] = 128;                        // B flat
            }
        }
        return pixels;
    }

    private static byte[] MakeGrayGradient(int w, int h)
    {
        var pixels = new byte[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                pixels[y * w + x] = (byte)(x * 255 / (w - 1));
            }
        }
        return pixels;
    }

    /// <summary>
    /// Creates a FlateDecode-compressed image stream from raw pixels.
    /// </summary>
    private static PdfStream MakeFlatStream(byte[] rawPixels, int w, int h, string csName)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")]  = new PdfName("Image"),
            [PdfName.Width]            = new PdfInteger(w),
            [PdfName.Height]           = new PdfInteger(h),
            [PdfName.ColorSpace]       = new PdfName(csName),
            [PdfName.BitsPerComponent] = new PdfInteger(8),
        };
        var s = new PdfStream(dict, rawPixels);
        // Compress with FlateDecode so the raw bytes become the encoded stream.
        s.SetEncodedData(rawPixels, "FlateDecode");
        return s;
    }

    /// <summary>
    /// Creates a DCTDecode image stream from raw pixels (pre-encoded as JPEG).
    /// </summary>
    private static PdfStream MakeDctStream(byte[] rawPixels, int w, int h, string csName, int quality = 90)
    {
        int channels = csName == "DeviceGray" ? 1 : 3;
        byte[] jpeg = new JpegStreamEncoder().Encode(rawPixels, new JpegEncodeOptions
        {
            Width               = w,
            Height              = h,
            NumberOfComponents  = channels,
            Quality             = quality,
            ChromaSubsampling   = channels == 3 ? ChromaSubsampling.Yuv444 : ChromaSubsampling.Yuv444,
        });
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")]  = new PdfName("Image"),
            [PdfName.Filter]           = new PdfName("DCTDecode"),
            [PdfName.Width]            = new PdfInteger(w),
            [PdfName.Height]           = new PdfInteger(h),
            [PdfName.ColorSpace]       = new PdfName(csName),
            [PdfName.BitsPerComponent] = new PdfInteger(8),
        };
        var s = new PdfStream(dict, jpeg)
        {
            Data = jpeg
        };
        return s;
    }

    private static PdfOptimizationOptions DefaultOpts() =>
        new() { ImageJpegQuality = 75, MaxImagePixelDimension = 0 };

    // ── Test (a): large RGB FlateDecode recompresses successfully ──────────────

    [Fact]
    public void TryRecompress_LargeRgbFlate_ReturnsTrue_AndFilterIsDct()
    {
        const int W = 256, H = 256;
        byte[] pixels = MakeRgbGradient(W, H);
        PdfStream s = MakeFlatStream(pixels, W, H, "DeviceRGB");
        int originalLen = s.Length;

        bool result = ImageRecompressor.TryRecompress(s, null, DefaultOpts());

        Assert.True(result, "TryRecompress should return true for a large RGB FlateDecode image");

        // Filter must now be DCTDecode.
        Assert.True(s.Dictionary.TryGetValue(PdfName.Filter, out PdfObject filterObj));
        Assert.IsType<PdfName>(filterObj);
        Assert.Equal("DCTDecode", ((PdfName)filterObj).Value);

        // Encoded stream must be smaller.
        Assert.True(s.Length < originalLen,
            $"JPEG ({s.Length}) should be smaller than FlateDecode ({originalLen})");
    }

    [Fact]
    public void TryRecompress_LargeRgbFlate_DecodeParms_Removed()
    {
        const int W = 256, H = 256;
        byte[] pixels = MakeRgbGradient(W, H);
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")]  = new PdfName("Image"),
            [PdfName.Width]            = new PdfInteger(W),
            [PdfName.Height]           = new PdfInteger(H),
            [PdfName.ColorSpace]       = new PdfName("DeviceRGB"),
            [PdfName.BitsPerComponent] = new PdfInteger(8),
        };
        var s = new PdfStream(dict, pixels);
        s.SetEncodedData(pixels, "FlateDecode");
        // Artificially inject a DecodeParms entry.
        s.Dictionary[PdfName.DecodeParms] = new PdfDictionary();

        ImageRecompressor.TryRecompress(s, null, DefaultOpts());

        Assert.False(s.Dictionary.ContainsKey(PdfName.DecodeParms),
            "/DecodeParms must be removed after JPEG write-back");
    }

    // ── Test (b): size guard — uniform image left unchanged ───────────────────

    [Fact]
    public void TryRecompress_SizeGuard_LeavesImageUnchanged_WhenJpegIsNotSmaller()
    {
        // A 128x128 perfectly uniform image. Flate collapses to ~100 bytes;
        // JPEG header alone is ~500 bytes, so JPEG will be larger.
        const int W = 128, H = 128;
        var pixels = new byte[W * H * 3];
        Array.Fill(pixels, (byte)200); // uniform single-value image

        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")]  = new PdfName("Image"),
            [PdfName.Width]            = new PdfInteger(W),
            [PdfName.Height]           = new PdfInteger(H),
            [PdfName.ColorSpace]       = new PdfName("DeviceRGB"),
            [PdfName.BitsPerComponent] = new PdfInteger(8),
        };
        var s = new PdfStream(dict, pixels);
        s.SetEncodedData(pixels, "FlateDecode");  // very compressible uniform data

        int beforeLen = s.Length;
        bool result = ImageRecompressor.TryRecompress(s, null, DefaultOpts());

        // Should return false (JPEG will be larger than the FlateDecode of uniform data).
        Assert.False(result, "Size guard should prevent replacing when JPEG is not smaller");
        Assert.Equal(beforeLen, s.Length);
        // Filter must remain FlateDecode.
        Assert.True(s.Dictionary.TryGetValue(PdfName.Filter, out PdfObject f));
        Assert.Equal("FlateDecode", ((PdfName)f).Value);
    }

    // ── Test (c): downsampling path ────────────────────────────────────────────

    [Fact]
    public void TryRecompress_Downsample_ReducesDimensions_PreservesAspect()
    {
        const int W = 512, H = 256;   // wide image
        byte[] pixels = MakeRgbGradient(W, H);
        PdfStream s = MakeFlatStream(pixels, W, H, "DeviceRGB");

        var opts = new PdfOptimizationOptions
        {
            ImageJpegQuality       = 75,
            MaxImagePixelDimension = 200, // should scale so larger side (512) → 200
        };

        bool result = ImageRecompressor.TryRecompress(s, null, opts);

        // Must succeed (gradient image will compress well enough after downsample).
        Assert.True(result, "TryRecompress with downsample should succeed");

        int newW = ((PdfInteger)s.Dictionary[PdfName.Width]).Value;
        int newH = ((PdfInteger)s.Dictionary[PdfName.Height]).Value;

        // Larger side must be at most MaxImagePixelDimension.
        Assert.True(newW <= 200 && newH <= 200,
            $"After downsample expected ≤200×200, got {newW}×{newH}");

        // Aspect ratio preserved: W:H = 512:256 = 2:1 so newW should be ~2*newH.
        double aspect = (double)newW / newH;
        Assert.InRange(aspect, 1.9, 2.1);
    }

    [Fact]
    public void TryRecompress_Downsample_TallImage_LargerSideIsHeight()
    {
        const int W = 128, H = 512; // tall image
        byte[] pixels = MakeRgbGradient(W, H);
        PdfStream s = MakeFlatStream(pixels, W, H, "DeviceRGB");

        var opts = new PdfOptimizationOptions
        {
            ImageJpegQuality       = 75,
            MaxImagePixelDimension = 200,
        };

        bool result = ImageRecompressor.TryRecompress(s, null, opts);
        Assert.True(result, "TryRecompress with tall-image downsample should succeed");

        int newH = ((PdfInteger)s.Dictionary[PdfName.Height]).Value;
        Assert.True(newH <= 200, $"Height should be <= 200, was {newH}");
    }

    [Fact]
    public void TryRecompress_NoDownsample_WhenBelowCap()
    {
        // Use a large RGB gradient so JPEG beats FlateDecode — ensures the size guard
        // passes and we can verify dimensions are not changed when image is below the cap.
        const int W = 512, H = 256;
        byte[] pixels = MakeRgbGradient(W, H);
        PdfStream s = MakeFlatStream(pixels, W, H, "DeviceRGB");

        // Cap is larger than the larger side (512) — no resize should occur.
        var opts = new PdfOptimizationOptions
        {
            ImageJpegQuality       = 75,
            MaxImagePixelDimension = 600,
        };

        bool result = ImageRecompressor.TryRecompress(s, null, opts);
        Assert.True(result, "TryRecompress below cap should recompress the FlateDecode image (size guard must pass for this gradient)");

        int newW = ((PdfInteger)s.Dictionary[PdfName.Width]).Value;
        int newH = ((PdfInteger)s.Dictionary[PdfName.Height]).Value;
        Assert.Equal(W, newW);
        Assert.Equal(H, newH);
    }

    // ── Fix 3: DCTDecode no-cap is left untouched ─────────────────────────────

    [Fact]
    public void TryRecompress_DctSource_NoCap_ReturnsFalse_FilterUnchanged()
    {
        // A DCTDecode source with no pixel cap — re-encoding would be pure quality loss;
        // TryRecompress must return false and leave the stream untouched.
        const int W = 256, H = 256;
        byte[] pixels = MakeRgbGradient(W, H);
        PdfStream s = MakeDctStream(pixels, W, H, "DeviceRGB", quality: 90);
        byte[] originalBytes = s.Data!.ToArray();
        int originalLen = s.Length;

        bool result = ImageRecompressor.TryRecompress(s, null, DefaultOpts()); // MaxImagePixelDimension = 0

        Assert.False(result, "DCTDecode source with no cap must not be re-encoded (second-generation loss)");
        Assert.True(s.Dictionary.TryGetValue(PdfName.Filter, out PdfObject f));
        Assert.Equal("DCTDecode", ((PdfName)f).Value);
        Assert.Equal(originalLen, s.Length);
    }

    [Fact]
    public void TryRecompress_DctSource_WithCapBelowLargerDim_IsDownsampled()
    {
        // A DCTDecode source where the cap triggers a downsample — should be recompressed.
        const int W = 512, H = 256;
        byte[] pixels = MakeRgbGradient(W, H);
        PdfStream s = MakeDctStream(pixels, W, H, "DeviceRGB", quality: 90);

        var opts = new PdfOptimizationOptions
        {
            ImageJpegQuality       = 75,
            MaxImagePixelDimension = 200, // larger side 512 > 200 → downsample applies
        };

        bool result = ImageRecompressor.TryRecompress(s, null, opts);
        Assert.True(result, "DCTDecode source with cap below larger dimension should be downsampled and recompressed");

        int newW = ((PdfInteger)s.Dictionary[PdfName.Width]).Value;
        int newH = ((PdfInteger)s.Dictionary[PdfName.Height]).Value;
        Assert.True(newW <= 200 && newH <= 200,
            $"After downsample expected ≤200×200, got {newW}×{newH}");
        Assert.True(s.Dictionary.TryGetValue(PdfName.Filter, out PdfObject f));
        Assert.Equal("DCTDecode", ((PdfName)f).Value);
    }

    // ── Size measurement (captured as assertion message) ──────────────────────

    [Fact]
    public void Measure_256x256_Rgb_FlateThenJpeg_SizesForReport()
    {
        const int W = 256, H = 256;
        byte[] pixels = MakeRgbGradient(W, H);
        PdfStream s = MakeFlatStream(pixels, W, H, "DeviceRGB");
        int before = s.Length;
        ImageRecompressor.TryRecompress(s, null, DefaultOpts());
        int after = s.Length;
        // Record sizes in assertion message so they appear in test output.
        Assert.True(after < before,
            $"SIZE_REPORT: FlateDecode={before} JPEG75={after} ratio={after * 100.0 / before:F1}%");
    }
}
