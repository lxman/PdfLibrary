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

    private static PdfObject? Res(PdfDocument d, PdfObject? x) =>
        x is PdfIndirectReference r ? d.GetObject(r.ObjectNumber) : x;

    /// <summary>Reads the /BaseFont of the first font in the annotation's /AP /N /Resources /Font.</summary>
    private static string? SavedAnnotApBaseFont(byte[] pdf, string subtype)
    {
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfArray? annots = doc.GetPage(0)!.GetAnnotations();
        if (annots is null) return null;
        foreach (PdfObject entry in annots)
        {
            if (Res(doc, entry) is not PdfDictionary annot) continue;
            if (annot.Get(new PdfName("Subtype")) is not PdfName sn || sn.Value != subtype) continue;
            if (Res(doc, annot.Get(new PdfName("AP"))) is not PdfDictionary ap) continue;
            if (Res(doc, ap.Get(new PdfName("N"))) is not PdfStream n) continue;
            if (Res(doc, n.Dictionary.Get(new PdfName("Resources"))) is not PdfDictionary res) continue;
            if (Res(doc, res.Get(new PdfName("Font"))) is not PdfDictionary fontDict) continue;
            foreach (KeyValuePair<PdfName, PdfObject> kv in fontDict)
                if (Res(doc, kv.Value) is PdfDictionary fd && fd.Get(new PdfName("BaseFont")) is PdfName bf)
                    return bf.Value;
        }
        return null;
    }

    /// <summary>Decoded text of the first matching annotation's /AP /N appearance stream.</summary>
    private static string SavedAnnotApStreamText(byte[] pdf, string subtype)
    {
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfArray? annots = doc.GetPage(0)!.GetAnnotations();
        if (annots is null) return "";
        foreach (PdfObject entry in annots)
        {
            if (Res(doc, entry) is not PdfDictionary annot) continue;
            if (annot.Get(new PdfName("Subtype")) is not PdfName sn || sn.Value != subtype) continue;
            if (Res(doc, annot.Get(new PdfName("AP"))) is not PdfDictionary ap) continue;
            if (Res(doc, ap.Get(new PdfName("N"))) is PdfStream n)
                return System.Text.Encoding.Latin1.GetString(n.GetDecodedData());
        }
        return "";
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>Number of disjoint horizontal bands (separated by ≥1 blank row) that contain ink in the rect.</summary>
    private static int InkRowBands(byte[] pdf, PdfRect rect)
    {
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bmp = SKBitmap.FromImage(image);
        int h = bmp.Height;
        int x0 = Math.Max(0, (int)rect.Left - 1), x1 = Math.Min(bmp.Width, (int)rect.Right + 1);
        int y0 = Math.Max(0, (int)(h - rect.Top) - 1), y1 = Math.Min(h, (int)(h - rect.Bottom) + 1);
        int bands = 0; bool inBand = false;
        for (int y = y0; y < y1; y++)
        {
            bool rowHasInk = false;
            for (int x = x0; x < x1; x++)
            {
                SKColor c = bmp.GetPixel(x, y);
                if (c.Alpha > 0 && (c.Red != 255 || c.Green != 255 || c.Blue != 255)) { rowHasInk = true; break; }
            }
            if (rowHasInk && !inBand) { bands++; inBand = true; }
            else if (!rowHasInk) inBand = false;
        }
        return bands;
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

    [Theory]
    [InlineData("TiRo", "Times-Roman")]
    [InlineData("Cour", "Courier")]
    [InlineData("HeBo", "Helvetica-Bold")]
    public void AddFreeText_WithFont_BakesAppearanceWithThatBaseFont(string fontName, string expectedBaseFont)
    {
        var rect = new PdfRect(100, 600, 400, 660);
        byte[] saved;
        using (var ms = new MemoryStream(BlankPage()))
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
        {
            editor.Pages.AddFreeText(0, rect, "Glyphs", 18.0, PdfColor.Black, quadding: 0, fontName: fontName);
            saved = SaveToBytes(editor);
        }
        Assert.True(SavedAnnotHasApN(saved, "FreeText"));
        Assert.True(RenderNonWhiteInRect(saved, rect) > 0, $"{fontName} FreeText did not render");
        Assert.Equal(expectedBaseFont, SavedAnnotApBaseFont(saved, "FreeText"));
    }

    [Fact]
    public void AddFreeText_MultiLine_LaysOutSeparateLines()
    {
        var rect = new PdfRect(100, 560, 400, 660);
        byte[] saved;
        using (var ms = new MemoryStream(BlankPage()))
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
        {
            editor.Pages.AddFreeText(0, rect, "Line one\nLine two", 14.0, PdfColor.Black);
            saved = SaveToBytes(editor);
        }

        // 1) /Contents round-trips with the line separator preserved.
        using var ms2 = new MemoryStream(saved);
        using PdfDocumentEditor reopened = PdfDocumentEditor.Open(ms2);
        PdfAnnotationInfo a = Assert.Single(reopened.Pages.GetAnnotations(0));
        Assert.Contains("\n", a.Contents);

        // 2) The /AP /N stream shows each line separately: two show ops + a line advance,
        //    and no single Tj carrying an embedded newline byte.
        string ap = SavedAnnotApStreamText(saved, "FreeText");
        Assert.Equal(2, CountOccurrences(ap, " Tj"));
        Assert.True(ap.Contains("T*") || ap.Contains(" Td"), "expected a line-advance (T* or Td) between lines");
        Assert.DoesNotContain("\n) Tj", ap); // no embedded newline inside a show string

        // 3) The two lines render on two distinct vertical bands.
        Assert.True(InkRowBands(saved, rect) >= 2, "multi-line FreeText did not render on two baselines");
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
