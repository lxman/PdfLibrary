using PdfLibrary.Document;
using PdfLibrary.Structure;
using PdfLibrary.Rendering.SkiaSharp;
using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Regression: a PatternType 2 (shading) pattern is a dictionary, not a stream — only tiling patterns
/// (type 1) carry a content stream. PdfResources.GetPattern returned `obj as PdfStream`, which is null
/// for a shading pattern, so every `/Pattern cs /Pn scn … f` gradient fill was silently skipped and the
/// region stayed unpainted. cairo (GTK/GNOME icons) fills with axial/radial shading patterns, so those
/// icons lost all their gradients.
/// </summary>
public class ShadingPatternFillTests
{
    // Fills rect [20..180] with an axial shading pattern, red (left) -> blue (right).
    private static byte[] BuildPdf()
    {
        const string pageContent = "/Pattern cs /P1 scn 20 20 160 160 re f";
        byte[] pc = System.Text.Encoding.Latin1.GetBytes(pageContent);

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
        Write("3 0 obj\r\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R " +
              "/Resources << /Pattern << /P1 5 0 R >> >> >>\r\nendobj\r\n");
        w.Flush(); off[4] = (int)ms.Position;
        Write($"4 0 obj\r\n<< /Length {pc.Length} >>\r\nstream\r\n");
        w.Flush(); ms.Write(pc, 0, pc.Length); Write("\r\nendstream\r\nendobj\r\n");
        w.Flush(); off[5] = (int)ms.Position;
        // Shading pattern — a DICTIONARY (not a stream).
        Write("5 0 obj\r\n<< /Type /Pattern /PatternType 2 /Matrix [1 0 0 1 0 0] /Shading 6 0 R >>\r\nendobj\r\n");
        w.Flush(); off[6] = (int)ms.Position;
        Write("6 0 obj\r\n<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [20 100 180 100] " +
              "/Domain [0 1] /Extend [true true] /Function 7 0 R >>\r\nendobj\r\n");
        w.Flush(); off[7] = (int)ms.Position;
        Write("7 0 obj\r\n<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>\r\nendobj\r\n");
        w.Flush(); long xref = ms.Position;
        Write("xref\r\n0 8\r\n0000000000 65535 f\r\n");
        for (var i = 1; i <= 7; i++) Write($"{off[i]:D10} 00000 n\r\n");
        Write("trailer\r\n<< /Size 8 /Root 1 0 R >>\r\nstartxref\r\n");
        Write($"{xref}\r\n%%EOF\r\n");
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void AxialShadingPattern_FillsRegion_NotLeftUnpainted()
    {
        using var ms = new MemoryStream(BuildPdf());
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bmp = SKBitmap.FromImage(image);

        // Near the left edge of the gradient the colour is red; near the right, blue. bitmap y = 200 - pdfY.
        SKColor left  = bmp.GetPixel(35, 200 - 100);
        SKColor right = bmp.GetPixel(165, 200 - 100);

        // Without the fix both stay white (255,255,255). With it: left is red-dominant, right blue-dominant.
        Assert.True(left.Red > 150 && left.Blue < 120,  $"left={left}");
        Assert.True(right.Blue > 150 && right.Red < 120, $"right={right}");
    }
}
