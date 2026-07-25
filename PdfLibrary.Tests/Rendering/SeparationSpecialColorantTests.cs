using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// The two reserved Separation colourant names, <c>/None</c> and <c>/All</c>, per ISO 32000-2 §8.6.6.4.
///
/// <para>
/// Both are special-cased by the clause in ways an ordinary spot colourant is not, and both were being
/// resolved as if they were ordinary spots — the colourant name was read only to derive an overprint
/// plate mask, never to decide what gets painted. The tint transform therefore ran for them, which the
/// clause forbids outright:
/// </para>
///
/// <list type="bullet">
/// <item><b>4-8 / 4-9</b> — <c>/None</c> "shall not produce any visible output […] shall have no effect
/// on the current page", "on all devices".</item>
/// <item><b>4-10</b> — for <c>/All</c> and <c>/None</c>, "PDF processors shall ignore the
/// <i>alternateSpace</i> and <i>tintTransform</i> parameters".</item>
/// <item><b>4-7</b> — <c>/All</c> on an additive device: "the subtractive tint values […] shall be
/// complemented by subtracting from 1 before applying to all available colourants". On a display the
/// available colourants are R, G and B, so tint <c>t</c> paints the neutral <c>1 − t</c>.</item>
/// </list>
///
/// <para>
/// These are painted-output claims, so they are asserted on rendered pixels rather than on the resolver's
/// return value: a resolver that returns a colour nobody paints, and a resolver that returns nothing
/// while the renderer paints black, are indistinguishable from the clause's point of view.
/// </para>
/// </summary>
public class SeparationSpecialColorantTests
{
    /// <summary>Fills the shared test rect with tint <paramref name="tint"/> of /Cs0.</summary>
    private static string FillRect(double tint) => ColourConformancePage.FillRect(
        $"/Cs0 cs {tint.ToString(System.Globalization.CultureInfo.InvariantCulture)} scn");

    /// <summary>A type 2 tint transform ramping white -> <paramref name="c1"/> in DeviceRGB.</summary>
    private static string RgbTint(string c1) => ColourConformancePage.ExponentialTint("1 1 1", c1);

    /// <summary>
    /// Renders <paramref name="pdf"/> and asserts that every pixel well inside the red rectangle is still
    /// red — i.e. the <c>/None</c> operator that followed marked nothing anywhere in the region, not just
    /// at one sampled point. Insets by 5px so path antialiasing at the rect's own edges is not counted.
    /// </summary>
    private static void AssertRedRectUntouched(byte[] pdf, string what) =>
        ColourConformancePage.ForEachPixelInRect(pdf, (x, y, c) =>
            Assert.True(c.Red > 235 && c.Green < 20 && c.Blue < 20,
                $"{what} marked the page at ({x},{y}): RGB({c.Red},{c.Green},{c.Blue}) is not the " +
                "underlying red. §8.6.6.4 requires /None to have no effect on the current page"));

    /// <summary>
    /// ISO 32000-2 §8.6.6.4, row 4-8: a Separation space whose colourant is <c>/None</c> "shall not
    /// produce any visible output […] shall have no effect on the current page" — regardless of what its
    /// tint transform would return. Here the transform ramps to solid black at tint 1, so an
    /// implementation that evaluates it paints a black rectangle.
    ///
    /// <para>
    /// The <c>/None</c> fill is laid <i>over an existing red rectangle</i> deliberately. "No visible
    /// output" is a claim about the painting operator being suppressed, not about the colour it would
    /// have resolved to; against a white page a resolver that merely returned white would look correct
    /// while still marking the page. Over red, only genuine suppression survives.
    /// </para>
    /// </summary>
    [Fact]
    public void SeparationNone_Fill_LeavesExistingContentUntouched()
    {
        string content = "1 0 0 rg 100 400 200 200 re f " + FillRect(1.0);
        byte[] pdf = ColourConformancePage.Build($"[/Separation /None /DeviceRGB {RgbTint("0 0 0")}]", content);

        AssertRedRectUntouched(pdf, "/None fill");
    }

    /// <summary>
    /// Row 4-8 again, for the stroking operator. "Shall have no effect on the current page" is not
    /// specific to <c>f</c>; a 20pt <c>/None</c> line straight through the red rect must leave no trace.
    /// </summary>
    [Fact]
    public void SeparationNone_Stroke_LeavesExistingContentUntouched()
    {
        const string content = "1 0 0 rg 100 400 200 200 re f " +
                               "/Cs0 CS 1 SCN 20 w 100 500 m 300 500 l S";
        byte[] pdf = ColourConformancePage.Build($"[/Separation /None /DeviceRGB {RgbTint("0 0 0")}]", content);

        AssertRedRectUntouched(pdf, "/None stroke");
    }

    /// <summary>
    /// Row 4-8 for glyphs. Text is filled with the non-stroking colour under the default render mode, so
    /// <c>/None</c> text must paint nothing either.
    /// </summary>
    [Fact]
    public void SeparationNone_Text_LeavesExistingContentUntouched()
    {
        const string content = "1 0 0 rg 100 400 200 200 re f " +
                               "/Cs0 cs 1 scn BT /F1 48 Tf 110 480 Td (NONE) Tj ET";
        byte[] pdf = ColourConformancePage.Build($"[/Separation /None /DeviceRGB {RgbTint("0 0 0")}]", content, withFont: true);

        AssertRedRectUntouched(pdf, "/None text");
    }

    /// <summary>
    /// ISO 32000-2 §8.6.6.5, row 5-9: a DeviceN space whose components are <i>all</i> <c>/None</c>
    /// "shall always discard its output […] it shall never revert to the alternate colour space".
    ///
    /// <para>
    /// The second half is the part that is easy to get wrong, and is why the tint transform here ramps
    /// to solid black: an implementation that reverts — the ordinary DeviceN path — paints a black
    /// rectangle over the red one. Discarding the output is not the same as evaluating the transform and
    /// then ignoring the result, and it is certainly not the same as painting white.
    /// </para>
    /// </summary>
    [Fact]
    public void AllNoneDeviceN_DiscardsOutput_WithoutRevertingToItsAlternate()
    {
        // (tA tB) → (0 tA tB 0) in DeviceCMYK. Tints (1, 0) give CMYK(0,1,0,0) — magenta, which differs
        // from the red backdrop in the blue channel by the full range. Tints must be chosen to CONTRAST
        // with the backdrop: (1, 1) would give CMYK(0,1,1,0), which is red, and a reverting renderer
        // would then paint the rect its existing colour and pass this test without discarding anything.
        const string ps = "<< /FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1 0 1] /Length 16 >>\r\n" +
                          "stream\r\n{ 0 3 1 roll 0 }\r\nendstream";
        const string content = "1 0 0 rg 100 400 200 200 re f " +
                               "/Cs0 cs 1 0 scn 100 400 200 200 re f";

        byte[] pdf = ColourConformancePage.Build(
            "[/DeviceN [/None /None] /DeviceCMYK 5 0 R]", content, withFont: false, ps);

        AssertRedRectUntouched(pdf, "all-/None DeviceN");
    }

    /// <summary>
    /// ISO 32000-2 §8.6.6.4, rows 4-7 and 4-10: for <c>/All</c> the processor "shall ignore the
    /// alternateSpace and tintTransform parameters", and on an additive device the tint "shall be
    /// complemented by subtracting from 1 before applying to all available colourants". Tint 1 therefore
    /// paints black, not the red this space's tint transform ramps to.
    /// </summary>
    [Fact]
    public void SeparationAll_AtFullTint_IgnoresTintTransformAndPaintsBlack()
    {
        byte[] pdf = ColourConformancePage.Build($"[/Separation /All /DeviceRGB {RgbTint("1 0 0")}]", FillRect(1.0));

        SKColor c = ColourConformancePage.RenderCentre(pdf);

        Assert.True(c.Red < 20 && c.Green < 20 && c.Blue < 20,
            $"/All at tint 1 painted RGB({c.Red},{c.Green},{c.Blue}); §8.6.6.4 requires the complement " +
            "(black) applied to all colourants, with the tint transform ignored");
    }

    /// <summary>
    /// The companion to the above: <c>/All</c> at tint 0 is the <i>minimum</i> concentration of every
    /// colourant, so its complement is 1 and the page stays white. Without this case a renderer that
    /// simply painted black for every <c>/All</c> fill would look conformant.
    /// </summary>
    [Fact]
    public void SeparationAll_AtZeroTint_PaintsWhite()
    {
        byte[] pdf = ColourConformancePage.Build($"[/Separation /All /DeviceRGB {RgbTint("1 0 0")}]", FillRect(0.0));

        SKColor c = ColourConformancePage.RenderCentre(pdf);

        Assert.True(c.Red > 235 && c.Green > 235 && c.Blue > 235,
            $"/All at tint 0 painted RGB({c.Red},{c.Green},{c.Blue}); the complement of 0 is full " +
            "intensity on every additive colourant, i.e. white");
    }
}
