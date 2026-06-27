using ICCSharp.Profile;

namespace ICCSharp.Diff;

/// <summary>
/// Differential tests for matrix/TRC ↔ matrix/TRC profiles (sRGB family).
/// </summary>
public class SrgbDiffTests
{
    private static readonly string SrgbPath =
        @"C:\Windows\System32\spool\drivers\color\sRGB Color Space Profile.icm";

    private readonly ITestOutputHelper _output;

    public SrgbDiffTests(ITestOutputHelper output) => _output = output;

    /// <summary>A small grid of representative sRGB inputs: corners, axes, mid-tones, neutrals.</summary>
    private static IReadOnlyList<double[]> SampleGrid3()
    {
        List<double[]> pixels = new();
        for (var r = 0; r <= 4; r++)
        for (var g = 0; g <= 4; g++)
        for (var b = 0; b <= 4; b++)
            pixels.Add(new[] { r / 4.0, g / 4.0, b / 4.0 });
        return pixels;
    }

    [Fact]
    public void Srgb_to_srgb_identity_max_delta_under_quantization_floor()
    {
        if (!LcmsBridge.IsAvailable) return;
        if (!System.IO.File.Exists(SrgbPath)) return;

        IccProfile profile = IccProfile.Parse(System.IO.File.ReadAllBytes(SrgbPath));
        var t = IccTransform.Create(profile, profile);

        IReadOnlyList<double[]> inputs = SampleGrid3();
        List<double[]> iccsharp = new();
        foreach (double[] p in inputs) iccsharp.Add(t.Apply(p));
        double[][] reference = LcmsBridge.Transform(SrgbPath, SrgbPath, 1, false, inputs, 3);

        DiffReport report = new("sRGB → sRGB (identity)", 3, inputs, iccsharp, reference);
        _output.WriteLine(report.ToString());

        // Pillow quantizes inputs to 1/255 and our pipeline is exact-double, so each pixel can
        // differ by up to ~1/255 ≈ 0.004 before any algorithmic disagreement shows up.
        Assert.True(report.GlobalMaxDelta < 0.01,
            $"Max delta {report.GlobalMaxDelta:F4} exceeds quantization floor (0.01).\n{report}");
    }

    [Fact]
    public void Srgb_to_srgb_with_bpc_still_within_quantization_floor()
    {
        if (!LcmsBridge.IsAvailable) return;
        if (!System.IO.File.Exists(SrgbPath)) return;

        IccProfile profile = IccProfile.Parse(System.IO.File.ReadAllBytes(SrgbPath));
        var t = IccTransform.Create(profile, profile,
            new TransformOptions { BlackPointCompensation = true });

        IReadOnlyList<double[]> inputs = SampleGrid3();
        List<double[]> iccsharp = new();
        foreach (double[] p in inputs) iccsharp.Add(t.Apply(p));
        double[][] reference = LcmsBridge.Transform(SrgbPath, SrgbPath, 1, true, inputs, 3);

        DiffReport report = new("sRGB → sRGB (BPC)", 3, inputs, iccsharp, reference);
        _output.WriteLine(report.ToString());

        Assert.True(report.GlobalMaxDelta < 0.01,
            $"Max delta {report.GlobalMaxDelta:F4} exceeds quantization floor (0.01).\n{report}");
    }
}
