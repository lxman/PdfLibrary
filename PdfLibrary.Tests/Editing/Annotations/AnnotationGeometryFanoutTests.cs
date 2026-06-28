using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Rendering.SkiaSharp;
using PdfLibrary.Structure;
using SkiaSharp;

namespace PdfLibrary.Tests.Editing.Annotations;

/// <summary>
/// Fan-out of the annotation slice to the geometry markup types: Circle, Line, Ink.
/// Each must round-trip its per-type data, carry an /AP /N stream, and render.
/// </summary>
public class AnnotationGeometryFanoutTests
{
    private static byte[] BlankPage() =>
        PdfDocumentBuilder.Create().AddPage(_ => { }).ToByteArray();

    private static byte[] SaveToBytes(PdfDocumentEditor editor)
    {
        string tmp = Path.GetTempFileName();
        try { editor.Save(tmp); return File.ReadAllBytes(tmp); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    private static bool SavedAnnotHasApN(byte[] pdf, string subtype)
    {
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfArray? annots = doc.GetPage(0)!.GetAnnotations();
        if (annots is null) return false;
        foreach (PdfObject entry in annots)
        {
            PdfObject? resolved = entry is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : entry;
            if (resolved is not PdfDictionary annot) continue;
            if (annot.Get(new PdfName("Subtype")) is not PdfName sn || sn.Value != subtype) continue;
            PdfObject? apRaw = annot.Get(new PdfName("AP"));
            PdfObject? ap = apRaw is PdfIndirectReference ar ? doc.GetObject(ar.ObjectNumber) : apRaw;
            return ap is PdfDictionary apDict && apDict.Get(new PdfName("N")) is not null;
        }
        return false;
    }

    private static int RenderNonWhiteInRect(byte[] pdf, PdfRect rect)
    {
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bmp = SKBitmap.FromImage(image);

        int h = bmp.Height;
        int x0 = Math.Max(0, (int)rect.Left - 1), x1 = Math.Min(bmp.Width, (int)rect.Right + 1);
        int y0 = Math.Max(0, (int)(h - rect.Top) - 1), y1 = Math.Min(h, (int)(h - rect.Bottom) + 1);

        int count = 0;
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            SKColor c = bmp.GetPixel(x, y);
            if (c.Alpha > 0 && (c.Red != 255 || c.Green != 255 || c.Blue != 255)) count++;
        }
        return count;
    }

    [Fact]
    public void AddCircle_RoundTrips_Renders()
    {
        var rect = new PdfRect(120, 580, 320, 700);
        byte[] saved;
        int id;
        using (var ms = new MemoryStream(BlankPage()))
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
        {
            id = editor.Pages.AddCircle(0, rect, PdfColor.Blue, PdfColor.Blue, 3.0);
            saved = SaveToBytes(editor);
        }
        Assert.True(id > 0);
        Assert.True(SavedAnnotHasApN(saved, "Circle"));

        using var ms2 = new MemoryStream(saved);
        using PdfDocumentEditor reopened = PdfDocumentEditor.Open(ms2);
        PdfAnnotationInfo a = Assert.Single(reopened.Pages.GetAnnotations(0));
        Assert.Equal("Circle", a.Subtype);
        Assert.True(a.AnnotationId > 0);
        Assert.NotNull(a.StrokeColor);
        Assert.NotNull(a.InteriorColor);
        Assert.Equal(3.0, a.BorderWidth!.Value, 3);

        Assert.True(RenderNonWhiteInRect(saved, rect) > 0, "Circle did not render");
    }

    [Fact]
    public void AddLine_RoundTrips_Renders()
    {
        byte[] saved;
        using (var ms = new MemoryStream(BlankPage()))
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
        {
            editor.Pages.AddLine(0, 120, 600, 320, 680, PdfColor.Red, 4.0);
            saved = SaveToBytes(editor);
        }
        Assert.True(SavedAnnotHasApN(saved, "Line"));

        using var ms2 = new MemoryStream(saved);
        using PdfDocumentEditor reopened = PdfDocumentEditor.Open(ms2);
        PdfAnnotationInfo a = Assert.Single(reopened.Pages.GetAnnotations(0));
        Assert.Equal("Line", a.Subtype);
        Assert.NotNull(a.LineEndpoints);
        Assert.Equal(120, a.LineEndpoints!.Value.X1, 3);
        Assert.Equal(600, a.LineEndpoints!.Value.Y1, 3);
        Assert.Equal(320, a.LineEndpoints!.Value.X2, 3);
        Assert.Equal(680, a.LineEndpoints!.Value.Y2, 3);

        Assert.True(RenderNonWhiteInRect(saved, new PdfRect(120, 600, 320, 680)) > 0, "Line did not render");
    }

    [Fact]
    public void AddInk_RoundTrips_Renders()
    {
        var paths = new List<IReadOnlyList<(double X, double Y)>>
        {
            new List<(double, double)> { (120, 600), (180, 660), (240, 600), (300, 660) }
        };
        byte[] saved;
        using (var ms = new MemoryStream(BlankPage()))
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
        {
            editor.Pages.AddInk(0, paths, PdfColor.Green, 4.0);
            saved = SaveToBytes(editor);
        }
        Assert.True(SavedAnnotHasApN(saved, "Ink"));

        using var ms2 = new MemoryStream(saved);
        using PdfDocumentEditor reopened = PdfDocumentEditor.Open(ms2);
        PdfAnnotationInfo a = Assert.Single(reopened.Pages.GetAnnotations(0));
        Assert.Equal("Ink", a.Subtype);
        Assert.NotNull(a.InkPaths);
        Assert.Single(a.InkPaths!);
        Assert.Equal(4, a.InkPaths![0].Count);

        Assert.True(RenderNonWhiteInRect(saved, new PdfRect(120, 600, 300, 660)) > 0, "Ink did not render");
    }
}
