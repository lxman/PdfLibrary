using PdfLibrary.Document;
using PdfLibrary.Rendering.SkiaSharp;
using PdfLibrary.Structure;
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
    /// <summary>
    /// A one-page PDF whose only content is <paramref name="content"/>, with a single named colour space
    /// <c>/Cs0</c> defined by the literal PDF syntax in <paramref name="colorSpaceDef"/>. The page is
    /// 612×792 so the rect below lands well away from every edge.
    /// </summary>
    private static byte[] BuildPdf(string colorSpaceDef, string content) =>
        BuildPdf(colorSpaceDef, content, withFont: false);

    private static byte[] BuildPdf(string colorSpaceDef, string content, bool withFont)
    {
        string fontRes = withFont
            ? " /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>"
            : string.Empty;
        byte[] contentBytes = System.Text.Encoding.Latin1.GetBytes(content);

        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, System.Text.Encoding.Latin1, leaveOpen: true) { NewLine = "\r\n" };
        void Write(string s) { w.Write(s); w.Flush(); }

        Write("%PDF-1.7\r\n");
        var off = new int[5];
        w.Flush(); off[1] = (int)ms.Position;
        Write("1 0 obj\r\n<< /Type /Catalog /Pages 2 0 R >>\r\nendobj\r\n");
        w.Flush(); off[2] = (int)ms.Position;
        Write("2 0 obj\r\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\r\nendobj\r\n");
        w.Flush(); off[3] = (int)ms.Position;
        Write("3 0 obj\r\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
              $"/Resources << /ColorSpace << /Cs0 {colorSpaceDef} >>{fontRes} >> >>\r\nendobj\r\n");
        w.Flush(); off[4] = (int)ms.Position;
        Write($"4 0 obj\r\n<< /Length {contentBytes.Length} >>\r\nstream\r\n");
        w.Flush(); ms.Write(contentBytes, 0, contentBytes.Length); Write("\r\nendstream\r\nendobj\r\n");
        w.Flush(); long xref = ms.Position;
        Write("xref\r\n0 5\r\n0000000000 65535 f\r\n");
        for (var i = 1; i <= 4; i++) Write($"{off[i]:D10} 00000 n\r\n");
        Write("trailer\r\n<< /Size 5 /Root 1 0 R >>\r\nstartxref\r\n");
        Write($"{xref}\r\n%%EOF\r\n");
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Fills [100..300]×[400..600] in page space with tint <paramref name="tint"/> of /Cs0.</summary>
    private static string FillRect(double tint) =>
        $"/Cs0 cs {tint.ToString(System.Globalization.CultureInfo.InvariantCulture)} scn 100 400 200 200 re f";

    /// <summary>A type 2 tint transform ramping white → <paramref name="c1"/> in DeviceRGB.</summary>
    private static string RgbTint(string c1) =>
        $"<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [{c1}] /N 1 >>";

    /// <summary>Renders the page and returns the pixel at the centre of the filled rect.</summary>
    private static SKColor RenderCentre(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bmp = SKBitmap.FromImage(image);
        // Rect spans page-space x 100..300, y 400..600; bitmap y is flipped (792 − pdfY).
        return bmp.GetPixel(200, 792 - 500);
    }

    /// <summary>
    /// Renders <paramref name="pdf"/> and asserts that every pixel well inside the red rectangle is still
    /// red — i.e. the <c>/None</c> operator that followed marked nothing anywhere in the region, not just
    /// at one sampled point. Insets by 5px so path antialiasing at the rect's own edges is not counted.
    /// </summary>
    private static void AssertRedRectUntouched(byte[] pdf, string what)
    {
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bmp = SKBitmap.FromImage(image);

        for (var y = 792 - 595; y <= 792 - 405; y++)
        {
            for (var x = 105; x <= 295; x++)
            {
                SKColor c = bmp.GetPixel(x, y);
                Assert.True(c.Red > 235 && c.Green < 20 && c.Blue < 20,
                    $"{what} marked the page at ({x},{y}): RGB({c.Red},{c.Green},{c.Blue}) is not the " +
                    "underlying red. §8.6.6.4 requires /None to have no effect on the current page");
            }
        }
    }

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
        byte[] pdf = BuildPdf($"[/Separation /None /DeviceRGB {RgbTint("0 0 0")}]", content);

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
        byte[] pdf = BuildPdf($"[/Separation /None /DeviceRGB {RgbTint("0 0 0")}]", content);

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
        byte[] pdf = BuildPdf($"[/Separation /None /DeviceRGB {RgbTint("0 0 0")}]", content, withFont: true);

        AssertRedRectUntouched(pdf, "/None text");
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
        byte[] pdf = BuildPdf($"[/Separation /All /DeviceRGB {RgbTint("1 0 0")}]", FillRect(1.0));

        SKColor c = RenderCentre(pdf);

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
        byte[] pdf = BuildPdf($"[/Separation /All /DeviceRGB {RgbTint("1 0 0")}]", FillRect(0.0));

        SKColor c = RenderCentre(pdf);

        Assert.True(c.Red > 235 && c.Green > 235 && c.Blue > 235,
            $"/All at tint 0 painted RGB({c.Red},{c.Green},{c.Blue}); the complement of 0 is full " +
            "intensity on every additive colourant, i.e. white");
    }
}
