using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering.Icc;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Covers ICC color management of an interleaved CMYK image/palette buffer through an embedded
/// profile (the path direct ICCBased CMYK images take in ImageRenderer). Uses the OS-installed
/// US Web Coated SWOP profile; skipped silently when it isn't present.
///
/// The point is that process CMYK is rendered through the profile, not the naive (1-c)(1-k)
/// approximation: SWOP process cyan is a muted sky-blue, not the electric (0,255,255) naive gives.
/// </summary>
public class IccCmykImageTests
{
    private static readonly string SwopPath =
        @"C:\Windows\System32\spool\drivers\color\RSWOP.icm";

    [Fact]
    public void Cmyk_buffer_is_colour_managed_through_embedded_profile_not_naive()
    {
        if (!File.Exists(SwopPath)) return;

        // No /Filter → GetDecodedData returns the raw profile bytes; the transform reads channel
        // count from the profile itself (CMYK → 4 inputs).
        var iccStream = new PdfStream(File.ReadAllBytes(SwopPath));

        // Two samples: 100% process cyan, then paper white (no ink).
        byte[] cmyk = [255, 0, 0, 0, 0, 0, 0, 0];
        byte[]? rgb = new IccColorConverter().TryConvertInterleavedToSrgb(iccStream, cmyk, 4);

        Assert.NotNull(rgb);
        Assert.Equal(6, rgb!.Length);

        // SWOP process cyan: muted, NOT naive electric (0,255,255). Both green and blue come back
        // clearly below 255, and it still reads as cyan (blue/green dominate red).
        Assert.True(rgb[1] < 235, $"SWOP cyan green should be muted below naive 255; got G={rgb[1]}");
        Assert.True(rgb[2] < 252, $"SWOP cyan blue should be at/below naive 255; got B={rgb[2]}");
        Assert.True(rgb[2] > rgb[0] && rgb[1] > rgb[0], $"cyan stays blue/green dominant; got ({rgb[0]},{rgb[1]},{rgb[2]})");

        // Paper white (no ink) stays near white.
        Assert.True(rgb[3] > 235 && rgb[4] > 235 && rgb[5] > 235,
            $"paper white expected; got ({rgb[3]},{rgb[4]},{rgb[5]})");
    }

    [Fact]
    public void Channel_count_mismatch_returns_null_so_caller_can_fall_back()
    {
        if (!File.Exists(SwopPath)) return;
        var iccStream = new PdfStream(File.ReadAllBytes(SwopPath));

        // Feeding 3-channel samples to a 4-channel (CMYK) profile must fail cleanly, not throw.
        byte[] rgbInput = [128, 64, 32, 200, 100, 50];
        byte[]? result = new IccColorConverter().TryConvertInterleavedToSrgb(iccStream, rgbInput, 3);

        Assert.Null(result);
    }

    [Fact]
    public void Parallel_path_matches_scalar_path_byte_for_byte()
    {
        if (!File.Exists(SwopPath)) return;
        var iccStream = new PdfStream(File.ReadAllBytes(SwopPath));
        var conv = new IccColorConverter();

        // 4 CMYK swatches (16 bytes). As a 4-sample buffer this stays under the parallel threshold
        // (scalar path); repeated to 20 000 samples it crosses it (parallel path). They must agree.
        byte[] swatch = [255, 0, 0, 0, 0, 255, 0, 0, 0, 0, 255, 0, 0, 0, 0, 255];
        byte[]? scalar = conv.TryConvertInterleavedToSrgb(iccStream, swatch, 4);
        Assert.NotNull(scalar);

        const int reps = 5000; // 4 * 5000 = 20 000 samples > threshold
        var big = new byte[swatch.Length * reps];
        for (var k = 0; k < reps; k++)
            Array.Copy(swatch, 0, big, k * swatch.Length, swatch.Length);

        byte[]? parallel = conv.TryConvertInterleavedToSrgb(iccStream, big, 4);
        Assert.NotNull(parallel);
        Assert.Equal(reps * scalar!.Length, parallel!.Length);

        // Each output sample must byte-match the scalar conversion of the repeating swatch.
        for (var i = 0; i < parallel.Length; i++)
            Assert.Equal(scalar[i % scalar.Length], parallel[i]);
    }

    [Fact]
    public void Black_point_compensation_darkens_the_raised_cmyk_black()
    {
        if (!File.Exists(SwopPath)) return;
        var iccStream = new PdfStream(File.ReadAllBytes(SwopPath));
        var conv = new IccColorConverter();

        // 100% K. SWOP's darkest black sits at PCS Y ≈ 0.043 (a raised black), so without BPC it
        // renders as a dark grey; with BPC it maps to the destination's true black → strictly darker.
        byte[] black = [0, 0, 0, 255];
        byte[]? off = conv.TryConvertInterleavedToSrgb(iccStream, black, 4, blackPointCompensation: false);
        byte[]? on = conv.TryConvertInterleavedToSrgb(iccStream, black, 4, blackPointCompensation: true);

        Assert.NotNull(off);
        Assert.NotNull(on);
        int sumOff = off![0] + off[1] + off[2];
        int sumOn = on![0] + on[1] + on[2];
        Assert.True(sumOn < sumOff, $"BPC ON should darken the raised SWOP black; off-sum={sumOff}, on-sum={sumOn}");
    }

    [Fact]
    public void Rendering_intent_name_flows_through_to_the_transform()
    {
        if (!File.Exists(SwopPath)) return;
        var iccStream = new PdfStream(File.ReadAllBytes(SwopPath));
        var conv = new IccColorConverter();

        double[] paperWhite = [0.0, 0.0, 0.0, 0.0]; // CMYK, no ink

        double[]? rel = conv.TryConvertToSrgb(iccStream, paperWhite, blackPointCompensation: false, renderingIntent: "RelativeColorimetric");
        double[]? abs = conv.TryConvertToSrgb(iccStream, paperWhite, blackPointCompensation: false, renderingIntent: "AbsoluteColorimetric");

        Assert.NotNull(rel);
        Assert.NotNull(abs);

        // The intent name must actually reach the ICC transform: relative normalises SWOP's media
        // white to ~pure white, absolute reproduces the dimmer real paper. If the name were ignored
        // the two would be identical.
        Assert.True(rel![1] > 0.99, $"relative paper white should be ~pure white; got G={rel[1]:F3}");
        Assert.True(rel[1] - abs![1] > 0.01, $"absolute should differ (dimmer); rel G={rel[1]:F3}, abs G={abs[1]:F3}");
    }
}
