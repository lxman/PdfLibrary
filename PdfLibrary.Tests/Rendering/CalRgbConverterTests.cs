using PdfLibrary.Rendering.Icc;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Covers CalRGB / CalGray calibrated color-space conversion to sRGB. The CalRGB parameters are
/// taken from the PDF Association's "PDF 2.0 image with BPC" example — a wide-gamut RGB space —
/// where Acrobat renders the calibrated image visibly redder than the same bytes read as DeviceRGB.
/// A passthrough (treat-as-DeviceRGB) implementation fails the red-shift assertions.
/// </summary>
public class CalRgbConverterTests
{
    // Wide-gamut CalRGB from the example file (WhitePoint D50, gamma 2.2, wide primary matrix).
    private static readonly double[] WhitePoint = [0.9643, 1.0000, 0.8251];
    private static readonly double[] Gamma = [2.2, 2.2, 2.2];
    private static readonly double[] Matrix =
    [
        0.7161, 0.2582, 0.0000,
        0.1009, 0.7249, 0.0518,
        0.1472, 0.0168, 0.7734
    ];

    private static CalRgbConverter WideGamut() => new(WhitePoint, Gamma, Matrix);

    [Fact]
    public void Orange_components_convert_to_a_redder_srgb_colour()
    {
        // A DeviceRGB "orange". Read as wide-gamut CalRGB, it must shift toward red: green drops
        // well below the passthrough value (0.5) and red stays dominant.
        double[] rgb = WideGamut().ToSrgb(0.9, 0.5, 0.1);

        Assert.True(rgb[0] > 0.7, $"red should stay strong; got R={rgb[0]:F3}");
        Assert.True(rgb[2] < 0.2, $"blue should stay low; got B={rgb[2]:F3}");
        Assert.True(rgb[1] < rgb[0], $"red should dominate green; got R={rgb[0]:F3} G={rgb[1]:F3}");
        // The red shift: green falls clearly below the naive DeviceRGB value of 0.5.
        Assert.True(rgb[1] < 0.45, $"green should drop (red shift); got G={rgb[1]:F3}");
        Assert.True(rgb[1] / rgb[0] < 0.5, $"green/red ratio should fall below the naive 0.56; got {rgb[1] / rgb[0]:F3}");
    }

    [Fact]
    public void White_maps_to_srgb_white()
    {
        double[] rgb = WideGamut().ToSrgb(1.0, 1.0, 1.0);
        Assert.True(rgb[0] > 0.98 && rgb[1] > 0.98 && rgb[2] > 0.98,
            $"white expected; got ({rgb[0]:F3}, {rgb[1]:F3}, {rgb[2]:F3})");
    }

    [Fact]
    public void Black_maps_to_srgb_black()
    {
        double[] rgb = WideGamut().ToSrgb(0.0, 0.0, 0.0);
        Assert.True(rgb[0] < 0.02 && rgb[1] < 0.02 && rgb[2] < 0.02,
            $"black expected; got ({rgb[0]:F3}, {rgb[1]:F3}, {rgb[2]:F3})");
    }

    [Fact]
    public void Equal_components_stay_neutral()
    {
        // Equal RGB through the matrix yields a scalar times the white point (the matrix columns
        // sum to the white point), so the neutral axis is preserved regardless of gamut width.
        double[] rgb = WideGamut().ToSrgb(0.6, 0.6, 0.6);
        Assert.Equal(rgb[0], rgb[1], 2);
        Assert.Equal(rgb[1], rgb[2], 2);
    }

    [Fact]
    public void CalGray_ramp_is_neutral_and_monotonic()
    {
        var conv = new CalGrayConverter(WhitePoint, 2.2);

        double[] white = conv.ToSrgb(1.0);
        double[] black = conv.ToSrgb(0.0);
        Assert.True(white[0] > 0.98, $"white expected; got {white[0]:F3}");
        Assert.True(black[0] < 0.02, $"black expected; got {black[0]:F3}");

        double prev = -1.0;
        for (var i = 0; i <= 10; i++)
        {
            double[] g = conv.ToSrgb(i / 10.0);
            Assert.Equal(g[0], g[1], 2);   // neutral
            Assert.Equal(g[1], g[2], 2);
            Assert.True(g[0] >= prev - 1e-9, $"ramp not monotonic at {i}");
            prev = g[0];
        }
    }
}
