using System.Numerics;
using PdfLibrary.Builder;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Document;

public class PageGeometryTests
{
    // A simple 1-page letter PDF (no crop, no rotation) via the builder.
    private static PdfDocument OneLetterPage()
    {
        byte[] pdf = PdfDocumentBuilder.Create().AddPage(_ => { }).ToByteArray();
        return PdfDocument.Load(new MemoryStream(pdf));
    }

    [Fact]
    public void PdfToImage_RotationZero_MapsBottomLeftToImageBottomLeft()
    {
        using PdfDocument doc = OneLetterPage();
        PdfPage page = doc.GetPage(0)!;
        double h = page.GetCropBox().Height;
        PageGeometry g = page.GetGeometry(2.0);

        // PDF origin (0,0) is the page bottom-left → image bottom (y = h*scale), x = 0.
        Vector2 origin = Vector2.Transform(Vector2.Zero, g.PdfToImage);
        Assert.Equal(0, origin.X, 3);
        Assert.Equal(h * 2.0, origin.Y, 3);

        // PixelWidth/Height reflect cropbox * scale.
        Assert.Equal((int)Math.Round(page.GetCropBox().Width * 2.0), g.PixelWidth);
        Assert.Equal((int)Math.Round(page.GetCropBox().Height * 2.0), g.PixelHeight);
    }

    [Fact]
    public void ImageToPdf_IsInverseOfPdfToImage()
    {
        using PdfDocument doc = OneLetterPage();
        PageGeometry g = doc.GetPage(0)!.GetGeometry(1.5);
        var p = new Vector2(123.4f, 567.8f);
        Vector2 roundTrip = Vector2.Transform(Vector2.Transform(p, g.PdfToImage), g.ImageToPdf);
        Assert.Equal(p.X, roundTrip.X, 2);
        Assert.Equal(p.Y, roundTrip.Y, 2);
    }

    [Fact]
    public void MapRectToImage_FlipsYAndScales()
    {
        using PdfDocument doc = OneLetterPage();
        PdfPage page = doc.GetPage(0)!;
        double h = page.GetCropBox().Height;
        PageGeometry g = page.GetGeometry(1.0);

        // A 100x20 rect near the PDF bottom maps to a rect near the image top-left area.
        ImageRect img = g.MapRectToImage(new PdfRect(50, 10, 150, 30));
        Assert.Equal(50, img.X, 3);
        Assert.Equal(100, img.Width, 3);
        // PDF y in [10,30] → image y (top-left) = h-30, height = 20.
        Assert.Equal(h - 30, img.Y, 3);
        Assert.Equal(20, img.Height, 3);
    }
}
