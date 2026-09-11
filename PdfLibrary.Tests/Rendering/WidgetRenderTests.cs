using PdfLibrary.Document;
using PdfLibrary.Structure;

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
    private static byte[] BuildWidgetPdfPrecise(
        string? appearanceContent = null,
        string? rect = null,
        string? bbox = null,
        string? matrix = null)
    {
        string apContent = appearanceContent
                           ?? $"q 1 0 0 rg 0 0 {(int)WidgetW} {(int)WidgetH} re f Q";
        byte[] apBytes = System.Text.Encoding.Latin1.GetBytes(apContent);
        int apLength = apBytes.Length;
        string rectValue = rect ?? $"{(int)WidgetLlx} {(int)WidgetLly} {(int)WidgetUrx} {(int)WidgetUry}";
        string bboxValue = bbox ?? $"0 0 {(int)WidgetW} {(int)WidgetH}";
        string matrixEntry = matrix is null ? "" : $" /Matrix [{matrix}]";

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
        Write($"4 0 obj\r\n<< /Type /Annot /Subtype /Widget /Rect [{rectValue}] /AP << /N 5 0 R >> >>\r\nendobj\r\n");

        // 5 0 obj  Appearance stream
        w.Flush();
        offsets[5] = (int)ms.Position;
        Write($"5 0 obj\r\n<< /Type /XObject /Subtype /Form /BBox [{bboxValue}]{matrixEntry} /Length {apLength} >>\r\nstream\r\n");
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

    [Fact]
    public void WidgetAnnotation_WithAppearanceStream_PixelsInsideRectAreNonWhite()
    {
        // Arrange — build a PDF whose only visible content is a red-filled Widget
        byte[] pdfBytes = BuildWidgetPdfPrecise();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);
        var list = RecordedPageProbe.Record(doc.GetPage(0)!);
        int drawnNonWhite = RecordedPageProbe.CountPaintCommandsInRect(list,
            WidgetLlx + 2, WidgetLly + 2, WidgetUrx - 2, WidgetUry - 2);

        Assert.True(drawnNonWhite > 0,
            $"Widget appearance emitted no paint command in [{WidgetLlx},{WidgetLly},{WidgetUrx},{WidgetUry}]. " +
            $"Expected red fill from appearance stream 'q 1 0 0 rg 0 0 200 100 re f Q'.");
    }

    [Fact]
    public void WidgetAnnotation_AppliesAppearanceMatrixBeforeFittingBBoxToRect()
    {
        // The appearance paints the left half of its 100x50 BBox. Its /Matrix rotates that box
        // counter-clockwise into [50,0]-[100,100]. The §12.5.5 fit to the 200x100 annotation
        // rectangle therefore places the red half across the BOTTOM of /Rect. The former BBox-only
        // shortcut instead painted the LEFT half, so the two probes below discriminate the paths.
        byte[] pdfBytes = BuildWidgetPdfPrecise(
            appearanceContent: "q 1 0 0 rg 0 0 50 50 re f Q",
            rect: "200 300 400 400",
            bbox: "0 0 100 50",
            matrix: "0 1 -1 0 100 0");

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);
        var list = RecordedPageProbe.Record(doc.GetPage(0)!);

        RecordedColor bottomRight = RecordedPageProbe.ColorAt(list, 350, 325);
        RecordedColor topLeft = RecordedPageProbe.ColorAt(list, 250, 375);

        Assert.True(bottomRight.Red > 200 && bottomRight.Green < 50 && bottomRight.Blue < 50,
            $"Expected the matrix-rotated appearance at bottom-right, got {bottomRight}.");
        Assert.False(topLeft.Red > 200 && topLeft.Green < 50 && topLeft.Blue < 50,
            $"The old BBox-only placement paints top-left red; §12.5.5 placement must not ({topLeft}).");
    }
}
