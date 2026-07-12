using PdfLibrary.Document;
using PdfLibrary.Structure;
using PdfLibrary.Rendering.SkiaSharp;
using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Regression: a Form XObject must inherit the full graphics state in effect at the Do operator —
/// including the current fill colour (ISO 32000-1 §8.10.1: Do saves the state, concatenates Matrix,
/// clips to BBox, paints, restores; it does NOT reset to a default state). matplotlib fills contour
/// regions by setting the colour on the page, then drawing the polygon inside a form that relies on
/// that inherited colour. Before this fix the form ran on a near-default graphics state, so every such
/// fill painted default black instead of the caller's colour.
/// </summary>
public class FormXObjectColorTests
{
    private static byte[] BuildPdf()
    {
        // Form /Fm fills a 200x200 rect with NO colour of its own — it must inherit the page's fill
        // colour. Page sets red (1 0 0 rg) before invoking the form.
        const string fm = "0 0 200 200 re f";
        byte[] fmBytes = System.Text.Encoding.Latin1.GetBytes(fm);
        const string pageContent = "q 1 0 0 rg 1 0 0 1 100 400 cm /Fm Do Q";
        byte[] pcBytes = System.Text.Encoding.Latin1.GetBytes(pageContent);

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
        Write("3 0 obj\r\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
              "/Resources << /XObject << /Fm 5 0 R >> >> >>\r\nendobj\r\n");
        w.Flush(); off[4] = (int)ms.Position;
        Write($"4 0 obj\r\n<< /Length {pcBytes.Length} >>\r\nstream\r\n");
        w.Flush(); ms.Write(pcBytes, 0, pcBytes.Length); Write("\r\nendstream\r\nendobj\r\n");
        w.Flush(); off[5] = (int)ms.Position;
        Write($"5 0 obj\r\n<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] /Length {fmBytes.Length} >>\r\nstream\r\n");
        w.Flush(); ms.Write(fmBytes, 0, fmBytes.Length); Write("\r\nendstream\r\nendobj\r\n");
        w.Flush(); long xref = ms.Position;
        Write("xref\r\n0 6\r\n0000000000 65535 f\r\n");
        for (var i = 1; i <= 5; i++) Write($"{off[i]:D10} 00000 n\r\n");
        Write("trailer\r\n<< /Size 6 /Root 1 0 R >>\r\nstartxref\r\n");
        Write($"{xref}\r\n%%EOF\r\n");
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void FormXObject_InheritsFillColor_RendersCallerColorNotBlack()
    {
        using var ms = new MemoryStream(BuildPdf());
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bmp = SKBitmap.FromImage(image);

        // Rect is page-space [100..300] x [400..600] → bitmap y = 792 - pdfY. Sample the centre.
        SKColor c = bmp.GetPixel(200, 792 - 500);
        // The form fills with the page's inherited red. Without inheritance it would paint default
        // black (~0,0,0); with no fill the pixel would stay white (~255,255,255). Assert red.
        Assert.InRange((int)c.Red, 200, 255);
        Assert.InRange((int)c.Green, 0, 60);
        Assert.InRange((int)c.Blue, 0, 60);
    }
}
