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
}
