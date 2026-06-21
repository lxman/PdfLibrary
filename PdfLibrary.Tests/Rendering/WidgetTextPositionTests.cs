using PdfLibrary.Document;
using PdfLibrary.Structure;
using PdfLibrary.Rendering.SkiaSharp;
using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Diagnostic: does a Widget annotation whose /AP /N SHOWS TEXT render the text positioned at the
/// widget /Rect, or dumped at the page origin (bottom-left)? A fill-rect appearance is known to
/// position correctly (WidgetRenderTests); text uses a different transform path in the renderer, so
/// this isolates whether text-in-appearance picks up the BBox→Rect matrix.
///
/// Widget rect [200 346 400 446] (mid-page). AP BBox [0 0 200 100], Tm at form-space (6,44) shows
/// "HELLO" in blue. Correct ⇒ blue pixels appear in the rect region (bitmap y≈348..444).
/// Bug ⇒ blue pixels appear near the page bottom (bitmap y≈740..) because only the AP's own Tm was
/// applied without the rect translation.
/// </summary>
public class WidgetTextPositionTests
{
    private const double PageW = 612, PageH = 792;
    private const int Llx = 200, Lly = 346, Urx = 400, Ury = 446;
    private const int BBoxW = Urx - Llx; // 200
    private const int BBoxH = Ury - Lly; // 100

    private static byte[] BuildTextWidgetPdf()
    {
        // Text shown at form-space (6,44) within BBox [0 0 200 100]; blue fill so it's easy to detect.
        const string ap = "/Tx BMC q BT /Helv 12 Tf 0 0 1 rg 1 0 0 1 6 44 Tm (HELLO) Tj ET Q EMC";
        byte[] apBytes = System.Text.Encoding.Latin1.GetBytes(ap);

        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, System.Text.Encoding.Latin1, leaveOpen: true) { NewLine = "\r\n" };
        void Write(string s) { w.Write(s); w.Flush(); }

        Write("%PDF-1.7\r\n");
        var off = new int[7];

        w.Flush(); off[1] = (int)ms.Position;
        Write("1 0 obj\r\n<< /Type /Catalog /Pages 2 0 R >>\r\nendobj\r\n");

        w.Flush(); off[2] = (int)ms.Position;
        Write("2 0 obj\r\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\r\nendobj\r\n");

        w.Flush(); off[3] = (int)ms.Position;
        Write($"3 0 obj\r\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {(int)PageW} {(int)PageH}] /Annots [4 0 R] >>\r\nendobj\r\n");

        w.Flush(); off[4] = (int)ms.Position;
        Write($"4 0 obj\r\n<< /Type /Annot /Subtype /Widget /Rect [{Llx} {Lly} {Urx} {Ury}] /AP << /N 5 0 R >> >>\r\nendobj\r\n");

        w.Flush(); off[5] = (int)ms.Position;
        Write($"5 0 obj\r\n<< /Type /XObject /Subtype /Form /BBox [0 0 {BBoxW} {BBoxH}] /Resources << /Font << /Helv 6 0 R >> >> /Length {apBytes.Length} >>\r\nstream\r\n");
        w.Flush(); ms.Write(apBytes, 0, apBytes.Length);
        Write("\r\nendstream\r\nendobj\r\n");

        w.Flush(); off[6] = (int)ms.Position;
        Write("6 0 obj\r\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\r\nendobj\r\n");

        w.Flush(); long xref = ms.Position;
        Write("xref\r\n0 7\r\n0000000000 65535 f\r\n");
        for (var i = 1; i <= 6; i++) Write($"{off[i]:D10} 00000 n\r\n");
        Write("trailer\r\n<< /Size 7 /Root 1 0 R >>\r\nstartxref\r\n");
        Write($"{xref}\r\n%%EOF\r\n");
        w.Flush();
        return ms.ToArray();
    }

    private static int NonWhiteOpaque(SKBitmap b, int x0, int y0, int x1, int y1)
    {
        int n = 0;
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
        x1 = Math.Min(b.Width, x1); y1 = Math.Min(b.Height, y1);
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            SKColor c = b.GetPixel(x, y);
            if (c.Alpha > 0 && (c.Red != 255 || c.Green != 255 || c.Blue != 255)) n++;
        }
        return n;
    }

    // Two text widgets at different rects. Regression guard for the CTM-accumulation bug: without
    // restoring the renderer CurrentState.Ctm per annotation, the SECOND widget inherits the first's
    // translate and drifts off its rect. Both must render in their own rect.
    private static byte[] BuildTwoTextWidgetPdf()
    {
        // Widget A rect [200 600 400 640]; Widget B rect [200 300 400 340].
        const string apA = "/Tx BMC q BT /Helv 12 Tf 0 0 1 rg 1 0 0 1 6 14 Tm (AAAAA) Tj ET Q EMC";
        const string apB = "/Tx BMC q BT /Helv 12 Tf 0 0 1 rg 1 0 0 1 6 14 Tm (BBBBB) Tj ET Q EMC";
        byte[] apABytes = System.Text.Encoding.Latin1.GetBytes(apA);
        byte[] apBBytes = System.Text.Encoding.Latin1.GetBytes(apB);

        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, System.Text.Encoding.Latin1, leaveOpen: true) { NewLine = "\r\n" };
        void Write(string s) { w.Write(s); w.Flush(); }

        Write("%PDF-1.7\r\n");
        var off = new int[9];
        w.Flush(); off[1] = (int)ms.Position;
        Write("1 0 obj\r\n<< /Type /Catalog /Pages 2 0 R >>\r\nendobj\r\n");
        w.Flush(); off[2] = (int)ms.Position;
        Write("2 0 obj\r\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\r\nendobj\r\n");
        w.Flush(); off[3] = (int)ms.Position;
        Write($"3 0 obj\r\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {(int)PageW} {(int)PageH}] /Annots [4 0 R 5 0 R] >>\r\nendobj\r\n");
        w.Flush(); off[4] = (int)ms.Position;
        Write("4 0 obj\r\n<< /Type /Annot /Subtype /Widget /Rect [200 600 400 640] /AP << /N 6 0 R >> >>\r\nendobj\r\n");
        w.Flush(); off[5] = (int)ms.Position;
        Write("5 0 obj\r\n<< /Type /Annot /Subtype /Widget /Rect [200 300 400 340] /AP << /N 7 0 R >> >>\r\nendobj\r\n");
        w.Flush(); off[6] = (int)ms.Position;
        Write($"6 0 obj\r\n<< /Type /XObject /Subtype /Form /BBox [0 0 200 40] /Resources << /Font << /Helv 8 0 R >> >> /Length {apABytes.Length} >>\r\nstream\r\n");
        w.Flush(); ms.Write(apABytes, 0, apABytes.Length); Write("\r\nendstream\r\nendobj\r\n");
        w.Flush(); off[7] = (int)ms.Position;
        Write($"7 0 obj\r\n<< /Type /XObject /Subtype /Form /BBox [0 0 200 40] /Resources << /Font << /Helv 8 0 R >> >> /Length {apBBytes.Length} >>\r\nstream\r\n");
        w.Flush(); ms.Write(apBBytes, 0, apBBytes.Length); Write("\r\nendstream\r\nendobj\r\n");
        w.Flush(); off[8] = (int)ms.Position;
        Write("8 0 obj\r\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\r\nendobj\r\n");
        w.Flush(); long xref = ms.Position;
        Write("xref\r\n0 9\r\n0000000000 65535 f\r\n");
        for (var i = 1; i <= 8; i++) Write($"{off[i]:D10} 00000 n\r\n");
        Write("trailer\r\n<< /Size 9 /Root 1 0 R >>\r\nstartxref\r\n");
        Write($"{xref}\r\n%%EOF\r\n");
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void TwoWidgets_BothRenderInTheirOwnRect_NoCtmAccumulation()
    {
        using var ms = new MemoryStream(BuildTwoTextWidgetPdf());
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bmp = SKBitmap.FromImage(image);

        // Widget A rect [200 600 400 640] → bitmap y [152,192]; Widget B rect [200 300 400 340] → y [452,492].
        int inA = NonWhiteOpaque(bmp, 200, (int)(PageH - 640), 400, (int)(PageH - 600));
        int inB = NonWhiteOpaque(bmp, 200, (int)(PageH - 340), 400, (int)(PageH - 300));
        Assert.True(inA > 0, $"Widget A text missing from its rect (count={inA}).");
        Assert.True(inB > 0, $"Widget B text missing from its rect (count={inB}) — CTM accumulation regression.");
    }

    [Fact]
    public void WidgetText_RendersInsideRect_NotAtPageBottom()
    {
        byte[] pdf = BuildTextWidgetPdf();
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;

        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bmp = SKBitmap.FromImage(image);

        // Rect region in bitmap coords: bitmapY = pageH - pdfY
        int rectTop = (int)(PageH - Ury);    // 346
        int rectBot = (int)(PageH - Lly);    // 446
        int inRect = NonWhiteOpaque(bmp, Llx, rectTop, Urx, rectBot);

        // Bottom strip (where a missing rect-translation would dump the text: pdf y≈44 → bitmap y≈748)
        int bottomStrip = NonWhiteOpaque(bmp, 0, (int)PageH - 60, (int)PageW, (int)PageH);

        // Diagnostic output regardless of pass/fail
        Assert.True(inRect > 0,
            $"DIAGNOSIS: widget text did NOT render in the rect region (count={inRect}); " +
            $"bottom-strip count={bottomStrip}. If bottom-strip>0 and inRect==0, the rect-translation " +
            $"is not applied to text appearances (real renderer bug). Image {bmp.Width}x{bmp.Height}.");
    }
}
