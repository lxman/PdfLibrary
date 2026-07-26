using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ISO 32000-2 §8.6.8, Table 73 (rows for <c>CS</c> and <c>cs</c>), matrix row 4-4: setting a colour
/// space "shall also set the current […] colour to its initial value, which depends on the colour
/// space". §8.6.6.4 states the Separation case again — "the initial value for both the stroking and
/// nonstroking colour in the graphics state shall be 1.0" — and §8.6.6.5 the DeviceN case.
///
/// <para>
/// The engine did neither. A prior attempt initialised every space to zero, which renders Separation
/// as tint 0 (lightest), and was backed out wholesale rather than corrected — leaving <c>cs</c> to
/// leave the PREVIOUS colour in place. Every test here therefore sets a contrasting colour first: if
/// the initial value is not applied, the fill paints that stale carry-over and the assertion fails.
/// Without the prior colour these tests would pass against a renderer that simply defaulted to black.
/// </para>
///
/// <para>
/// The per-space values are not uniform, which is the trap the abandoned fix fell into.
/// <b>DeviceCMYK's initial colour is [0 0 0 1], not all-zeros</b> — all-zeros in CMYK is white, and the
/// clause requires black via the K plate. Separation and DeviceN initialise to 1.0, the opposite end
/// from the device spaces, because their tints are subtractive.
/// </para>
/// </summary>
public class InitialColorValueTests
{
    /// <summary>Sets a contrasting red, then selects /Cs0 and fills WITHOUT any sc/scn operator.</summary>
    private const string RedThenSelectCs0 = "1 0 0 rg /Cs0 cs 100 400 200 200 re f";

    private static void AssertBlack(SKColor c, string what) =>
        Assert.True(c.Red < 25 && c.Green < 25 && c.Blue < 25,
            $"{what} painted RGB({c.Red},{c.Green},{c.Blue}); expected near-black. A red result means " +
            "the previous colour carried over instead of the space's initial value being applied.");

    /// <summary>
    /// Row 4-4 for Separation: initial tint 1.0. The tint transform ramps white → black, so tint 1.0
    /// is black and tint 0.0 (the value the abandoned fix produced) would be white — the two failure
    /// modes are distinguishable from each other and from the red carry-over.
    /// </summary>
    [Fact]
    public void Separation_WithoutScn_UsesInitialTintOfOne()
    {
        const string cs = "[/Separation /Spot /DeviceRGB " +
                          "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>]";

        SKColor c = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build(cs, RedThenSelectCs0));

        AssertBlack(c, "/Separation with no scn");
    }

    /// <summary>
    /// Row 4-4 for DeviceN: "each component shall be given an initial value of 1.0" (§8.6.6.5). The
    /// type 4 transform maps (t₁,t₂) → (0, t₁, t₂, 0), so the required initial (1,1) yields
    /// DeviceCMYK(0,1,1,0) — red.
    ///
    /// <para>
    /// The prior colour here is BLUE, not the red the other cases use, precisely because this
    /// transform's correct answer IS red: against a red backdrop the test would pass whether the
    /// initial value was applied or the previous colour carried over. Blue separates all three
    /// outcomes — carry-over paints blue, correct initialisation paints red, and a wrongly-zeroed
    /// initial paints CMYK(0,0,0,0) = white.
    /// </para>
    /// </summary>
    [Fact]
    public void DeviceN_WithoutScn_UsesInitialTintOfOnePerComponent()
    {
        // (t₁ t₂) → (0 t₁ t₂ 0): push 0, roll top 3 by 1, push 0. Same transform the /All and /None
        // suites already use, so its behaviour is established rather than newly assumed.
        const string ps = "<< /FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1 0 1] /Length 16 >>\r\n" +
                          "stream\r\n{ 0 3 1 roll 0 }\r\nendstream";
        const string cs = "[/DeviceN [/SpotA /SpotB] /DeviceCMYK 5 0 R]";

        SKColor c = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build(cs, "0 0 1 rg /Cs0 cs 100 400 200 200 re f", withFont: false,
                extraResources: "", extraObjects: ps));

        Assert.True(c.Red > 200 && c.Green < 60 && c.Blue < 60,
            $"/DeviceN with no scn painted RGB({c.Red},{c.Green},{c.Blue}); initial tints of 1.0 map " +
            "through this transform to CMYK(0,1,1,0) = red. Blue means the previous colour carried " +
            "over; white means the initial tints were zeroed.");
    }

    /// <summary>
    /// Row 4-4 for DeviceCMYK: initial colour is <c>[0 0 0 1]</c> — black via the K plate — NOT
    /// all-zeros, which in CMYK is white. This is the case the abandoned fix would also have broken,
    /// and it is why "initialise everything to zero" is not the fix.
    /// </summary>
    [Fact]
    public void DeviceCmyk_WithoutScn_UsesBlackNotWhite()
    {
        SKColor c = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build("/DeviceRGB",
                "1 0 0 rg /DeviceCMYK cs 100 400 200 200 re f"));

        AssertBlack(c, "/DeviceCMYK with no sc");
    }

    /// <summary>Row 4-4 for DeviceRGB: initial colour is all components 0.0, i.e. black.</summary>
    [Fact]
    public void DeviceRgb_WithoutScn_UsesBlack()
    {
        SKColor c = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build("/DeviceRGB",
                "1 0 0 rg /DeviceRGB cs 100 400 200 200 re f"));

        AssertBlack(c, "/DeviceRGB with no sc");
    }

    /// <summary>
    /// Row 4-4 for Lab: L is fixed at 0.0 (always valid — L's range is [0,100] by definition), but a
    /// and b must clamp to whatever <c>/Range</c> the space declares. Here <c>/Range [20 80 -50 50]</c>
    /// excludes a = 0, so a correctly-clamped initial value is (L=0, a=20, b=0), not (0,0,0) — and the
    /// two are visually distinguishable: with a forced to 0 (the un-clamped/bugged answer)
    /// <c>LabToSrgb</c> converts (0,0,0) to black, RGB(0,0,0), but the correctly-clamped (0,20,0)
    /// converts to RGB(33,0,1) — the small but nonzero and reproducible chroma shift a nonzero *a*
    /// injects even at zero lightness. Verified by mutation: forcing <c>InitialColorFor</c>'s Lab case
    /// to ignore <c>/Range</c> and always return a=0 makes this test fail with RGB(0,0,0), and reverting
    /// restores the pass — see the sweep's fix report for the transcript.
    /// </summary>
    [Fact]
    public void Lab_WithoutScn_ClampsAToDeclaredRange()
    {
        const string cs = "[/Lab << /Range [20 80 -50 50] >>]";
        SKColor c = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build(cs, RedThenSelectCs0));

        Assert.True(Math.Abs(c.Red - 33) <= 10 && Math.Abs(c.Green - 0) <= 10 && Math.Abs(c.Blue - 1) <= 10,
            $"/Lab with no sc painted RGB({c.Red},{c.Green},{c.Blue}); expected near RGB(33,0,1) — the " +
            "colour (L=0, a=20, b=0) converts to, once a is correctly clamped up to the declared " +
            "/Range's lower bound of 20 rather than left at the un-clamped 0. RGB(255,0,0) means the " +
            "previous colour carried over; RGB(0,0,0) means a was left at 0 instead of being clamped.");
    }

    /// <summary>
    /// Row 4-4 for ICCBased: each component clamps to its profile's declared per-channel <c>/Range</c>
    /// (default [0,1], same as PDF component ranges generally). Here <c>/N 4</c> with
    /// <c>/Range [0.3 1 0 1 0 1 0 1]</c> excludes C = 0, so the correctly-clamped initial colour is
    /// (C=0.3, M=0, Y=0, K=0), not all-zero. The stream carries no real ICC profile bytes, so the ICC
    /// transform fails to parse and <c>ResolveICCBased</c> falls back to interpreting the 4 components
    /// as <c>DeviceCMYK</c> directly — CMYK(0.3,0,0,0) is a pale cyan, RGB(178,255,255), distinguishable
    /// both from the red carry-over and from CMYK(0,0,0,0) = white (the un-clamped answer).
    /// </summary>
    [Fact]
    public void ICCBased_WithoutScn_ClampsToDeclaredRange()
    {
        const string icc = "<< /N 4 /Range [0.3 1 0 1 0 1 0 1] /Length 0 >>\r\nstream\r\n\r\nendstream";
        const string cs = "[/ICCBased 5 0 R]";
        SKColor c = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build(cs, RedThenSelectCs0, withFont: false, extraResources: "", extraObjects: icc));

        Assert.True(Math.Abs(c.Red - 178) <= 10 && c.Green > 245 && c.Blue > 245,
            $"/ICCBased with no sc painted RGB({c.Red},{c.Green},{c.Blue}); expected near RGB(178,255,255) " +
            "— CMYK(0.3,0,0,0), once C is correctly clamped up to the declared /Range's lower bound of " +
            "0.3 rather than left at the un-clamped 0. RGB(255,0,0) means the previous colour carried " +
            "over; RGB(255,255,255) means C was left at 0 instead of being clamped.");
    }
}
