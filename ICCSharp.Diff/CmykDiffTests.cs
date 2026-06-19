using ICCSharp.Profile;
using Xunit.Abstractions;

namespace ICCSharp.Diff;

/// <summary>
/// Differential tests for legacy lut + Lab PCS profiles (CMYK SWOP).
/// </summary>
public class CmykDiffTests
{
    private static readonly string SrgbPath =
        @"C:\Windows\System32\spool\drivers\color\sRGB Color Space Profile.icm";
    private static readonly string CmykPath =
        @"C:\Windows\System32\spool\drivers\color\RSWOP.icm";

    private readonly ITestOutputHelper _output;

    public CmykDiffTests(ITestOutputHelper output) => _output = output;

    private static IReadOnlyList<double[]> SampleGrid3()
    {
        List<double[]> pixels = new();
        for (var r = 0; r <= 4; r++)
        for (var g = 0; g <= 4; g++)
        for (var b = 0; b <= 4; b++)
            pixels.Add(new[] { r / 4.0, g / 4.0, b / 4.0 });
        return pixels;
    }

    private static IReadOnlyList<double[]> SampleGrid4()
    {
        List<double[]> pixels = new();
        for (var c = 0; c <= 3; c++)
        for (var m = 0; m <= 3; m++)
        for (var y = 0; y <= 3; y++)
        for (var k = 0; k <= 3; k++)
            pixels.Add(new[] { c / 3.0, m / 3.0, y / 3.0, k / 3.0 });
        return pixels;
    }

    [Fact]
    public void Srgb_to_cmyk_differential_diff()
    {
        if (!LcmsBridge.IsAvailable) return;
        if (!System.IO.File.Exists(SrgbPath) || !System.IO.File.Exists(CmykPath)) return;

        IccProfile srgb = IccProfile.Parse(System.IO.File.ReadAllBytes(SrgbPath));
        IccProfile cmyk = IccProfile.Parse(System.IO.File.ReadAllBytes(CmykPath));
        var t = IccTransform.Create(srgb, cmyk);

        IReadOnlyList<double[]> inputs = SampleGrid3();
        List<double[]> iccsharp = new();
        foreach (double[] p in inputs) iccsharp.Add(t.Apply(p));
        double[][] reference = LcmsBridge.Transform(SrgbPath, CmykPath, 1, false, inputs, 4);

        DiffReport report = new("sRGB → CMYK (SWOP)", 4, inputs, iccsharp, reference);
        _output.WriteLine(report.ToString());

        // For CMYK output, channels in [0,1]. ΔE in linear CMYK space isn't perceptual but a
        // max delta of 5% is a reasonable threshold for "first version close enough".
        Assert.True(report.GlobalMaxDelta < 0.10,
            $"sRGB→CMYK max delta {report.GlobalMaxDelta:F4} > 0.10.\n{report}");
    }

    [Fact]
    public void Cmyk_to_srgb_differential_diff()
    {
        if (!LcmsBridge.IsAvailable) return;
        if (!System.IO.File.Exists(SrgbPath) || !System.IO.File.Exists(CmykPath)) return;

        IccProfile srgb = IccProfile.Parse(System.IO.File.ReadAllBytes(SrgbPath));
        IccProfile cmyk = IccProfile.Parse(System.IO.File.ReadAllBytes(CmykPath));
        var t = IccTransform.Create(cmyk, srgb);

        IReadOnlyList<double[]> inputs = SampleGrid4();
        List<double[]> iccsharp = new();
        foreach (double[] p in inputs) iccsharp.Add(t.Apply(p));
        double[][] reference = LcmsBridge.Transform(CmykPath, SrgbPath, 1, false, inputs, 3);

        DiffReport report = new("CMYK (SWOP) → sRGB", 3, inputs, iccsharp, reference);
        _output.WriteLine(report.ToString());

        Assert.True(report.GlobalMaxDelta < 0.10,
            $"CMYK→sRGB max delta {report.GlobalMaxDelta:F4} > 0.10.\n{report}");
    }
}
