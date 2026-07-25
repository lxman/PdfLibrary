using ICCSharp.Eval;
using ICCSharp.IO;
using ICCSharp.Profile;
using ICCSharp.Transform;

namespace ICCSharp.Tests.Transform;

/// <summary>
/// Black point compensation must DETECT the profile's reachable black rather than trust its
/// <c>mediaBlackPoint</c> ('bkpt') tag.
///
/// <para>
/// The Agfa "Swop Standard" profile that Windows installs as RSWOP.icm — the same profile embedded in
/// the project's synthetic CMYK fixtures — declares a black point of XYZ Y = 0.042923 (L* 24.62) while
/// its reachable black is L* 7.84. Building BPC from the declared value maps everything darker than
/// L* 24.6 to a negative PCS value, which clamps to zero and destroys shadow detail.
/// </para>
///
/// <para>
/// Expected sRGB values below are littleCMS output for the same profile and intent, independently
/// corroborated by Adobe Acrobat's on-screen rendering and by poppler and Ghostscript (which both use
/// littleCMS). All four agree to within a count.
/// </para>
/// </summary>
public class BlackPointDetectionTests
{
    private static readonly string SrgbPath =
        @"C:\Windows\System32\spool\drivers\color\sRGB Color Space Profile.icm";
    private static readonly string CmykPath =
        @"C:\Windows\System32\spool\drivers\color\RSWOP.icm";

    private static bool Available => File.Exists(SrgbPath) && File.Exists(CmykPath);

    private static IccProfile Load(string path) => IccProfile.Parse(File.ReadAllBytes(path));

    /// <summary>CMYK 0..1 through the transform to 8-bit sRGB, for comparison against reference CMMs.</summary>
    private static (int R, int G, int B) ToSrgb(IColorTransform t, double c, double m, double y, double k)
    {
        Span<double> inp = [c, m, y, k];
        Span<double> outp = stackalloc double[3];
        t.Apply(inp, outp);
        static int B8(double v) => (int)Math.Round(Math.Clamp(v, 0.0, 1.0) * 255.0);
        return (B8(outp[0]), B8(outp[1]), B8(outp[2]));
    }

    [Fact]
    public void Detected_black_is_far_darker_than_the_profiles_declared_bkpt_tag()
    {
        if (!Available) return;
        IccProfile cmyk = Load(CmykPath);

        XyzNumber declared = cmyk.BlackPoint ?? new XyzNumber(0, 0, 0);
        XyzNumber detected = IccTwoProfileTransform.DetectBlackPoint(cmyk);

        // The tag really is this wrong — if this assert fails, Windows shipped a different RSWOP.icm
        // and the rest of this file's expectations need re-deriving.
        Assert.True(declared.Y > 0.04, $"declared black Y={declared.Y:F6} (expected the bogus ~0.0429)");
        Assert.True(detected.Y < declared.Y / 2.0,
            $"detected black Y={detected.Y:F6} should be far below declared Y={declared.Y:F6}");
        Assert.True(detected.Y >= 0.0, $"detected black Y={detected.Y:F6} must not be negative");
    }

    [Fact]
    public void Srgb_matrix_profile_detects_black_at_zero()
    {
        if (!Available) return;
        // A matrix/TRC profile's curves send 0 to 0, so its black point is exactly zero. Detection must
        // not invent a lift here, or BPC would wash out the destination.
        XyzNumber detected = IccTwoProfileTransform.DetectBlackPoint(Load(SrgbPath));
        Assert.Equal(0.0, detected.X);
        Assert.Equal(0.0, detected.Y);
        Assert.Equal(0.0, detected.Z);
    }

    [Fact]
    public void Saturated_darks_keep_their_channels_under_bpc()
    {
        if (!Available) return;
        var t = new IccTwoProfileTransform(Load(CmykPath), Load(SrgbPath),
            RenderingIntent.RelativeColorimetric, blackPointCompensation: true);

        // C+M violet. littleCMS/Adobe: (61,29,116). With the bogus bkpt tag this collapsed to (12,0,113)
        // — red down 49 counts and green flattened to zero. The tolerance covers CLUT interpolation
        // differences between CMMs; what it does NOT tolerate is a channel crushed to nothing.
        (int R, int G, int B) violet = ToSrgb(t, 1.0, 1.0, 0.0, 0.0);
        Assert.True(Math.Abs(violet.R - 61) <= 12, $"violet R={violet.R}, expected ~61");
        Assert.True(Math.Abs(violet.G - 29) <= 12, $"violet G={violet.G}, expected ~29");
        Assert.True(Math.Abs(violet.B - 116) <= 12, $"violet B={violet.B}, expected ~116");

        // C+Y red: littleCMS/Adobe (229,37,27); previously (226,0,0) — two channels lost.
        (int R, int G, int B) red = ToSrgb(t, 0.0, 1.0, 1.0, 0.0);
        Assert.True(Math.Abs(red.G - 37) <= 12, $"red G={red.G}, expected ~37");
        Assert.True(Math.Abs(red.B - 27) <= 12, $"red B={red.B}, expected ~27");
    }

    [Fact]
    public void Absolute_colorimetric_ignores_the_bpc_flag()
    {
        if (!Available) return;
        // ISO 32000-2 8.6.5.9: "If the current render intent of an object is AbsColorimetric then the
        // value of UseBlackPtComp shall be treated as OFF."
        IccProfile cmyk = Load(CmykPath), srgb = Load(SrgbPath);
        var withBpc = new IccTwoProfileTransform(cmyk, srgb,
            RenderingIntent.AbsoluteColorimetric, blackPointCompensation: true);
        var withoutBpc = new IccTwoProfileTransform(cmyk, srgb,
            RenderingIntent.AbsoluteColorimetric, blackPointCompensation: false);

        Assert.Equal(ToSrgb(withoutBpc, 0, 0, 0, 1.0), ToSrgb(withBpc, 0, 0, 0, 1.0));
        Assert.Equal(ToSrgb(withoutBpc, 1.0, 1.0, 0, 0), ToSrgb(withBpc, 1.0, 1.0, 0, 0));
    }

    [Fact]
    public void Bpc_off_still_reproduces_the_unlifted_black()
    {
        if (!Available) return;
        // Regression guard on the OTHER direction: with BPC off, K=100 must stay at the profile's own
        // black (littleCMS RelCol no-BPC gives (34,33,33)), not get dragged to zero.
        var t = new IccTwoProfileTransform(Load(CmykPath), Load(SrgbPath),
            RenderingIntent.RelativeColorimetric, blackPointCompensation: false);

        (int R, int G, int B) black = ToSrgb(t, 0, 0, 0, 1.0);
        Assert.True(black.R is >= 20 and <= 48, $"K=100 with BPC off: R={black.R}, expected ~34");
    }
}
