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
/// Builder-path mirror: authoring markup annotations into a NEW document via PdfPageBuilder must
/// emit the dict keys AND a generated /AP /N stream (the builder writer reuses the same shared
/// content builder as the editing path), so they render here and round-trip through the reader.
/// </summary>
public class AnnotationBuilderPathTests
{
    private static int CountAnnotsWithApBySubtype(byte[] pdf, out HashSet<string> subtypesWithAp)
    {
        subtypesWithAp = new HashSet<string>();
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfArray? annots = doc.GetPage(0)!.GetAnnotations();
        if (annots is null) return 0;
        int total = 0;
        foreach (PdfObject entry in annots)
        {
            PdfObject? resolved = entry is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : entry;
            if (resolved is not PdfDictionary annot) continue;
            total++;
            string sub = annot.Get(new PdfName("Subtype")) is PdfName sn ? sn.Value : "";
            PdfObject? apRaw = annot.Get(new PdfName("AP"));
            PdfObject? ap = apRaw is PdfIndirectReference ar ? doc.GetObject(ar.ObjectNumber) : apRaw;
            if (ap is PdfDictionary apDict && apDict.Get(new PdfName("N")) is not null)
                subtypesWithAp.Add(sub);
        }
        return total;
    }

    private static byte[] BuildAllTypes()
    {
        var ink = new List<IReadOnlyList<(double X, double Y)>>
        {
            new List<(double, double)> { (100, 400), (160, 440), (220, 400) }
        };
        return PdfDocumentBuilder.Create()
            .AddPage(p =>
            {
                p.AddSquare(new PdfRect(100, 600, 300, 700), PdfColor.Red, PdfColor.Yellow, 2.0);
                p.AddCircle(new PdfRect(100, 470, 300, 570), PdfColor.Blue, PdfColor.Blue, 3.0);
                p.AddLine(100, 360, 300, 380, PdfColor.Red, 4.0);
                p.AddInk(ink, PdfColor.Green, 4.0);
                p.AddFreeText(new PdfRect(100, 280, 420, 320), "Hello FreeText", 18.0, PdfColor.Black);
                p.AddHighlight(new PdfRect(100, 240, 300, 260));
                p.AddNote(120, 200, "A note");
            })
            .ToByteArray();
    }

    [Fact]
    public void BuilderAnnotations_AllTypes_EmitApAndRoundTrip()
    {
        byte[] pdf = BuildAllTypes();

        int total = CountAnnotsWithApBySubtype(pdf, out HashSet<string> withAp);
        Assert.Equal(7, total);
        foreach (string expected in new[] { "Square", "Circle", "Line", "Ink", "FreeText", "Highlight", "Text" })
            Assert.Contains(expected, withAp);

        // Reader round-trip via the editing API on the builder-produced document.
        using var ms = new MemoryStream(pdf);
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(ms);
        IReadOnlyList<PdfAnnotationInfo> annots = editor.Pages.GetAnnotations(0);
        Assert.Equal(7, annots.Count);

        PdfAnnotationInfo line = Assert.Single(annots, a => a.Subtype == "Line");
        Assert.NotNull(line.LineEndpoints);
        Assert.Equal(100, line.LineEndpoints!.Value.X1, 3);

        PdfAnnotationInfo ft = Assert.Single(annots, a => a.Subtype == "FreeText");
        Assert.Equal("Hello FreeText", ft.Contents);
    }

    [Fact]
    public void BuilderSquare_Renders()
    {
        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddSquare(new PdfRect(100, 600, 300, 700), PdfColor.Red, PdfColor.Red, 3.0))
            .ToByteArray();

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bmp = SKBitmap.FromImage(image);

        int h = bmp.Height;
        int count = 0;
        for (int y = (h - 700) + 4; y < (h - 600) - 4; y++)
        for (int x = 104; x < 296; x++)
        {
            SKColor c = bmp.GetPixel(x, y);
            if (c.Alpha > 0 && (c.Red != 255 || c.Green != 255 || c.Blue != 255)) count++;
        }
        Assert.True(count > 0, "Builder Square did not render");
    }
}
