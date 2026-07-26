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
/// other.
/// </para>
/// </summary>
public class SeparationTintRangeTests
{
    private const string Cs = "[/Separation /Spot /DeviceCMYK " +
                              "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 1 0 0] /N 1 >>]";

    private static SKColor AtTint(string tint) => ColourConformancePage.RenderCentre(
        ColourConformancePage.Build(Cs, ColourConformancePage.FillRect($"/Cs0 cs {tint} scn")));

    /// <summary>Channel tolerance for the absolute-colour assertions: these go through an ICC
    /// CMYK→sRGB conversion, so exact byte equality isn't the right bar.</summary>
    private static void AssertNear(SKColor c, byte r, byte g, byte b, string what) =>
        Assert.True(Math.Abs(c.Red - r) <= 12 && Math.Abs(c.Green - g) <= 12 && Math.Abs(c.Blue - b) <= 12,
            $"{what}: got RGB({c.Red},{c.Green},{c.Blue}), expected near RGB({r},{g},{b})");

    [Fact]
    public void TintAboveOne_ClampsToOne()
    {
        SKColor above = AtTint("1.5");
        SKColor at = AtTint("1");

        // Equality alone is vacuous (see class doc): also pin the absolute colour tint 1 must
        // produce — C1 [0 1 0 0] is CMYK magenta, ≈ RGB(255,0,255).
        AssertNear(above, 255, 0, 255, "AtTint(\"1.5\")");
        Assert.True(above == at,
            "A tint above 1.0 must behave as 1.0 (§8.6.6.4 bounds the component to [0.0, 1.0])");
    }

    [Fact]
    public void TintBelowZero_ClampsToZero()
    {
        SKColor below = AtTint("-0.5");
        SKColor at = AtTint("0");

        // Equality alone is vacuous (see class doc): also pin the absolute colour tint 0 must
        // produce — C0 [0 0 0 0] is no ink, which over the harness's blank page is white.
        AssertNear(below, 255, 255, 255, "AtTint(\"-0.5\")");
        Assert.True(below == at,
            "A tint below 0.0 must behave as 0.0 (§8.6.6.4 bounds the component to [0.0, 1.0])");
    }
}
