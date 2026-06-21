using PdfLibrary.Document;
using PdfLibrary.Structure;
using PdfLibrary.Rendering.SkiaSharp;
using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Regression: a Form XObject must inherit the ExtGState transparency (/ca) in effect at the Do
/// operator (ISO 32000-1 §8.10). A black fill drawn inside a form under /ca 0.5 must render grey,
/// not full-opacity black — the bug that made watermark stamps paint solid black.
/// </summary>
public class FormXObjectAlphaTests
{
    private static byte[] BuildPdf()
    {
        // Form /Fm fills a 200x200 black rect at its origin. Page draws it under /G1 (/ca 0.5).
        const string fm = "0 0 0 rg 0 0 200 200 re f";
        byte[] fmBytes = System.Text.Encoding.Latin1.GetBytes(fm);
        const string pageContent = "q /G1 gs 1 0 0 1 100 400 cm /Fm Do Q";
        byte[] pcBytes = System.Text.Encoding.Latin1.GetBytes(pageContent);

        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, System.Text.Encoding.Latin1, leaveOpen: true) { NewLine = "\r\n" };
        void Write(string s) { w.Write(s); w.Flush(); }

        Write("%PDF-1.7\r\n");
        var off = new int[8];
        w.Flush(); off[1] = (int)ms.Position;
        Write("1 0 obj\r\n<< /Type /Catalog /Pages 2 0 R >>\r\nendobj\r\n");
        w.Flush(); off[2] = (int)ms.Position;
        Write("2 0 obj\r\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\r\nendobj\r\n");
        w.Flush(); off[3] = (int)ms.Position;
        Write("3 0 obj\r\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
              "/Resources << /ExtGState << /G1 6 0 R >> /XObject << /Fm 5 0 R >> >> >>\r\nendobj\r\n");
        w.Flush(); off[4] = (int)ms.Position;
        Write($"4 0 obj\r\n<< /Length {pcBytes.Length} >>\r\nstream\r\n");
        w.Flush(); ms.Write(pcBytes, 0, pcBytes.Length); Write("\r\nendstream\r\nendobj\r\n");
        w.Flush(); off[5] = (int)ms.Position;
        Write($"5 0 obj\r\n<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] /Length {fmBytes.Length} >>\r\nstream\r\n");
        w.Flush(); ms.Write(fmBytes, 0, fmBytes.Length); Write("\r\nendstream\r\nendobj\r\n");
        w.Flush(); off[6] = (int)ms.Position;
        Write("6 0 obj\r\n<< /Type /ExtGState /ca 0.5 /CA 0.5 >>\r\nendobj\r\n");
        w.Flush(); long xref = ms.Position;
        Write("xref\r\n0 7\r\n0000000000 65535 f\r\n");
        for (var i = 1; i <= 6; i++) Write($"{off[i]:D10} 00000 n\r\n");
        Write("trailer\r\n<< /Size 7 /Root 1 0 R >>\r\nstartxref\r\n");
        Write($"{xref}\r\n%%EOF\r\n");
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void FormXObject_InheritsFillAlpha_BlackRendersGrey()
    {
        using var ms = new MemoryStream(BuildPdf());
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bmp = SKBitmap.FromImage(image);

        // Rect is page-space [100..300] x [400..600] → bitmap y = 792 - pdfY. Sample the centre.
        SKColor c = bmp.GetPixel(200, 792 - 500);
        // The render target has a transparent background, so a black fill drawn under /ca 0.5 yields a
        // black pixel at HALF alpha (≈128). Without alpha inheritance the form would paint at full
        // opacity (alpha 255). Assert the fill is black AND the alpha is ~half — that IS the fix.
        Assert.True(c.Red < 40 && c.Green < 40 && c.Blue < 40, $"expected black fill, got {c}");
        Assert.InRange((int)c.Alpha, 90, 190);
    }
}
