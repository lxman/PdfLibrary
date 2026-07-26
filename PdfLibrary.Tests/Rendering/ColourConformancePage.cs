using PdfLibrary.Document;
using PdfLibrary.Rendering.SkiaSharp;
using PdfLibrary.Structure;
using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// A hand-authored one-page PDF plus pixel readback, shared by the §8.6.6.4 / §8.6.6.5 colour
/// conformance tests.
///
/// <para>
/// These clauses are claims about what reaches the page ("shall not produce any visible output",
/// "shall apply the designated colourant", "shall perform subsequent painting operations in the
/// alternate colour space"), so they are asserted on rendered pixels. A resolver that returns a colour
/// nobody paints, and a resolver that returns nothing while the renderer paints black, are the same
/// thing as far as the clause is concerned — only the raster distinguishes them.
/// </para>
///
/// <para>
/// The PDF is written out by hand rather than through PdfDocumentBuilder because these tests need
/// exact control over the <c>/ColorSpace</c> resource — the builder has no vocabulary for a Separation
/// or DeviceN space, and the whole point is what the renderer does with one.
/// </para>
/// </summary>
internal static class ColourConformancePage
{
    /// <summary>Page box; the test rectangle at [100..300]×[400..600] sits well clear of every edge.</summary>
    private const int PageWidth = 612;
    private const int PageHeight = 792;

    /// <summary>
    /// A one-page PDF whose content is <paramref name="content"/>, with a single named colour space
    /// <c>/Cs0</c> given by the literal PDF syntax <paramref name="colorSpaceDef"/>. Any strings in
    /// <paramref name="extraObjects"/> become objects 5, 6, … so a colour space can reference e.g. a
    /// type 4 tint transform as <c>5 0 R</c>.
    /// </summary>
    /// <param name="extraResources">Literal PDF appended inside the page's /Resources dictionary, e.g.
    /// <c>" /XObject &lt;&lt; /Im0 5 0 R &gt;&gt;"</c>. Objects referenced here come from
    /// <paramref name="extraObjects"/>, which are numbered from 5.</param>
    public static byte[] Build(
        string colorSpaceDef, string content, bool withFont = false, string extraResources = "",
        params string[] extraObjects)
    {
        byte[] contentBytes = System.Text.Encoding.Latin1.GetBytes(content);
        string fontRes = withFont
            ? " /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>"
            : string.Empty;
        int objCount = 4 + extraObjects.Length;

        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, System.Text.Encoding.Latin1, leaveOpen: true) { NewLine = "\r\n" };
        void Write(string s) { w.Write(s); w.Flush(); }

        Write("%PDF-1.7\r\n");
        var off = new int[objCount + 1];
        w.Flush(); off[1] = (int)ms.Position;
        Write("1 0 obj\r\n<< /Type /Catalog /Pages 2 0 R >>\r\nendobj\r\n");
        w.Flush(); off[2] = (int)ms.Position;
        Write("2 0 obj\r\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\r\nendobj\r\n");
        w.Flush(); off[3] = (int)ms.Position;
        Write($"3 0 obj\r\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] " +
              $"/Contents 4 0 R /Resources << /ColorSpace << /Cs0 {colorSpaceDef} >>{fontRes}" +
              $"{extraResources} >> >>\r\nendobj\r\n");
        w.Flush(); off[4] = (int)ms.Position;
        Write($"4 0 obj\r\n<< /Length {contentBytes.Length} >>\r\nstream\r\n");
        w.Flush(); ms.Write(contentBytes, 0, contentBytes.Length); Write("\r\nendstream\r\nendobj\r\n");

        for (var i = 0; i < extraObjects.Length; i++)
        {
            w.Flush(); off[5 + i] = (int)ms.Position;
            Write($"{5 + i} 0 obj\r\n{extraObjects[i]}\r\nendobj\r\n");
        }

        w.Flush(); long xref = ms.Position;
        Write($"xref\r\n0 {objCount + 1}\r\n0000000000 65535 f\r\n");
        for (var i = 1; i <= objCount; i++) Write($"{off[i]:D10} 00000 n\r\n");
        Write($"trailer\r\n<< /Size {objCount + 1} /Root 1 0 R >>\r\nstartxref\r\n");
        Write($"{xref}\r\n%%EOF\r\n");
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Content stream filling [100..300]×[400..600] with <paramref name="operators"/> setting the colour.</summary>
    public static string FillRect(string operators) => $"{operators} 100 400 200 200 re f";

    /// <summary>A type 2 (exponential) tint transform ramping <paramref name="c0"/> → <paramref name="c1"/>.</summary>
    public static string ExponentialTint(string c0, string c1) =>
        $"<< /FunctionType 2 /Domain [0 1] /C0 [{c0}] /C1 [{c1}] /N 1 >>";

    /// <summary>Renders at 1:1 and returns the pixel at the centre of the test rectangle.</summary>
    public static SKColor RenderCentre(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bmp = SKBitmap.FromImage(image);
        // Rect spans page-space x 100..300, y 400..600; bitmap rows are flipped (PageHeight − pdfY).
        return bmp.GetPixel(200, PageHeight - 500);
    }

    /// <summary>
    /// Renders at 1:1 and invokes <paramref name="check"/> for every pixel well inside the test
    /// rectangle, inset by 5px so the rect's own edge antialiasing is not sampled.
    /// </summary>
    public static void ForEachPixelInRect(byte[] pdf, Action<int, int, SKColor> check)
    {
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bmp = SKBitmap.FromImage(image);

        for (var y = PageHeight - 595; y <= PageHeight - 405; y++)
            for (var x = 105; x <= 295; x++)
                check(x, y, bmp.GetPixel(x, y));
    }

    /// <summary>
    /// Renders <paramref name="pdf"/> and asserts that every pixel well inside the red rectangle is still
    /// red — i.e. the <c>/None</c> operator that followed marked nothing anywhere in the region, not just
    /// at one sampled point. Insets by 5px so path antialiasing at the rect's own edges is not counted.
    /// </summary>
    public static void AssertRedRectUntouched(byte[] pdf, string what) =>
        ForEachPixelInRect(pdf, (x, y, c) =>
            Assert.True(c.Red > 235 && c.Green < 20 && c.Blue < 20,
                $"{what} marked the page at ({x},{y}): RGB({c.Red},{c.Green},{c.Blue}) is not the " +
                "underlying red. §8.6.6.4 requires /None to have no effect on the current page"));
}
