using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ISO 32000-2 §8.6.6.4, matrix row 4-2: "A colour value in a Separation colour space shall consist of
/// a single tint component in the range 0.0 to 1.0."
///
/// <para>
/// The component-count half is already enforced — <c>ResolveSeparation</c> acts only when
/// <c>color.Count == 1</c>. This pins the range half: a tint outside [0,1] must behave as the nearest
/// valid tint rather than extrapolating the transform beyond its domain, which would produce colours
/// no valid file could request.
/// </para>
///
/// <para>
/// Each test asserts two things, not one. The equality half (<c>AtTint("1.5") == AtTint("1")</c>)
/// alone would be vacuous: if the space failed to resolve at all for some unrelated reason, both
/// sides would paint the same WRONG colour (e.g. the resource-name fallback, or an unresolved raw
/// tint) and the comparison would still hold. Asserting the absolute expected colour in addition —
/// magenta at tint 1, white at tint 0, per the transform <c>C0 [0 0 0 0] → C1 [0 1 0 0]</c> — rules
/// that out: both sides must land on the colour the clause actually predicts, not merely on each
/// other. <c>AtTint</c> also paints a red backdrop before the tint fill (see below), so "painted
/// nothing at all" is a third distinguishable failure mode rather than one that could coincide with
/// the white tint-0 answer.
/// </para>
///
/// <para>
/// <b>Known limitation, found by mutation (widening this class's own <c>/Domain</c> to <c>[-1 2]</c>
/// and re-running):</b> only the low half of the range is actually pinned by these tests.
/// <c>TintBelowZero_ClampsToZero</c> fails under that mutation, as intended — with the domain no
/// longer clamping, tint −0.5 flows through unclamped to CMYK magenta = −0.5, and
/// <c>ColorConverter.ConvertColor</c>'s DeviceCMYK branch has no lower clamp
/// (<c>Math.Min(1.0, m*(1-k)+k)</c> only bounds the high side), so the byte cast overflows to
/// RGB(255,126,255) instead of white. But <c>TintAboveOne_ClampsToOne</c> passes unchanged under the
/// same mutation: tint 1.5 flows through unclamped to M = 1.5, and <c>ColorConverter</c>'s own
/// <c>Math.Min(1.0, 1.5)</c> saturates it to the exact same byte as the correctly-clamped M = 1.0
/// case. The high-side clamp is redundantly re-enforced one layer downstream of the thing this test
/// means to pin, so no assertion reachable through <c>DeviceCMYK</c>'s magenta channel — endpoint or
/// intermediate — can tell "clamped upstream" apart from "unclamped upstream, saturated downstream".
/// The high side of row 4-2 is therefore conformant by inspection of <c>ExponentialFunction</c> and the
/// required <c>/Domain</c> on every conformant tint transform (§7.10.1 Table 38), not by a test that
/// has been seen to fail — see the row's own note in <c>rendering-conformance.md</c>.
/// </para>
/// </summary>
public class SeparationTintRangeTests
{
    private const string Cs = "[/Separation /Spot /DeviceCMYK " +
                              "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 1 0 0] /N 1 >>]";

    /// <summary>
    /// Paints a red backdrop before selecting <c>/Cs0</c> and filling with <paramref name="tint"/>, so a
    /// space that resolved to "paint nothing at all" leaves red visible instead of coincidentally
    /// matching an expected white or wrapped-overflow colour.
    /// </summary>
    private static SKColor AtTint(string tint) => ColourConformancePage.RenderCentre(
        ColourConformancePage.Build(Cs,
            ColourConformancePage.FillRect($"1 0 0 rg 100 400 200 200 re f /Cs0 cs {tint} scn")));

    /// <summary>Channel tolerance for the absolute-colour assertions: these go through
    /// <c>ColorConverter</c>'s naive (non-ICC) CMYK→RGB formula — <c>DeviceCMYK</c> has no ICC profile
    /// to convert through — whose <c>(byte)</c> truncation of a <c>* 255</c> multiply can be off by a
    /// few units at a non-integral tint, so exact byte equality isn't the right bar.</summary>
    private static void AssertNear(SKColor c, byte r, byte g, byte b, string what) =>
        Assert.True(Math.Abs(c.Red - r) <= 12 && Math.Abs(c.Green - g) <= 12 && Math.Abs(c.Blue - b) <= 12,
            $"{what}: got RGB({c.Red},{c.Green},{c.Blue}), expected near RGB({r},{g},{b})");

    /// <summary>
    /// Pins the high end of the range AND the shape of the ramp between the endpoints — see the class
    /// doc for the mutation trace showing that the endpoint-only assertions cannot, on their own,
    /// distinguish "clamped to 1.0 before the transform" from "left at 1.5, saturated to the same byte
    /// downstream by <c>ColorConverter</c>". The midpoint assertion does not close that gap (0.5 is
    /// inside every domain this test can construct, so it behaves identically whether or not the
    /// out-of-range clamp exists) — it strengthens the test against a different bug class (e.g. a
    /// transposed or mis-scaled interpolation) rather than the one row 4-2 is about, and that is stated
    /// honestly rather than presented as a fix for the mutation.
    /// </summary>
    [Fact]
    public void TintAboveOne_ClampsToOne()
    {
        SKColor above = AtTint("1.5");
        SKColor at = AtTint("1");
        SKColor mid = AtTint("0.5");

        // Equality alone is vacuous (see class doc): also pin the absolute colour tint 1 must
        // produce — C1 [0 1 0 0] is CMYK magenta, ≈ RGB(255,0,255).
        AssertNear(above, 255, 0, 255, "AtTint(\"1.5\")");
        Assert.True(above == at,
            "A tint above 1.0 must behave as 1.0 (§8.6.6.4 bounds the component to [0.0, 1.0])");

        // Pins the ramp itself, not just its endpoints: halfway between C0 [0 0 0 0] and C1 [0 1 0 0]
        // is M = 0.5, i.e. CMYK(0, 0.5, 0, 0) ≈ RGB(255, 128, 255).
        AssertNear(mid, 255, 128, 255, "AtTint(\"0.5\")");
    }

    [Fact]
    public void TintBelowZero_ClampsToZero()
    {
        SKColor below = AtTint("-0.5");
        SKColor at = AtTint("0");

        // Equality alone is vacuous (see class doc): also pin the absolute colour tint 0 must
        // produce — C0 [0 0 0 0] is no ink, which over the harness's blank page is white. The red
        // backdrop AtTint paints before the fill means "resolved to white" and "painted nothing" are
        // no longer the same observation, unlike a plain blank-page backdrop.
        AssertNear(below, 255, 255, 255, "AtTint(\"-0.5\")");
        Assert.True(below == at,
            "A tint below 0.0 must behave as 0.0 (§8.6.6.4 bounds the component to [0.0, 1.0])");
    }
}
