using ImageResampling;
using JpegCodec;

namespace PixelResampler.Tests;

/// <summary>
/// TDD tests for ImageResampler — written first, before the implementation exists.
/// </summary>
public class ResamplerTests
{
    // -----------------------------------------------------------------------
    // Grayscale (1 channel)
    // -----------------------------------------------------------------------

    [Fact]
    public void Downsample_Grayscale_HalvesDimensions()
    {
        // 4×4 solid grey image → 2×2 should stay the same colour.
        const int srcW = 4, srcH = 4, nc = 1;
        var src = new byte[srcW * srcH * nc];
        Array.Fill(src, (byte)200);

        byte[] dst = ImageResampler.Resample(src, srcW, srcH, nc, 2, 2);

        Assert.Equal(2 * 2 * nc, dst.Length);
        foreach (byte p in dst)
            Assert.True(Math.Abs(p - 200) <= 2,
                $"Expected ≈200, got {p}");
    }

    [Fact]
    public void Downsample_Grayscale_PreservesAverageColor()
    {
        // Checkerboard of 0 and 200 → average ≈ 100 when downsampled 2:1.
        const int srcW = 8, srcH = 8, nc = 1;
        var src = new byte[srcW * srcH];
        for (var y = 0; y < srcH; y++)
            for (var x = 0; x < srcW; x++)
                src[y * srcW + x] = (byte)(((x + y) % 2 == 0) ? 0 : 200);

        byte[] dst = ImageResampler.Resample(src, srcW, srcH, nc, 4, 4);

        double avg = dst.Average(b => (double)b);
        Assert.True(Math.Abs(avg - 100) <= 15,
            $"Average colour {avg:F1} too far from 100");
    }

    // -----------------------------------------------------------------------
    // RGB (3 channels)
    // -----------------------------------------------------------------------

    [Fact]
    public void Downsample_Rgb_HalvesDimensions()
    {
        const int srcW = 8, srcH = 8, nc = 3;
        var src = new byte[srcW * srcH * nc];
        // Fill with a known solid colour.
        for (var i = 0; i < src.Length; i += 3)
        {
            src[i + 0] = 100; // R
            src[i + 1] = 150; // G
            src[i + 2] = 200; // B
        }

        byte[] dst = ImageResampler.Resample(src, srcW, srcH, nc, 4, 4);

        Assert.Equal(4 * 4 * nc, dst.Length);
        for (var i = 0; i < dst.Length; i += 3)
        {
            Assert.True(Math.Abs(dst[i + 0] - 100) <= 2, $"R mismatch at pixel {i / 3}");
            Assert.True(Math.Abs(dst[i + 1] - 150) <= 2, $"G mismatch at pixel {i / 3}");
            Assert.True(Math.Abs(dst[i + 2] - 200) <= 2, $"B mismatch at pixel {i / 3}");
        }
    }

    // -----------------------------------------------------------------------
    // RGBA (4 channels)
    // -----------------------------------------------------------------------

    [Fact]
    public void Downsample_Rgba_PreservesAllFourChannels()
    {
        const int srcW = 4, srcH = 4, nc = 4;
        var src = new byte[srcW * srcH * nc];
        for (var i = 0; i < src.Length; i += 4)
        {
            src[i + 0] = 10;
            src[i + 1] = 20;
            src[i + 2] = 30;
            src[i + 3] = 255;
        }

        byte[] dst = ImageResampler.Resample(src, srcW, srcH, nc, 2, 2);

        Assert.Equal(2 * 2 * nc, dst.Length);
        for (var i = 0; i < dst.Length; i += 4)
        {
            Assert.True(Math.Abs(dst[i + 0] - 10) <= 2);
            Assert.True(Math.Abs(dst[i + 1] - 20) <= 2);
            Assert.True(Math.Abs(dst[i + 2] - 30) <= 2);
            Assert.True(Math.Abs(dst[i + 3] - 255) <= 2);
        }
    }

    // -----------------------------------------------------------------------
    // Edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public void Downsample_ToOnePx_ReturnsAverageColor()
    {
        // 4×4 uniform → 1×1 should just be that colour.
        const int srcW = 4, srcH = 4, nc = 1;
        var src = new byte[srcW * srcH];
        Array.Fill(src, (byte)128);

        byte[] dst = ImageResampler.Resample(src, srcW, srcH, nc, 1, 1);

        Assert.Single(dst);
        Assert.True(Math.Abs(dst[0] - 128) <= 2);
    }

    [Fact]
    public void Downsample_NonIntegerRatio_ProducesCorrectSize()
    {
        // 7×5 → 3×2 — non-integer scaling factors.
        const int srcW = 7, srcH = 5, nc = 1;
        var src = new byte[srcW * srcH];
        Array.Fill(src, (byte)64);

        byte[] dst = ImageResampler.Resample(src, srcW, srcH, nc, 3, 2);

        Assert.Equal(3 * 2 * nc, dst.Length);
        foreach (byte p in dst)
            Assert.True(Math.Abs(p - 64) <= 3);
    }

    [Fact]
    public void Resample_NoOp_WhenTargetEqualsSoource()
    {
        const int W = 4, H = 4, nc = 3;
        var src = new byte[W * H * nc];
        for (var i = 0; i < src.Length; i++) src[i] = (byte)(i % 256);

        byte[] dst = ImageResampler.Resample(src, W, H, nc, W, H);

        // When src and dst dimensions are identical the output must be
        // byte-identical (no rounding drift on a trivial no-op pass).
        Assert.Equal(src, dst);
    }

    // -----------------------------------------------------------------------
    // DPI-based convenience overload
    // -----------------------------------------------------------------------

    [Fact]
    public void Resample_DpiOverload_DownscalesBy2x()
    {
        // 8×8 at 300 DPI → target 150 DPI should give 4×4.
        const int srcW = 8, srcH = 8, nc = 1;
        var src = new byte[srcW * srcH];
        Array.Fill(src, (byte)100);

        byte[] dst = ImageResampler.ResampleByDpi(src, srcW, srcH, nc,
            sourceDpi: 300, targetDpi: 150);

        Assert.Equal(4 * 4 * nc, dst.Length);
    }

    [Fact]
    public void Resample_DpiOverload_NoOpAtSameDpi()
    {
        const int srcW = 4, srcH = 4, nc = 1;
        var src = new byte[srcW * srcH];
        for (var i = 0; i < src.Length; i++) src[i] = (byte)i;

        byte[] dst = ImageResampler.ResampleByDpi(src, srcW, srcH, nc,
            sourceDpi: 150, targetDpi: 150);

        Assert.Equal(src, dst);
    }

    // -----------------------------------------------------------------------
    // Argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Resample_ThrowsOnNullInput()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ImageResampler.Resample(null!, 4, 4, 1, 2, 2));
    }

    [Fact]
    public void Resample_ThrowsOnZeroDimension()
    {
        var src = new byte[16];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImageResampler.Resample(src, 4, 4, 1, 0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImageResampler.Resample(src, 4, 4, 1, 2, 0));
    }

    [Fact]
    public void Resample_ThrowsOnUnsupportedChannelCount()
    {
        var src = new byte[16];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImageResampler.Resample(src, 4, 4, 5, 2, 2));
    }

    // -----------------------------------------------------------------------
    // JPEG round-trip: re-encode at lower quality and verify it still decodes
    // -----------------------------------------------------------------------

    [Fact]
    public void JpegReEncode_AtLowerQuality_StillDecodesApproximately()
    {
        // Synthesise a 32×32 RGB gradient image.
        const int W = 32, H = 32, nc = 3;
        var original = new byte[W * H * nc];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                int i = (y * W + x) * nc;
                original[i + 0] = (byte)(x * 8);
                original[i + 1] = (byte)(y * 8);
                original[i + 2] = (byte)((x + y) * 4);
            }

        // First encode at high quality.
        var encOpts95 = new JpegEncodeOptions
        {
            Width = W, Height = H, NumberOfComponents = nc, Quality = 95
        };
        byte[] highQJpeg = new JpegStreamEncoder().Encode(original, encOpts95);

        // Decode it.
        JpegDecodeResult decoded = new JpegStreamDecoder().Decode(highQJpeg);
        Assert.Equal(W * H * nc, decoded.ComponentData.Length);

        // Re-encode at lower quality — the lossy recompress building block.
        var encOpts50 = new JpegEncodeOptions
        {
            Width = W, Height = H, NumberOfComponents = nc, Quality = 50
        };
        byte[] lowQJpeg = new JpegStreamEncoder().Encode(decoded.ComponentData, encOpts50);

        // The lower quality file must be smaller (lossy compression).
        Assert.True(lowQJpeg.Length < highQJpeg.Length,
            $"Low-Q ({lowQJpeg.Length} B) should be smaller than high-Q ({highQJpeg.Length} B)");

        // Decode the low-quality re-encode; should still decode without error
        // and be roughly similar to original (PSNR ≥ 20 dB is a low bar for Q=50).
        JpegDecodeResult reDecoded = new JpegStreamDecoder().Decode(lowQJpeg);
        Assert.Equal(W * H * nc, reDecoded.ComponentData.Length);

        double sumSqErr = 0;
        for (var i = 0; i < original.Length; i++)
        {
            int d = reDecoded.ComponentData[i] - original[i];
            sumSqErr += d * d;
        }
        double mse = sumSqErr / original.Length;
        double psnr = mse == 0 ? double.PositiveInfinity : 10.0 * Math.Log10(255.0 * 255.0 / mse);

        Assert.True(psnr > 20.0,
            $"Re-encode PSNR={psnr:F2} dB is below 20 dB floor at Q=50");
    }

    [Fact]
    public void JpegReEncode_AfterDownsample_StillDecodes()
    {
        // Synthesise a 32×32 RGB image, downsample 2:1, then JPEG-encode the result.
        const int srcW = 32, srcH = 32, nc = 3;
        var original = new byte[srcW * srcH * nc];
        for (var y = 0; y < srcH; y++)
            for (var x = 0; x < srcW; x++)
            {
                int i = (y * srcW + x) * nc;
                original[i + 0] = (byte)(x * 8);
                original[i + 1] = (byte)(y * 8);
                original[i + 2] = 128;
            }

        // Downsample 32×32 → 16×16.
        byte[] downsampled = ImageResampler.Resample(original, srcW, srcH, nc, 16, 16);
        Assert.Equal(16 * 16 * nc, downsampled.Length);

        // JPEG-encode the downsampled result.
        byte[] jpeg = new JpegStreamEncoder().Encode(downsampled,
            new JpegEncodeOptions { Width = 16, Height = 16, NumberOfComponents = nc, Quality = 75 });

        // Must decode without throwing and produce the right size.
        JpegDecodeResult result = new JpegStreamDecoder().Decode(jpeg);
        Assert.Equal(16, result.Width);
        Assert.Equal(16, result.Height);
        Assert.Equal(nc, result.NumberOfComponents);
        Assert.Equal(16 * 16 * nc, result.ComponentData.Length);
    }
}
