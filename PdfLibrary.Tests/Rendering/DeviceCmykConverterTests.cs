using ICCSharp.Profile;
using PdfLibrary.Rendering.Icc;

namespace PdfLibrary.Tests.Rendering;

public class DeviceCmykConverterTests
{
    private static DeviceCmykConverter Swop()
        => new(IccProfile.Parse(IccResources.ReadDefaultCmykProfile()));

    [Fact]
    public void Built_from_real_profile_is_not_degraded()
        => Assert.False(Swop().IsDegraded);

    [Fact]
    public void White_cmyk_maps_near_white()
    {
        (byte r, byte g, byte b) = Swop().ToRgb(0, 0, 0, 0);
        Assert.True(r >= 245 && g >= 245 && b >= 245, $"got ({r},{g},{b})");
    }

    [Fact]
    public void Full_black_cmyk_maps_dark()
    {
        (byte r, byte g, byte b) = Swop().ToRgb(0, 0, 0, 1);
        Assert.True(r <= 70 && g <= 70 && b <= 70, $"got ({r},{g},{b})");
    }

    [Fact]
    public void Icc_path_differs_from_naive_pure_cyan()
    {
        // Naive DeviceCMYK pure cyan is exactly (0,255,255); SWOP must differ (proves ICC is engaged).
        (byte r, byte g, byte b) = Swop().ToRgb(1, 0, 0, 0);
        Assert.NotEqual(((byte)0, (byte)255, (byte)255), (r, g, b));
    }

    [Fact]
    public void Rgb_roundtrip_through_cmyk_is_close_for_interior_color()
    {
        DeviceCmykConverter conv = Swop();
        const double r = 0.50, g = 0.40, b = 0.30;          // warm, safely inside the SWOP gamut
        (double c, double m, double y, double k) = conv.ToCmyk(r, g, b);
        (byte rr, byte gg, byte bb) = conv.ToRgb(c, m, y, k);
        Assert.True(Math.Abs(rr / 255.0 - r) < 0.06, $"R {rr/255.0:F3} vs {r}");
        Assert.True(Math.Abs(gg / 255.0 - g) < 0.06, $"G {gg/255.0:F3} vs {g}");
        Assert.True(Math.Abs(bb / 255.0 - b) < 0.06, $"B {bb/255.0:F3} vs {b}");
    }

    [Fact]
    public void Bulk_forward_equals_scalar()
    {
        DeviceCmykConverter conv = Swop();
        double[] cmyk = { 0,0,0,0,  1,0,0,0,  0,0.4,0.8,0,  0.2,0.3,0.4,0.1 };
        var bulk = new byte[(cmyk.Length / 4) * 3];
        conv.ToRgbMany(cmyk, bulk);

        for (var i = 0; i < cmyk.Length / 4; i++)
        {
            (byte r, byte g, byte b) = conv.ToRgb(cmyk[i*4], cmyk[i*4+1], cmyk[i*4+2], cmyk[i*4+3]);
            Assert.Equal(r, bulk[i*3]);
            Assert.Equal(g, bulk[i*3+1]);
            Assert.Equal(b, bulk[i*3+2]);
        }
    }

    [Fact]
    public void Naive_helpers_match_legacy_behavior()
    {
        Assert.Equal(((byte)0,(byte)255,(byte)255), DeviceCmykConverter.NaiveCmykToRgb(1,0,0,0));
        Assert.Equal(((byte)0,(byte)0,(byte)0),     DeviceCmykConverter.NaiveCmykToRgb(0,0,0,1));
        Assert.Equal((0.0,0.0,0.0,1.0),             DeviceCmykConverter.NaiveRgbToCmyk(0,0,0));
        Assert.Equal((0.0,0.0,0.0,0.0),             DeviceCmykConverter.NaiveRgbToCmyk(1,1,1));
    }

    [Fact]
    public void Bulk_forward_perf_smoke()
    {
        DeviceCmykConverter conv = Swop();
        const int pixels = 100_000;
        var cmyk = new double[pixels * 4];
        for (var i = 0; i < pixels; i++) { cmyk[i*4]=0.2; cmyk[i*4+1]=0.4; cmyk[i*4+2]=0.1; cmyk[i*4+3]=0.0; }
        var rgb = new byte[pixels * 3];

        var sw = System.Diagnostics.Stopwatch.StartNew();
        conv.ToRgbMany(cmyk, rgb);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 5000, $"100k-pixel CMYK->RGB took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Perceptual_black_is_darker_than_relative_colorimetric()
    {
        // Perceptual intent maps the full dynamic range (realistic black point) instead of clipping
        // darks to media-white as RelativeColorimetric does. For this library's bundled CC0 profile
        // (SWOP_TR003_coated_3), Perceptual K=1 = (49,49,49) vs the old RelCol ~(55,53,53). Assert the
        // property (darker than the RelCol floor) pinned to this profile's value with a small margin.
        // (PdfLibrary overrides with Adobe SWOP v2, which renders K=1 = Adobe's (35,31,32) — verified in a
        // PdfLibrary-side test, not here, since the library ships the CC0 profile.)
        (byte r, byte g, byte b) = Swop().ToRgb(0, 0, 0, 1);
        Assert.True(r <= 52 && g <= 52 && b <= 52, $"K=1 not dark enough for Perceptual: ({r},{g},{b})");
    }

    [Fact]
    public void Perceptual_heavy_ink_is_darker_than_media_black_floor()
    {
        // 300% ink (C+M+Y, no K) must be darker than RelativeColorimetric's washed-out floor (~68).
        // CC0-profile Perceptual gives (65,65,66) — darker than RelCol, as expected.
        (byte r, byte g, byte b) = Swop().ToRgb(1, 1, 1, 0);
        Assert.True(r < 68 && g < 68 && b < 68, $"300% ink not dark: ({r},{g},{b})");
    }

    [Fact]
    public void Absolute_forward_white_differs_from_perceptual_white()
    {
        // Paper-white simulation: AbsoluteColorimetric reproduces the profile's media white literally
        // (a slightly-off, dimmer sheet) instead of mapping it to display white as Perceptual does.
        IccProfile p = IccProfile.Parse(IccResources.ReadDefaultCmykProfile());
        var perceptual = new DeviceCmykConverter(p, RenderingIntent.Perceptual);
        var absolute   = new DeviceCmykConverter(p, RenderingIntent.AbsoluteColorimetric);
        (byte pr, byte pg, byte pb) = perceptual.ToRgb(0, 0, 0, 0);
        (byte ar, byte ag, byte ab) = absolute.ToRgb(0, 0, 0, 0);
        Assert.NotEqual((pr, pg, pb), (ar, ag, ab));                 // paper sim visibly changes the sheet
        Assert.True(ar <= pr && ag <= pg && ab <= pb, $"absolute white {(ar,ag,ab)} not dimmer than perceptual {(pr,pg,pb)}");
    }

    [Fact]
    public void Inverse_transform_is_relative_colorimetric_roundtrip_stable()
    {
        // The inverse (sRGB->CMYK) is now RelativeColorimetric: an in-gamut colour round-trips near-identically.
        var conv = new DeviceCmykConverter(IccProfile.Parse(IccResources.ReadDefaultCmykProfile()));
        const double r = 0.50, g = 0.40, b = 0.30;                  // safely inside SWOP
        (double c, double m, double y, double k) = conv.ToCmyk(r, g, b);
        (byte rr, byte gg, byte bb) = conv.ToRgb(c, m, y, k);
        Assert.True(Math.Abs(rr / 255.0 - r) < 0.06, $"R {rr/255.0:F3} vs {r}");
        Assert.True(Math.Abs(gg / 255.0 - g) < 0.06, $"G {gg/255.0:F3} vs {g}");
        Assert.True(Math.Abs(bb / 255.0 - b) < 0.06, $"B {bb/255.0:F3} vs {b}");
    }

    [Fact]
    public void Default_display_intent_rebuilds_converter()
    {
        DeviceCmykConverter.DisplayIntent = RenderingIntent.Perceptual;
        DeviceCmykConverter.DisplayBlackPointCompensation = false;
        (byte pr, byte pg, byte pb) = DeviceCmykConverter.Default.ToRgb(0, 0, 0, 0);
        try
        {
            DeviceCmykConverter.DisplayIntent = RenderingIntent.AbsoluteColorimetric;
            (byte ar, byte ag, byte ab) = DeviceCmykConverter.Default.ToRgb(0, 0, 0, 0);
            Assert.NotEqual((pr, pg, pb), (ar, ag, ab));            // Default rebuilt with the new intent
        }
        finally { DeviceCmykConverter.DisplayIntent = RenderingIntent.Perceptual; }   // restore process default
    }

    [Fact]
    public void Roundtrip_relative_is_near_identity_for_in_gamut_grey()
    {
        var conv = new DeviceCmykConverter(IccProfile.Parse(IccResources.ReadDefaultCmykProfile()));
        (byte r, byte g, byte b) = conv.RoundTripRgbRelative(128, 128, 128);   // neutral grey is well inside gamut
        Assert.True(Math.Abs(r - 128) <= 12 && Math.Abs(g - 128) <= 12 && Math.Abs(b - 128) <= 12,
            $"in-gamut grey drifted too far: ({r},{g},{b})");
    }

    [Fact]
    public void Roundtrip_relative_clips_out_of_gamut_saturated_green()
    {
        // Pure sRGB green is far outside a press CMYK gamut; the round-trip must clip it substantially.
        var conv = new DeviceCmykConverter(IccProfile.Parse(IccResources.ReadDefaultCmykProfile()));
        (byte r, byte g, byte b) = conv.RoundTripRgbRelative(0, 255, 0);
        int drift = Math.Abs(r - 0) + Math.Abs(g - 255) + Math.Abs(b - 0);
        Assert.True(drift > 60, $"out-of-gamut green did not clip (drift {drift}): ({r},{g},{b})");
    }
}
