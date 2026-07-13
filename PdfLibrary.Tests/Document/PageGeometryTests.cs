using System.Numerics;
using PdfLibrary.Builder;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
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

    [Fact]
    public void GetGeometry_CropBoxExceedingMediaBox_ReducedToMediaBoxIntersection()
    {
        // ISO 32000-1 §14.11.2: a CropBox extending beyond the MediaBox is effectively reduced to
        // their intersection. This page (modeled on GWG180-184) declares an out-of-spec CropBox that
        // is larger than the MediaBox on every side; the rendered geometry must use the intersection
        // (here the MediaBox), not the raw oversized crop.
        var page = new PdfPage(new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Page"),
            [new PdfName("MediaBox")] = new PdfArray(new PdfReal(0), new PdfReal(0), new PdfReal(595.276), new PdfReal(792)),
            [new PdfName("CropBox")] = new PdfArray(new PdfReal(-8.50394), new PdfReal(-8.50394), new PdfReal(603.779), new PdfReal(800.504)),
        });

        PageGeometry g = page.GetGeometry(2.0);

        Assert.Equal((int)Math.Round(595.276 * 2.0), g.PixelWidth);   // MediaBox width (1191), not raw crop (1225)
        Assert.Equal((int)Math.Round(792.0 * 2.0), g.PixelHeight);    // MediaBox height (1584), not raw crop (1618)
    }

    // Minimal single-page PDF with explicit MediaBox/CropBox, loaded through the real parser so the
    // page has a document reference (the render path requires one).
    private static byte[] MinimalPdf(string mediaBox, string cropBox)
    {
        const string content = "1 0 0 rg 32 32 100 100 re f\n";
        string[] bodies =
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox {mediaBox} /CropBox {cropBox} /Contents 4 0 R /Resources << >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}endstream",
        };
        var bytes = new List<byte>();
        void Add(string s) => bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(s));
        Add("%PDF-1.4\n");
        var offsets = new int[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            offsets[i] = bytes.Count;
            Add($"{i + 1} 0 obj\n{bodies[i]}\nendobj\n");
        }
        int xref = bytes.Count, n = bodies.Length + 1;
        Add($"xref\n0 {n}\n0000000000 65535 f \n");
        foreach (int off in offsets) Add($"{off:D10} 00000 n \n");
        Add($"trailer\n<< /Size {n} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return bytes.ToArray();
    }

    [Fact]
    public void Record_CropBoxExceedingMediaBox_RenderSizeUsesMediaBoxIntersection()
    {
        // Same defect on the actual render path RenderToImage uses: the recorded page size must be
        // the CropBox∩MediaBox (595.276 x 792), not the raw oversized CropBox (612.283 x 809.008).
        byte[] pdf = MinimalPdf("[0 0 595.276 792]", "[-8.50394 -8.50394 603.779 800.504]");
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));

        PageDrawList list = RecordingRenderTarget.Record(doc.GetPage(0)!, 2.0);

        Assert.Equal(595.276, list.Begin.Width, 3);
        Assert.Equal(792.0, list.Begin.Height, 3);
    }
}
