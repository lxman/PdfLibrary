using PdfLibrary.Document;
using PdfLibrary.Rendering.SkiaSharp;
using PdfLibrary.Structure;
using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Tests that widget annotation appearances are drawn by PdfRenderer instead of skipped.
///
/// The fixture hand-builds a minimal raw PDF (no builder API dependency) that contains
/// a single Widget annotation whose /AP /N stream paints a solid red rectangle covering
/// the annotation /Rect. The test renders the page and asserts that pixels inside the
/// widget's area are non-white (red fill present).
/// </summary>
public class WidgetRenderTests
{
    // Page dimensions: Letter (612 x 792 pts).
    // Widget rect: centre of page, 200x100 pts.
    private const double PageWidth = 612;
    private const double PageHeight = 792;
    private const double WidgetLlx = 200;
    private const double WidgetLly = 346;  // (792-100)/2
    private const double WidgetUrx = 400;
    private const double WidgetUry = 446;
    private const double WidgetW = WidgetUrx - WidgetLlx;  // 200
    private const double WidgetH = WidgetUry - WidgetLly;  // 100

    /// <summary>
    /// Hand-build a minimal PDF with one Widget annotation whose appearance stream
    /// fills the annotation rect in solid red (RGB 1 0 0).
    ///
    /// Object layout:
    ///   1 0  catalog
    ///   2 0  pages node
    ///   3 0  page
    ///   4 0  widget annotation dict
    ///   5 0  appearance stream (/AP /N)
    /// </summary>
    private static byte[] BuildWidgetPdf()
    {
        // Appearance stream content: q 1 0 0 rg 0 0 200 100 re f Q
        string apContent = $"q 1 0 0 rg 0 0 {(int)WidgetW} {(int)WidgetH} re f Q";
        byte[] apBytes = System.Text.Encoding.Latin1.GetBytes(apContent);
        int apLength = apBytes.Length;

        var sb = new System.Text.StringBuilder();

        // Header
        sb.Append("%PDF-1.7\r\n");

        // Offsets list (populated as we write)
        var offsets = new Dictionary<int, int>();
        var body = new System.Text.StringBuilder();

        // Helper to record offset and add object
        void AddObject(int num, string content)
        {
            offsets[num] = sb.Length + body.Length;
            body.Append(content);
        }

        // 1 0 obj  Catalog
        AddObject(1,
            "1 0 obj\r\n" +
            "<< /Type /Catalog /Pages 2 0 R >>\r\n" +
            "endobj\r\n");

        // 2 0 obj  Pages
        AddObject(2,
            "2 0 obj\r\n" +
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>\r\n" +
            "endobj\r\n");

        // 3 0 obj  Page  (with /Annots referencing widget)
        AddObject(3,
            "3 0 obj\r\n" +
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {(int)PageWidth} {(int)PageHeight}] /Annots [4 0 R] >>\r\n" +
            "endobj\r\n");

        // 4 0 obj  Widget annotation dict
        AddObject(4,
            "4 0 obj\r\n" +
            $"<< /Type /Annot /Subtype /Widget /Rect [{(int)WidgetLlx} {(int)WidgetLly} {(int)WidgetUrx} {(int)WidgetUry}] /AP << /N 5 0 R >> >>\r\n" +
            "endobj\r\n");

        // 5 0 obj  Appearance stream (Form XObject)
        AddObject(5,
            "5 0 obj\r\n" +
            $"<< /Type /XObject /Subtype /Form /BBox [0 0 {(int)WidgetW} {(int)WidgetH}] /Length {apLength} >>\r\n" +
            "stream\r\n");

        sb.Append(body);

        // Append the appearance stream binary content
        string preamble = sb.ToString();
        var resultBytes = new System.Collections.Generic.List<byte>(
            System.Text.Encoding.Latin1.GetBytes(preamble));
        resultBytes.AddRange(apBytes);

        // After stream content, finish off the object and build xref
        string streamEnd = "\r\nendstream\r\nendobj\r\n";
        resultBytes.AddRange(System.Text.Encoding.Latin1.GetBytes(streamEnd));

        // Recalculate offsets by actually tracking positions in the byte stream
        // (the StringBuilder approach above is approximate; use a fresh pass)
        return BuildWidgetPdfPrecise();
    }

    /// <summary>
    /// Builds the PDF by writing bytes directly so offsets are exact.
    /// </summary>
    private static byte[] BuildWidgetPdfPrecise()
    {
        string apContent = $"q 1 0 0 rg 0 0 {(int)WidgetW} {(int)WidgetH} re f Q";
        byte[] apBytes = System.Text.Encoding.Latin1.GetBytes(apContent);
        int apLength = apBytes.Length;

        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, System.Text.Encoding.Latin1, leaveOpen: true);
        w.NewLine = "\r\n";

        void Write(string s) { w.Write(s); w.Flush(); }

        // Header
        Write("%PDF-1.7\r\n");

        var offsets = new int[6]; // 1-indexed

        // 1 0 obj  Catalog
        w.Flush();
        offsets[1] = (int)ms.Position;
        Write("1 0 obj\r\n<< /Type /Catalog /Pages 2 0 R >>\r\nendobj\r\n");

        // 2 0 obj  Pages
        w.Flush();
        offsets[2] = (int)ms.Position;
        Write("2 0 obj\r\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\r\nendobj\r\n");

        // 3 0 obj  Page
        w.Flush();
        offsets[3] = (int)ms.Position;
        Write($"3 0 obj\r\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {(int)PageWidth} {(int)PageHeight}] /Annots [4 0 R] >>\r\nendobj\r\n");

        // 4 0 obj  Widget annotation
        w.Flush();
        offsets[4] = (int)ms.Position;
        Write($"4 0 obj\r\n<< /Type /Annot /Subtype /Widget /Rect [{(int)WidgetLlx} {(int)WidgetLly} {(int)WidgetUrx} {(int)WidgetUry}] /AP << /N 5 0 R >> >>\r\nendobj\r\n");

        // 5 0 obj  Appearance stream
        w.Flush();
        offsets[5] = (int)ms.Position;
        Write($"5 0 obj\r\n<< /Type /XObject /Subtype /Form /BBox [0 0 {(int)WidgetW} {(int)WidgetH}] /Length {apLength} >>\r\nstream\r\n");
        w.Flush();
        ms.Write(apBytes, 0, apBytes.Length);
        Write("\r\nendstream\r\nendobj\r\n");

        // xref table
        w.Flush();
        long xrefOffset = ms.Position;
        Write("xref\r\n");
        Write($"0 6\r\n");
        Write("0000000000 65535 f\r\n");
        for (int i = 1; i <= 5; i++)
            Write($"{offsets[i]:D10} 00000 n\r\n");

        // trailer
        Write("trailer\r\n");
        Write("<< /Size 6 /Root 1 0 R >>\r\n");
        Write("startxref\r\n");
        Write($"{xrefOffset}\r\n");
        Write("%%EOF\r\n");

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Count pixels in the given region of an SKBitmap that are opaque and non-white.
    /// Transparent pixels (alpha == 0) are excluded — they are background, not drawn content.
    /// Coordinates are in bitmap pixels (top-left origin).
    /// </summary>
    private static int CountOpaqueNonWhitePixelsInRegion(SKBitmap bitmap, int x0, int y0, int x1, int y1)
    {
        int count = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                SKColor c = bitmap.GetPixel(x, y);
                // Only count pixels that are actually drawn (alpha > 0) and not white
                if (c.Alpha > 0 && (c.Red != 255 || c.Green != 255 || c.Blue != 255))
                    count++;
            }
        }
        return count;
    }

    [Fact]
    public void WidgetAnnotation_WithAppearanceStream_PixelsInsideRectAreNonWhite()
    {
        // Arrange — build a PDF whose only visible content is a red-filled Widget
        byte[] pdfBytes = BuildWidgetPdfPrecise();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;

        // Render at 1× (1 pt = 1 px at 72 DPI)
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        Assert.NotNull(image);
        using SKBitmap bitmap = SKBitmap.FromImage(image);

        // Widget rect in PDF coords (origin bottom-left):
        //   llx=200 lly=346 urx=400 ury=446
        // PDF Y → bitmap Y: bitmapY = pageHeight - pdfY
        //   bitmap top of widget    = pageHeight - ury = 792 - 446 = 346
        //   bitmap bottom of widget = pageHeight - lly = 792 - 346 = 446
        int bx0 = (int)WidgetLlx + 2;        // small inset to avoid edge AA
        int bx1 = (int)WidgetUrx - 2;
        int by0 = (int)(PageHeight - WidgetUry) + 2;
        int by1 = (int)(PageHeight - WidgetLly) - 2;

        // The appearance stream paints solid red (1 0 0 rg), so drawn pixels will be opaque and non-white.
        // Transparent pixels (alpha=0) are unrendered background — we exclude them.
        int drawnNonWhite = CountOpaqueNonWhitePixelsInRegion(bitmap, bx0, by0, bx1, by1);

        Assert.True(drawnNonWhite > 0,
            $"Widget appearance was not rendered: no opaque non-white pixels found in the widget region. " +
            $"Widget rect (bitmap coords): x=[{bx0},{bx1}) y=[{by0},{by1}). " +
            $"Image size: {bitmap.Width}×{bitmap.Height}. " +
            $"Expected red fill from appearance stream 'q 1 0 0 rg 0 0 200 100 re f Q'.");
    }
}
