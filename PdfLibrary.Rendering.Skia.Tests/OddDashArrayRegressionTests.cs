using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;
using PdfLibrary.Document;
using PdfLibrary.Structure;
using SkiaSharp;
using Xunit;

namespace PdfLibrary.Rendering.Skia.Tests;

public class OddDashArrayRegressionTests
{
    // A PDF dash array may legally have an ODD number of entries — it is applied cyclically
    // (e.g. [3] means 3 on, 3 off, 3 on, ...). SkiaSharp's SKPathEffect.CreateDash requires an
    // EVEN-length array and throws ArgumentException otherwise. The Skia renderer used to pass the
    // array straight through, so real-world files with odd dash arrays (the automotive "SCV"
    // schematic family) crashed with "The intervals must have an even number of entries."
    // Rendering such a page must NOT throw, and the dashed stroke must still be drawn.
    [Fact]
    public void Odd_length_dash_array_renders_without_throwing()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PdfPageSize.Letter, page =>
        {
            page.AddPath()
                .MoveTo(50, 400)
                .LineTo(550, 400)
                .DashPattern([3])            // odd-length array: the crash trigger
                .Stroke(PdfColor.Black, 4);
        });

        using var ms = new MemoryStream();
        builder.Save(ms);
        ms.Position = 0;

        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage pg = doc.GetPage(0)!;

        using SKImage img = pg.RenderToImage(scale: 2.0);   // must not throw
        using SKBitmap bmp = SKBitmap.FromImage(img);

        // The dashed line must actually draw: expect near-black pixels somewhere on the page.
        int dark = 0;
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            SKColor p = bmp.GetPixel(x, y);
            if (p is { Red: < 80, Green: < 80, Blue: < 80 }) dark++;
        }

        Assert.True(dark > 50, $"expected the dashed stroke to draw dark pixels; got {dark}");
    }
}
