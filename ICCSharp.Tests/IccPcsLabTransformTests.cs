using ICCSharp.Profile;
using ICCSharp.Transform;

namespace ICCSharp.Tests;

public class IccPcsLabTransformTests
{
    // Same OS-installed CMYK fixture used elsewhere in this project (e.g.
    // Transform/CmykProfileSmokeTests.cs, Transform/IccTwoProfileTransformTests.cs) — no bundled
    // binary fixture exists in ICCSharp.Tests, so we reuse this convention rather than add one.
    private static readonly string CmykPath =
        @"C:\Windows\System32\spool\drivers\color\RSWOP.icm";

    private static IccProfile LoadCmyk() => IccProfile.Parse(File.ReadAllBytes(CmykPath));

    [Fact]
    public void White_lab_produces_near_zero_ink()
    {
        if (!File.Exists(CmykPath)) return;
        var t = IccPcsLabTransform.Create(LoadCmyk());
        Span<double> outCmyk = stackalloc double[t.OutputChannels];
        t.Apply(stackalloc double[] { 100.0, 0.0, 0.0 }, outCmyk);
        Assert.Equal(4, t.OutputChannels);
        for (var i = 0; i < 4; i++) Assert.True(outCmyk[i] < 0.08, $"channel {i} = {outCmyk[i]}");
    }

    [Fact]
    public void Black_lab_produces_heavy_ink()
    {
        if (!File.Exists(CmykPath)) return;
        var t = IccPcsLabTransform.Create(LoadCmyk());
        Span<double> outCmyk = stackalloc double[4];
        t.Apply(stackalloc double[] { 0.0, 0.0, 0.0 }, outCmyk);
        Assert.True(outCmyk[3] > 0.5, $"K = {outCmyk[3]}");
    }

    [Fact]
    public void Matches_full_transform_for_srgb_red_roundtrip()
    {
        // Oracle: sRGB red → PCS via the existing two-profile path must land on the same CMYK
        // as feeding that colour's Lab directly. Compute red's Lab with the same primitives the
        // library uses (LabXyzConverter/rely on a precomputed constant): sRGB (1,0,0) ≈ Lab(54.29, 80.81, 69.89).
        if (!File.Exists(CmykPath)) return;
        IccProfile cmyk = LoadCmyk();
        var full = IccTransform.Create(BuiltInProfiles.Srgb, cmyk,
            new TransformOptions { Intent = RenderingIntent.RelativeColorimetric });
        double[] viaRgb = full.Apply(1.0, 0.0, 0.0);
        var labT = IccPcsLabTransform.Create(cmyk);
        Span<double> viaLab = stackalloc double[4];
        labT.Apply(stackalloc double[] { 54.29, 80.81, 69.89 }, viaLab);
        for (var i = 0; i < 4; i++) Assert.True(Math.Abs(viaRgb[i] - viaLab[i]) < 0.05,
            $"channel {i}: {viaRgb[i]} vs {viaLab[i]}");
    }
}
