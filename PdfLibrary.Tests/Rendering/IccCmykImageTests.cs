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
}
