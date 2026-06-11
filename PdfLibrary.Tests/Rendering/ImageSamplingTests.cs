using PdfLibrary.Rendering.SkiaSharp.Rendering;
using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Covers <see cref="ImageRenderer.ChooseImageSampling"/> — the resampler decision that determines
/// whether a magnified low-resolution image renders crisp (honouring /Interpolate) or blurs its
/// pixels into one another.
/// </summary>
public class ImageSamplingTests
{
    [Fact]
    public void Upscaling_without_interpolate_uses_nearest_neighbour()
    {
        // 8px source magnified to 400 device px, /Interpolate absent/false → crisp, no blending.
        SKSamplingOptions s = ImageRenderer.ChooseImageSampling(8, 8, 400, 400, interpolate: false);
        Assert.False(s.UseCubic);
        Assert.Equal(SKFilterMode.Nearest, s.Filter);
    }

    [Fact]
    public void Upscaling_with_interpolate_uses_cubic()
    {
        // Same magnification, but the image opts into smoothing.
        SKSamplingOptions s = ImageRenderer.ChooseImageSampling(8, 8, 400, 400, interpolate: true);
        Assert.True(s.UseCubic);
    }

    [Fact]
    public void One_to_one_without_interpolate_stays_nearest()
    {
        SKSamplingOptions s = ImageRenderer.ChooseImageSampling(400, 400, 400, 400, interpolate: false);
        Assert.False(s.UseCubic);
        Assert.Equal(SKFilterMode.Nearest, s.Filter);
    }

    [Fact]
    public void Minifying_uses_mipmapped_linear_regardless_of_interpolate()
    {
        // A high-resolution scan shrunk to page resolution: quality filtering for anti-aliasing,
        // independent of /Interpolate (which governs upscaling only).
        SKSamplingOptions s = ImageRenderer.ChooseImageSampling(2000, 2000, 200, 200, interpolate: false);
        Assert.False(s.UseCubic);
        Assert.Equal(SKFilterMode.Linear, s.Filter);
        Assert.Equal(SKMipmapMode.Linear, s.Mipmap);
    }
}
