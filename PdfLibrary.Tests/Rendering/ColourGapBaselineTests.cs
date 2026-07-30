using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Baseline pins for the open colour-gap entries G-8 and G-10, plus the G-13 observation test —
/// see Docs/colour/rendering-conformance.md. Each baseline asserts TODAY'S measured behaviour;
/// the comment names the ruled goal, and the eventual fix must flip the pin red and retire it
/// deliberately (the G-14 pattern).
/// </summary>
public class ColourGapBaselineTests
{
    // G-8 BASELINE: a shading used as a PATTERN (PatternType 2 via scn) does not consult
    // PaintsNothing — only OnFill's fill-space gate does, and the FILL space here is /Pattern,
    // not /None. The pattern resolves and FillWithShadingPattern has no PaintsNothing check
    // (unlike OnPaintShading's sh route), so the shading route paints. The tint transform
    // (object 8) is NEVER EVALUATED: ShadingBuilder.BuildColorMapper calls
    // ColorSpaceResolver.BuildTintToRgb, which declines the /None colourant at
    // ColorSpaceResolver.cs:414 and returns null before PdfFunction.Create ever touches the
    // Separation's tint transform. BuildColorMapper then falls through to the ToArgbByCount
    // fallback, which reads the shading /Function's single 1.0 tint component as DeviceGray
    // level 1.0 = white. So the fixture's element-8 tint transform is dead weight — it is never
    // consulted, and any C0/C1 pair would still measure white. Ruled goal (§8.6.6.4, G-7's rule
    // extended to the pattern route): a /None shading paints nothing and the red backdrop
    // survives.
    [Fact]
    public void NoneShadingPattern_paints_G8Baseline()
    {
        // Objects from 5: pattern → shading → shading function (t → tint, 1-out, constant 1.0)
        // → Separation tint transform (tint → RGB, 3-out, constant black). The two functions are
        // DISTINCT on purpose: the shading /Function outputs the space's 1 tint component; the
        // Separation's element-3 transform outputs the alternate's 3.
        const string pattern = "<< /Type /Pattern /PatternType 2 /Matrix [1 0 0 1 0 0] /Shading 6 0 R >>";
        const string shading = "<< /ShadingType 2 /ColorSpace [/Separation /None /DeviceRGB 8 0 R] " +
                               "/Coords [100 500 300 500] /Domain [0 1] /Extend [true true] /Function 7 0 R >>";
        const string shadingFn = "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [1] /N 1 >>";
        const string tint = "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [0 0 0] /N 1 >>";
        const string content = "1 0 0 rg 100 400 200 200 re f " +
                               "/Pattern cs /P1 scn 100 400 200 200 re f";

        byte[] pdf = ColourConformancePage.Build("/DeviceRGB", content, withFont: false,
            extraResources: " /Pattern << /P1 5 0 R >>", pattern, shading, shadingFn, tint);

        SKColor c = ColourConformancePage.RenderCentre(pdf);
        // MEASURED (not the predicted constant black): the shading route paints white, not the
        // tint transform's C0/C1 black. Corrected 2026-07-29 per Task 2's STOP rule — a
        // non-black paint that still covers the red backdrop is a measurement correction, not a
        // blocking gap-entry mismatch. The pattern still PAINTS (not the red backdrop surviving),
        // so G-8's routing claim (pattern route bypasses PaintsNothing) stands; only the specific
        // colour it happens to paint was mispredicted.
        Assert.True(c.Red > 235 && c.Green > 235 && c.Blue > 235,
            $"G-8 baseline moved: /None shading pattern painted RGB({c.Red},{c.Green},{c.Blue}), " +
            "expected white. If it now leaves the red backdrop, G-8 is FIXED — " +
            "retire this pin deliberately and update the matrix.");
    }

    // G-10 BASELINE: TextPaintsNothing masks RenderingMode with & 3, so mode 4 (fill + add to
    // clip) with a /None fill skips the entire glyph render — INCLUDING the add-to-clip half.
    // The blue fill painted after ET therefore covers the whole rect unclipped. Ruled goal
    // (row 4-8's clause, G-10): /None suppresses the FILL only; mode 4 must still establish the
    // glyph clip, so the trailing blue would land only inside the glyph outlines.
    [Fact]
    public void Mode4NoneText_establishes_no_clip_G10Baseline()
    {
        const string content = "1 0 0 rg 100 400 200 200 re f " +
                               "/Cs0 cs 1 scn BT /F1 48 Tf 4 Tr 110 480 Td (NONE) Tj ET " +
                               "0 0 1 rg 100 400 200 200 re f";
        byte[] pdf = ColourConformancePage.Build(
            $"[/Separation /None /DeviceRGB {ColourConformancePage.ExponentialTint("1 1 1", "0 0 0")}]",
            content, withFont: true);

        ColourConformancePage.ForEachPixelInRect(pdf, (x, y, c) =>
            Assert.True(c.Blue > 235 && c.Red < 20,
                $"G-10 baseline moved at ({x},{y}): RGB({c.Red},{c.Green},{c.Blue}) is not the " +
                "unclipped blue. If red now survives outside glyph shapes, the mode-4 clip is " +
                "IMPLEMENTED — retire this pin deliberately and update the matrix."));
    }

    // G-13 OBSERVATION (not a limitation pin — this is the missing fixture): a stencil mask
    // painted immediately after a bare `cs` (no scn) must take the colour space's INITIAL
    // colour, exactly as a fill would. Separation initial tint is 1.0; the tint ramps
    // white -> black, so the stencil paints black over the red backdrop. GREEN converts the
    // matrix's "reasoned about, only" into "observed". If this FAILS, STOP - that is a real
    // routing bug, not a baseline to record.
    [Fact]
    public void Stencil_after_bare_cs_takes_the_initial_tint_G13()
    {
        const string img = "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 " +
                           "/ImageMask true /BitsPerComponent 1 /Length 2 >>\r\nstream\r\n" +
                           "\u0000\u0000\r\nendstream";
        const string content = "1 0 0 rg 100 400 200 200 re f " +
                               "/Cs0 cs q 200 0 0 200 100 400 cm /Im0 Do Q";

        byte[] pdf = ColourConformancePage.Build(
            $"[/Separation /Spot /DeviceRGB {ColourConformancePage.ExponentialTint("1 1 1", "0 0 0")}]",
            content, withFont: false, extraResources: " /XObject << /Im0 5 0 R >>", img);

        SKColor c = ColourConformancePage.RenderCentre(pdf);
        Assert.True(c.Red < 25 && c.Green < 25 && c.Blue < 25,
            $"stencil after bare cs painted RGB({c.Red},{c.Green},{c.Blue}); expected the initial " +
            "tint 1.0 = black. Red means the stencil did not pick up the initial colour a fill gets.");
    }
}
