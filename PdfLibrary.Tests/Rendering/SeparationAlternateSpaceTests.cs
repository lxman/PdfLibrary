using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Reversion to the alternate colour space — ISO 32000-2 §8.6.6.4 rows 4-3, 4-12, 4-13 and 4-15, and
/// §8.6.6.5 rows 5-2 and 5-4.
///
/// <para>
/// On an additive device "a Separation colour space never applies a process colourant directly; it
/// always reverts to the alternate colour space", and for an unavailable colourant on any device the
/// processor "shall arrange for subsequent painting operations to be performed in an alternate colour
/// space". Pellucid's RGB display path is that additive device, and the D-1 audit established that it
/// has no route to the spot machinery at all — but nothing asserted the resulting pixel, so the
/// behaviour was conformant by inspection only.
/// </para>
///
/// <para>
/// The oracle here is deliberately not a hard-coded RGB triple: it is <i>the same colour painted
/// directly in the alternate space</i>. "Reverts to the alternate colour space" means exactly that the
/// two must be indistinguishable, so comparing them tests the clause rather than testing whichever
/// CMYK→RGB conversion this build happens to use. A hard-coded expectation would have to be rewritten
/// every time that conversion is refined, and would quietly stop testing reversion.
/// </para>
/// </summary>
public class SeparationAlternateSpaceTests
{
    /// <summary>Asserts two content streams paint the identical colour at the rect centre.</summary>
    private static void AssertSamePixel(string colorSpaceDef, string viaSeparation, string direct,
        string clause, params string[] extraObjects)
    {
        SKColor actual = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build(colorSpaceDef, ColourConformancePage.FillRect(viaSeparation),
                withFont: false, extraObjects: extraObjects));
        SKColor expected = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build("/DeviceRGB", ColourConformancePage.FillRect(direct)));

        Assert.True(actual == expected,
            $"{clause}: painted RGB({actual.Red},{actual.Green},{actual.Blue}) but the alternate space " +
            $"painted directly gives RGB({expected.Red},{expected.Green},{expected.Blue})");
    }

    /// <summary>
    /// Rows 4-12 / 4-13 / 4-15: a spot colourant the device does not have must revert, painting exactly
    /// what its tint transform yields in the alternate space. The transform is the linear ramp
    /// <c>[0 0 0 0] → [0 1 0 0]</c>, so tint 0.5 is DeviceCMYK (0, 0.5, 0, 0) — the same colour the
    /// direct <c>k</c> fill sets.
    /// </summary>
    [Fact]
    public void SpotSeparation_OnAdditiveDevice_PaintsItsAlternateSpaceColour()
    {
        string cs = $"[/Separation /PANTONE#20185#20C /DeviceCMYK " +
                    $"{ColourConformancePage.ExponentialTint("0 0 0 0", "0 1 0 0")}]";

        AssertSamePixel(cs, "/Cs0 cs 0.5 scn", "0 0.5 0 0 k",
            "§8.6.6.4 row 4-13 — spot Separation must revert to its alternate space");
    }

    /// <summary>
    /// Row 4-15 again at the ramp's far end: the tint transform "shall be called with the tint value",
    /// so a different tint must produce a correspondingly different colour. Without this case a
    /// renderer that ignored the tint and always evaluated at some fixed point would pass the test above.
    /// </summary>
    [Fact]
    public void SpotSeparation_AtFullTint_PaintsTheTransformsEndpoint()
    {
        string cs = $"[/Separation /PANTONE#20185#20C /DeviceCMYK " +
                    $"{ColourConformancePage.ExponentialTint("0 0 0 0", "0 1 0 0")}]";

        AssertSamePixel(cs, "/Cs0 cs 1 scn", "0 1 0 0 k",
            "§8.6.6.4 row 4-15 — the tint transform must be called with the current tint");
    }

    /// <summary>
    /// Row 4-3: "tints shall always be treated as subtractive colours, even if the device produces
    /// output for the designated component by an additive method" — 0.0 is the minimum concentration of
    /// colourant and 1.0 the maximum, so on paper and on screen alike a higher tint is the darker one.
    /// This is the clause most likely to be inverted by an implementation thinking in RGB, and it is not
    /// implied by the two tests above: both would pass with the ramp running backwards, because the
    /// direct-fill oracle would be inverted in exactly the same way.
    /// </summary>
    [Fact]
    public void SeparationTints_AreSubtractive_HigherTintIsDarker()
    {
        string cs = $"[/Separation /PANTONE#20185#20C /DeviceCMYK " +
                    $"{ColourConformancePage.ExponentialTint("0 0 0 0", "0 1 0 0")}]";

        SKColor light = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build(cs, ColourConformancePage.FillRect("/Cs0 cs 0 scn")));
        SKColor dark = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build(cs, ColourConformancePage.FillRect("/Cs0 cs 1 scn")));

        static int Luma(SKColor c) => (299 * c.Red + 587 * c.Green + 114 * c.Blue) / 1000;

        Assert.True(Luma(light) > Luma(dark),
            $"§8.6.6.4 row 4-3: tint 0 gave luma {Luma(light)} and tint 1 gave {Luma(dark)}; tints are " +
            "subtractive, so 0 is the minimum colourant (lightest) and 1 the maximum (darkest)");
    }

    /// <summary>
    /// §8.6.6.5 rows 5-2 and 5-4: a DeviceN space whose component names are not available on the device
    /// performs "subsequent painting operations in the alternate colour space", with the tint transform
    /// "called with n tint values" returning m components. The type 4 transform here maps
    /// (tA, tB) → (0, tA, tB, 0) in DeviceCMYK, so both inputs must reach it in the right order — a
    /// renderer that dropped or transposed a component paints a visibly different colour.
    /// </summary>
    [Fact]
    public void DeviceN_RevertsToAlternate_PassingEveryTintToTheTransform()
    {
        // PostScript: stack (tA tB) → push 0, roll top 3 by 1 → (0 tA tB), push 0 → (0 tA tB 0).
        const string ps = "<< /FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1 0 1] /Length 16 >>\r\n" +
                          "stream\r\n{ 0 3 1 roll 0 }\r\nendstream";
        const string cs = "[/DeviceN [/SpotA /SpotB] /DeviceCMYK 5 0 R]";

        AssertSamePixel(cs, "/Cs0 cs 0.25 0.75 scn", "0 0.25 0.75 0 k",
            "§8.6.6.5 rows 5-2/5-4 — DeviceN must revert through its n-input tint transform", ps);
    }
}
