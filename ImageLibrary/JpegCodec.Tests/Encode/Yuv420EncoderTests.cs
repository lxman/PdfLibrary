using JpegCodec.Internal;

namespace JpegCodec.Tests.Encode;

/// <summary>
/// Tests for the 4:2:0 chroma-subsampling encoder path.
/// </summary>
public class Yuv420EncoderTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static double ComputePsnr(byte[] reference, byte[] decoded, int length)
    {
        double sumSqErr = 0;
        for (var i = 0; i < length; i++)
        {
            int d = decoded[i] - reference[i];
            sumSqErr += d * d;
        }
        double mse = sumSqErr / length;
        return mse == 0 ? double.PositiveInfinity : 10.0 * Math.Log10(255.0 * 255.0 / mse);
    }

    /// <summary>
    /// Build a 64×64 RGB test image with a smooth gradient pattern.
    /// Smooth content is the worst case for chroma subsampling because the
    /// decoder upsamples with nearest-neighbour and the error shows.
    /// A PSNR floor of 28 dB at Q=85 is achievable for smooth content.
    /// </summary>
    private static byte[] MakeGradientRgb(int width, int height)
    {
        var data = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int i = (y * width + x) * 3;
                data[i + 0] = (byte)(x * 255 / (width - 1));
                data[i + 1] = (byte)(y * 255 / (height - 1));
                data[i + 2] = (byte)(((x + y) * 255) / (width + height - 2));
            }
        }
        return data;
    }

    // -----------------------------------------------------------------------
    // Core PSNR + size test
    // -----------------------------------------------------------------------

    [Fact]
    public void Encode420_Decode_MeetsPsnrFloor_AndSmallerThan444()
    {
        const int W = 64, H = 64;
        byte[] rgb = MakeGradientRgb(W, H);

        var opts444 = new JpegEncodeOptions
        {
            Width = W, Height = H, NumberOfComponents = 3, Quality = 85,
            ChromaSubsampling = ChromaSubsampling.Yuv444,
        };
        var opts420 = new JpegEncodeOptions
        {
            Width = W, Height = H, NumberOfComponents = 3, Quality = 85,
            ChromaSubsampling = ChromaSubsampling.Yuv420,
        };

        var encoder = new JpegStreamEncoder();
        byte[] jpeg444 = encoder.Encode(rgb, opts444);
        byte[] jpeg420 = encoder.Encode(rgb, opts420);

        // --- Decode 4:2:0 and check PSNR ---
        // The encoder converts RGB→YCbCr internally; the decoder returns YCbCr
        // component data (no inverse transform). To compute a meaningful PSNR,
        // convert the original RGB to YCbCr first so both sides are in the
        // same colour space. The 28 dB floor is on the YCbCr signal.
        JpegDecodeResult result = new JpegStreamDecoder().Decode(jpeg420);
        Assert.Equal(W, result.Width);
        Assert.Equal(H, result.Height);
        Assert.Equal(3, result.NumberOfComponents);
        Assert.Equal(W * H * 3, result.ComponentData.Length);

        byte[] ycbcrReference = YCbCrConverter.RgbToYCbCrInterleaved(rgb, W, H);
        double psnr = ComputePsnr(ycbcrReference, result.ComponentData, ycbcrReference.Length);
        Assert.True(psnr >= 28.0,
            $"4:2:0 round-trip PSNR {psnr:F2} dB (YCbCr space) is below the 28 dB floor.");

        // --- 4:2:0 must be smaller than 4:4:4 at same quality ---
        Assert.True(jpeg420.Length < jpeg444.Length,
            $"4:2:0 ({jpeg420.Length} bytes) is not smaller than 4:4:4 ({jpeg444.Length} bytes).");
    }

    // -----------------------------------------------------------------------
    // Structural validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Encode420_EmitsValidJpegStructure()
    {
        const int W = 16, H = 16;
        byte[] rgb = MakeGradientRgb(W, H);

        byte[] jpeg = new JpegStreamEncoder().Encode(rgb,
            new JpegEncodeOptions
            {
                Width = W, Height = H, NumberOfComponents = 3, Quality = 80,
                ChromaSubsampling = ChromaSubsampling.Yuv420,
            });

        Assert.True(jpeg.Length > 4);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);  // SOI
        Assert.Equal(0xFF, jpeg[^2]);
        Assert.Equal(0xD9, jpeg[^1]); // EOI
    }

    [Fact]
    public void Encode420_Identify_ReportsCorrectDimensions()
    {
        const int W = 48, H = 32;
        byte[] rgb = MakeGradientRgb(W, H);

        byte[] jpeg = new JpegStreamEncoder().Encode(rgb,
            new JpegEncodeOptions
            {
                Width = W, Height = H, NumberOfComponents = 3, Quality = 85,
                ChromaSubsampling = ChromaSubsampling.Yuv420,
            });

        JpegImageInfo info = new JpegStreamDecoder().Identify(jpeg);
        Assert.Equal(W, info.Width);
        Assert.Equal(H, info.Height);
        Assert.Equal(3, info.NumberOfComponents);
    }

    // -----------------------------------------------------------------------
    // Default is still 4:4:4
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultOptions_Still444_ResultUnchanged()
    {
        const int W = 32, H = 32;
        byte[] rgb = MakeGradientRgb(W, H);

        // Encode with explicit Yuv444 and with default (omitting ChromaSubsampling).
        var optsExplicit = new JpegEncodeOptions
        {
            Width = W, Height = H, NumberOfComponents = 3, Quality = 90,
            ChromaSubsampling = ChromaSubsampling.Yuv444,
        };
        var optsDefault = new JpegEncodeOptions
        {
            Width = W, Height = H, NumberOfComponents = 3, Quality = 90,
            // ChromaSubsampling intentionally omitted — must default to Yuv444.
        };

        var encoder = new JpegStreamEncoder();
        byte[] jpegExplicit = encoder.Encode(rgb, optsExplicit);
        byte[] jpegDefault  = encoder.Encode(rgb, optsDefault);

        // Both should produce identical bit-streams (same code path).
        Assert.Equal(jpegExplicit, jpegDefault);
    }

    // -----------------------------------------------------------------------
    // Non-multiple-of-16 dimensions
    // -----------------------------------------------------------------------

    [Fact]
    public void Encode420_OddDimensions_Decodes_WithoutThrowing()
    {
        // 30×22 is not a multiple of 16 — exercises edge-padding.
        const int W = 30, H = 22;
        byte[] rgb = MakeGradientRgb(W, H);

        var encoder = new JpegStreamEncoder();
        byte[] jpeg = encoder.Encode(rgb,
            new JpegEncodeOptions
            {
                Width = W, Height = H, NumberOfComponents = 3, Quality = 85,
                ChromaSubsampling = ChromaSubsampling.Yuv420,
            });

        JpegDecodeResult result = new JpegStreamDecoder().Decode(jpeg);
        Assert.Equal(W, result.Width);
        Assert.Equal(H, result.Height);
        Assert.Equal(W * H * 3, result.ComponentData.Length);
    }

    // -----------------------------------------------------------------------
    // ChromaSubsampling.Yuv420 on non-3-component input falls back to 4:4:4
    // -----------------------------------------------------------------------

    [Fact]
    public void Encode420_OnGrayscale_FallsBackTo444_NoThrow()
    {
        // Yuv420 only makes sense for 3-component colour. The encoder should
        // silently fall back to 4:4:4 for grayscale (nc=1) rather than throw.
        const int W = 16, H = 16;
        var gray = new byte[W * H];
        Array.Fill(gray, (byte)128);

        byte[] jpeg = new JpegStreamEncoder().Encode(gray,
            new JpegEncodeOptions
            {
                Width = W, Height = H, NumberOfComponents = 1, Quality = 85,
                ChromaSubsampling = ChromaSubsampling.Yuv420,
            });

        JpegDecodeResult result = new JpegStreamDecoder().Decode(jpeg);
        Assert.Equal(1, result.NumberOfComponents);
    }
}
