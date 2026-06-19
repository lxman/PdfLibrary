using ICCSharp.Profile;
using ICCSharp.Tags;

namespace ICCSharp.Tests.Transform;

/// <summary>
/// End-to-end smoke tests against the OS-installed CMYK profile (US Web Coated SWOP, RSWOP.icm).
/// Validates that legacy lut8/lut16 parsing + pipeline + Lab PCS encoding actually round-trip on
/// a real-world profile, without claiming bit-exact match against any reference CMM.
/// </summary>
public class CmykProfileSmokeTests
{
    private static readonly string SrgbPath =
        @"C:\Windows\System32\spool\drivers\color\sRGB Color Space Profile.icm";
    private static readonly string CmykPath =
        @"C:\Windows\System32\spool\drivers\color\RSWOP.icm";

    [Fact]
    public void RSWOP_profile_parses_and_reports_class_cmyk_lab_pcs()
    {
        if (!File.Exists(CmykPath)) return;
        IccProfile p = IccProfile.Parse(File.ReadAllBytes(CmykPath));

        Assert.Equal(ProfileClass.Output, p.Header.Class);
        Assert.Equal(ColorSpaceSignatures.CMYK, p.Header.DataColorSpace);
        Assert.Equal(ColorSpaceSignatures.Lab, p.Header.ProfileConnectionSpace);
    }

    [Fact]
    public void RSWOP_profile_has_A2B_and_B2A_tags()
    {
        if (!File.Exists(CmykPath)) return;
        IccProfile p = IccProfile.Parse(File.ReadAllBytes(CmykPath));

        // CMYK printer profiles always carry at least A2B0 and B2A0.
        Assert.NotNull(p.AToB0);
        Assert.NotNull(p.BToA0);
        // Should be legacy lut tags (this is a v2 profile).
        Assert.True(p.AToB0 is Lut8TagElement or Lut16TagElement,
            $"AToB0 is {p.AToB0?.GetType().Name}");
        Assert.True(p.BToA0 is Lut8TagElement or Lut16TagElement,
            $"BToA0 is {p.BToA0?.GetType().Name}");
    }

    [Fact]
    public void SRGB_to_CMYK_to_SRGB_round_trip_stays_within_a_reasonable_range()
    {
        if (!File.Exists(SrgbPath) || !File.Exists(CmykPath)) return;
        IccProfile srgb = IccProfile.Parse(File.ReadAllBytes(SrgbPath));
        IccProfile cmyk = IccProfile.Parse(File.ReadAllBytes(CmykPath));

        // sRGB → CMYK → sRGB. The CMYK gamut is smaller, so colors outside it are clipped, but
        // mid-tone neutrals should survive the round trip reasonably.
        var srgbToCmyk = IccTransform.Create(srgb, cmyk);
        var cmykToSrgb = IccTransform.Create(cmyk, srgb);

        Assert.Equal(3, srgbToCmyk.InputChannels);
        Assert.Equal(4, srgbToCmyk.OutputChannels);
        Assert.Equal(4, cmykToSrgb.InputChannels);
        Assert.Equal(3, cmykToSrgb.OutputChannels);

        double[] cmykMid = srgbToCmyk.Apply(0.5, 0.5, 0.5);
        Assert.Equal(4, cmykMid.Length);
        // All CMYK values should land within [0, 1].
        foreach (double v in cmykMid)
            Assert.InRange(v, -0.01, 1.01);

        // Round trip through sRGB: should be in a reasonable region of mid-gray.
        double[] backToSrgb = cmykToSrgb.Apply(cmykMid);
        // Loose tolerance — CMYK profile is lossy.
        Assert.InRange(backToSrgb[0], 0.3, 0.7);
        Assert.InRange(backToSrgb[1], 0.3, 0.7);
        Assert.InRange(backToSrgb[2], 0.3, 0.7);
    }

    [Fact]
    public void SRGB_pure_white_to_CMYK_lands_near_paper_white()
    {
        if (!File.Exists(SrgbPath) || !File.Exists(CmykPath)) return;
        IccProfile srgb = IccProfile.Parse(File.ReadAllBytes(SrgbPath));
        IccProfile cmyk = IccProfile.Parse(File.ReadAllBytes(CmykPath));
        var t = IccTransform.Create(srgb, cmyk);

        double[] cmykWhite = t.Apply(1.0, 1.0, 1.0);
        // Paper white in CMYK should be near (0, 0, 0, 0) — no ink.
        // SWOP doesn't always go to exactly zero; allow a few % of ink as paper-tint correction.
        Assert.InRange(cmykWhite[0], 0.0, 0.10);
        Assert.InRange(cmykWhite[1], 0.0, 0.10);
        Assert.InRange(cmykWhite[2], 0.0, 0.10);
        Assert.InRange(cmykWhite[3], 0.0, 0.10);
    }

    [Fact]
    public void SRGB_pure_black_to_CMYK_yields_significant_K()
    {
        if (!File.Exists(SrgbPath) || !File.Exists(CmykPath)) return;
        IccProfile srgb = IccProfile.Parse(File.ReadAllBytes(SrgbPath));
        IccProfile cmyk = IccProfile.Parse(File.ReadAllBytes(CmykPath));
        var t = IccTransform.Create(srgb, cmyk);

        double[] cmykBlack = t.Apply(0.0, 0.0, 0.0);
        // Black should have substantial K (black ink). SWOP typically maxes near full K + some CMY.
        Assert.True(cmykBlack[3] > 0.5,
            $"Expected K > 0.5 for sRGB black; got K = {cmykBlack[3]:F3}, full = ({cmykBlack[0]:F3}, {cmykBlack[1]:F3}, {cmykBlack[2]:F3}, {cmykBlack[3]:F3})");
    }

    [Fact]
    public void Absolute_colorimetric_reproduces_swop_media_white_not_pure_white()
    {
        if (!File.Exists(CmykPath)) return;
        IccProfile swop = IccProfile.Parse(File.ReadAllBytes(CmykPath));

        var rel = IccTransform.Create(swop, BuiltInProfiles.Srgb,
            new TransformOptions { Intent = RenderingIntent.RelativeColorimetric });
        var abs = IccTransform.Create(swop, BuiltInProfiles.Srgb,
            new TransformOptions { Intent = RenderingIntent.AbsoluteColorimetric });

        // Paper white = no ink. Relative colorimetric normalises SWOP's media white to the
        // destination's → ~pure white. Absolute reproduces SWOP's actual paper white, which is
        // dimmer and slightly warm. (Before this fix, absolute silently equalled relative.)
        double[] relWhite = rel.Apply(0, 0, 0, 0);
        double[] absWhite = abs.Apply(0, 0, 0, 0);

        Assert.True(relWhite[0] > 0.99 && relWhite[1] > 0.99,
            $"relative paper white should be ~pure white; got ({relWhite[0]:F3}, {relWhite[1]:F3}, {relWhite[2]:F3})");

        // Absolute differs from relative (not the silent-equals-relative bug) and is dimmer.
        Assert.True(relWhite[1] - absWhite[1] > 0.01,
            $"absolute paper white should differ from relative; abs={absWhite[1]:F3} rel={relWhite[1]:F3}");
        Assert.True(absWhite[0] < 0.99,
            $"absolute paper white should be below pure white; got R={absWhite[0]:F3}");

        // ...and warm/neutral (red >= blue), since SWOP paper is not a neutral pure white.
        Assert.True(absWhite[0] >= absWhite[2] - 1e-6,
            $"absolute paper white should be warm/neutral; got ({absWhite[0]:F3}, {absWhite[1]:F3}, {absWhite[2]:F3})");
    }
}
