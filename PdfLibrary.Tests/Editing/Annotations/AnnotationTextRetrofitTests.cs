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
/// FreeText annotation plus the /AP retrofit for the existing Highlight and Note (Text) add paths,
/// so all three render in this library's /AP-only renderer.
/// </summary>
public class AnnotationTextRetrofitTests
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
    public void AddFreeText_RoundTrips_Renders()
    {
        var rect = new PdfRect(100, 600, 400, 660);
        byte[] saved;
        int id;
        using (var ms = new MemoryStream(BlankPage()))
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
        {
            id = editor.Pages.AddFreeText(0, rect, "Hello FreeText", 18.0, PdfColor.Black);
            saved = SaveToBytes(editor);
        }
        Assert.True(id > 0);
        Assert.True(SavedAnnotHasApN(saved, "FreeText"));

        using var ms2 = new MemoryStream(saved);
        using PdfDocumentEditor reopened = PdfDocumentEditor.Open(ms2);
        PdfAnnotationInfo a = Assert.Single(reopened.Pages.GetAnnotations(0));
        Assert.Equal("FreeText", a.Subtype);
        Assert.Equal("Hello FreeText", a.Contents);
        Assert.Equal(0, a.Quadding);
        Assert.False(string.IsNullOrEmpty(a.DefaultAppearance));

        Assert.True(RenderNonWhiteInRect(saved, rect) > 0, "FreeText did not render");
    }

    [Fact]
    public void AddFreeText_WithFont_RoundTripsFontInDa()
    {
        var rect = new PdfRect(100, 600, 400, 660);
        byte[] saved;
        using (var ms = new MemoryStream(BlankPage()))
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
        {
            editor.Pages.AddFreeText(0, rect, "Times text", 18.0, PdfColor.Black, quadding: 0, fontName: "TiRo");
            saved = SaveToBytes(editor);
        }
        using var ms2 = new MemoryStream(saved);
        using PdfDocumentEditor reopened = PdfDocumentEditor.Open(ms2);
        PdfAnnotationInfo a = Assert.Single(reopened.Pages.GetAnnotations(0));
        Assert.Contains("/TiRo", a.DefaultAppearance);
    }

    [Fact]
    public void AddHighlight_NowGeneratesAp_AndRenders()
    {
        var rect = new PdfRect(100, 600, 300, 620);
        byte[] saved;
        using (var ms = new MemoryStream(BlankPage()))
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
        {
            editor.Pages.AddHighlight(0, rect, PdfColor.Yellow);
            saved = SaveToBytes(editor);
        }
        Assert.True(SavedAnnotHasApN(saved, "Highlight"), "Highlight should now carry an /AP stream");
        Assert.True(RenderNonWhiteInRect(saved, rect) > 0, "Highlight did not render");
    }

    [Fact]
    public void AddNote_NowGeneratesAp_AndRenders()
    {
        byte[] saved;
        using (var ms = new MemoryStream(BlankPage()))
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
        {
            editor.Pages.AddNote(0, 120, 660, "A sticky note");
            saved = SaveToBytes(editor);
        }
        Assert.True(SavedAnnotHasApN(saved, "Text"), "Note should now carry an /AP stream");
        // Note icon rect is 24x24 anchored at (120, 636)-(144, 660).
        Assert.True(RenderNonWhiteInRect(saved, new PdfRect(120, 636, 144, 660)) > 0, "Note icon did not render");
    }
}
