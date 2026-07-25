using System;
using System.IO;
using System.Linq;
using ICCSharp.IO;
using ICCSharp.Profile;
using ICCSharp.Transform;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Conformance;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Black-point detection on a <b>v4</b> ICC profile.
///
/// <para>
/// <see cref="IccTwoProfileTransform.DetectBlackPoint"/> round-trips PCS black through the profile
/// (<c>Lab(0,0,0) → B2A(relative) → device → A2B(relative) → PCS</c>). v2 profiles reach that through
/// the legacy lut8/lut16 tags; v4 profiles reach it through the mAB/mBA multi-stage pipelines — a
/// genuinely different code path with different PCS encoding. Every profile embedded in the veraPDF,
/// BFO and PDF Standards corpora is v2, so only the GWG Ghent Output Suite exercises v4 at all.
/// </para>
///
/// <para>
/// The expectation below is not invented: it is littleCMS's answer for this exact profile under the
/// same round trip — the same engine poppler and Ghostscript use. GWG205's declared <c>bkpt</c> tag
/// claims L* 7.88 while the profile can only reach L* 12.16, so the tag is <b>too dark</b> here. That
/// is the opposite error from the Agfa SWOP v2 profile, whose tag is far too light (L* 24.62 against a
/// reachable 7.84). Detection has to track the profile either way; a rule of thumb like "the tag is
/// always optimistic" would get one of the two wrong.
/// </para>
///
/// <para>LocalOnly: the GOS suite is a sibling checkout, absent on CI.</para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class V4BlackPointDetectionTests
{
    /// <summary>littleCMS's round-trip black for the GWG205 profile: L* 12.16 → Y ≈ 0.014301 (D50).</summary>
    private const double ReferenceBlackY = 0.014301;

    /// <summary>The profile's own /bkpt tag: Y ≈ 0.008728 (L* 7.88) — darker than actually reachable.</summary>
    private const double DeclaredTagY = 0.008728;

    /// <summary>The v4 CMYK ICC profile embedded in GWG205, or null when the GOS suite is absent.</summary>
    private static byte[]? LoadGwg205Profile()
    {
        if (!GwgGosHarness.IsAvailable)
            return null;

        string? path = GwgGosHarness.PdfX4Files()
            .FirstOrDefault(p => Path.GetFileName(p).StartsWith("GWG205", StringComparison.OrdinalIgnoreCase));
        if (path is null)
            return null;

        using PdfDocument doc = PdfDocument.Load(path);
        for (var i = 0; i < doc.PageCount; i++)
        {
            PdfPage? page = doc.GetPage(i);
            if (page is null)
                continue;

            foreach (PdfImage image in page.GetImages())
            {
                PdfArray? cs = image.ColorSpaceArray;
                if (cs is not { Count: >= 2 } || cs[0] is not PdfName { Value: "ICCBased" })
                    continue;

                PdfObject? streamObj = cs[1];
                if (streamObj is PdfIndirectReference r)
                    streamObj = doc.ResolveReference(r);
                if (streamObj is PdfStream profile)
                    return profile.GetDecodedData();
            }
        }
        return null;
    }

    [Fact]
    public void V4_profile_black_point_matches_the_littleCMS_reference()
    {
        byte[]? bytes = LoadGwg205Profile();
        Assert.SkipUnless(bytes is not null, "gwg-gos not present (GWG205 v4 profile unavailable)");

        IccProfile profile = IccProfile.Parse(bytes!);
        Assert.Equal(4, profile.Header.Version.Major);         // guard: still the v4 fixture we think it is

        XyzNumber detected = IccTwoProfileTransform.DetectBlackPoint(profile);

        // Must not silently fall back to zero — that would mean the v4 branch degrades to "no BPC"
        // without saying so, which is exactly the failure this test exists to catch.
        Assert.True(detected.Y > 0.0,
            "v4 detection returned zero — the mAB/mBA round trip did not run");

        // Measured agreement is 3.9% (Y 0.013737 against 0.014301, i.e. L* 11.77 against 12.16 — two
        // independent CMMs interpolating the same CLUT). 10% leaves ~2.5x headroom for that while still
        // excluding both failure modes worth catching: the declared tag (39% low) and a zero fallback.
        Assert.True(Math.Abs(detected.Y - ReferenceBlackY) <= ReferenceBlackY * 0.10,
            $"detected Y={detected.Y:F6}, littleCMS reference {ReferenceBlackY:F6}");
    }

    [Fact]
    public void V4_detection_does_not_simply_echo_the_bkpt_tag()
    {
        byte[]? bytes = LoadGwg205Profile();
        Assert.SkipUnless(bytes is not null, "gwg-gos not present (GWG205 v4 profile unavailable)");

        IccProfile profile = IccProfile.Parse(bytes!);
        XyzNumber declared = profile.BlackPoint ?? new XyzNumber(0, 0, 0);
        XyzNumber detected = IccTwoProfileTransform.DetectBlackPoint(profile);

        // Sanity-check the fixture itself: if GWG reissues the suite with a corrected tag, the premise
        // of this test changes and it should be re-derived rather than silently still passing.
        Assert.True(Math.Abs(declared.Y - DeclaredTagY) < 0.0005,
            $"GWG205's bkpt tag moved (Y={declared.Y:F6}); re-derive the reference before trusting this test");

        // Detection must reach the LIGHTER, actually-reachable black rather than echoing the darker tag.
        Assert.True(detected.Y > declared.Y * 1.3,
            $"detected Y={detected.Y:F6} tracks the tag ({declared.Y:F6}) instead of the profile");
    }
}
