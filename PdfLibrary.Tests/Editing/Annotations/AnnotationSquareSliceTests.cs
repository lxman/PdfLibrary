using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Tests.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing.Annotations;

/// <summary>
/// Vertical-slice proof for the annotation-extension feature: a Square markup annotation
/// added via the editing-add path (the consumer's path) must (1) round-trip with its
/// per-type data + a stable id, (2) carry an /AP /N appearance stream, (3) actually render
/// in this library's renderer, and (4) be deletable by id.
/// </summary>
public class AnnotationSquareSliceTests
{
    private static byte[] BlankPage() =>
        PdfDocumentBuilder.Create().AddPage(_ => { }).ToByteArray();

    private static byte[] SaveToBytes(PdfDocumentEditor editor)
    {
        string tmp = Path.GetTempFileName();
        try
        {
            editor.Save(tmp);
            return File.ReadAllBytes(tmp);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static bool SavedSquareHasApN(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfArray? annots = doc.GetPage(0)!.GetAnnotations();
        if (annots is null) return false;
        foreach (PdfObject entry in annots)
        {
            PdfObject? resolved = entry is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : entry;
            if (resolved is not PdfDictionary annot) continue;
            if (annot.Get(new PdfName("Subtype")) is not PdfName { Value: "Square" }) continue;

            PdfObject? apRaw = annot.Get(new PdfName("AP"));
            PdfObject? ap = apRaw is PdfIndirectReference ar ? doc.GetObject(ar.ObjectNumber) : apRaw;
            if (ap is not PdfDictionary apDict) return false;
            return apDict.Get(new PdfName("N")) is not null;
        }
        return false;
    }

    [Fact]
    public void AddSquare_SameSession_ReturnsIdAndReadsBack()
    {
        byte[] pdf = BlankPage();
        var rect = new PdfRect(100, 600, 300, 700);

        using var ms = new MemoryStream(pdf);
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(ms);

        int id = editor.Pages.AddSquare(0, rect, PdfColor.Red, PdfColor.Yellow, 2.0);
        Assert.True(id > 0);

        PdfAnnotationInfo sq = Assert.Single(editor.Pages.GetAnnotations(0));
        Assert.Equal("Square", sq.Subtype);
        Assert.Equal(id, sq.AnnotationId);
        Assert.NotNull(sq.StrokeColor);
        Assert.NotNull(sq.InteriorColor);
        Assert.Equal(2.0, sq.BorderWidth!.Value, 3);
    }

    [Fact]
    public void AddSquare_RoundTrips_WithApAndData()
    {
        byte[] pdf = BlankPage();
        var rect = new PdfRect(100, 600, 300, 700);

        byte[] saved;
        using (var ms = new MemoryStream(pdf))
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
        {
            editor.Pages.AddSquare(0, rect, PdfColor.Red, PdfColor.Yellow, 2.0);
            saved = SaveToBytes(editor);
        }

        Assert.True(SavedSquareHasApN(saved), "Saved Square annotation must carry an /AP /N appearance stream");

        using var ms2 = new MemoryStream(saved);
        using PdfDocumentEditor reopened = PdfDocumentEditor.Open(ms2);
        PdfAnnotationInfo sq = Assert.Single(reopened.Pages.GetAnnotations(0));

        Assert.Equal("Square", sq.Subtype);
        Assert.True(sq.AnnotationId > 0);
        Assert.Equal(100, sq.Rect.Left, 3);
        Assert.Equal(600, sq.Rect.Bottom, 3);
        Assert.Equal(300, sq.Rect.Right, 3);
        Assert.Equal(700, sq.Rect.Top, 3);
        Assert.NotNull(sq.StrokeColor);
        Assert.Equal(1.0, sq.StrokeColor!.Value.R, 3); // red
        Assert.NotNull(sq.InteriorColor);
        Assert.Equal(2.0, sq.BorderWidth!.Value, 3);
    }

    [Fact]
    public void AddSquare_RendersInsideRect()
    {
        byte[] pdf = BlankPage();
        var rect = new PdfRect(100, 600, 300, 700);

        byte[] saved;
        using (var ms = new MemoryStream(pdf))
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
        {
            // Solid interior fill so the rect region is clearly non-white.
            editor.Pages.AddSquare(0, rect, PdfColor.Red, PdfColor.Red, 3.0);
            saved = SaveToBytes(editor);
        }

        using var ms2 = new MemoryStream(saved);
        using PdfDocument doc = PdfDocument.Load(ms2);
        int drawn = RecordedPageProbe.CountPaintCommandsInRect(
            RecordedPageProbe.Record(doc.GetPage(0)!), 104, 604, 296, 696);
        Assert.True(drawn > 0,
            "Square appearance emitted no paint command in its annotation rectangle.");
    }

    [Fact]
    public void RemoveAnnotation_ById_RemovesSquare()
    {
        byte[] pdf = BlankPage();
        var rect = new PdfRect(100, 600, 300, 700);

        byte[] saved;
        using (var ms = new MemoryStream(pdf))
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
        {
            editor.Pages.AddSquare(0, rect, PdfColor.Red, PdfColor.Yellow, 2.0);
            saved = SaveToBytes(editor);
        }

        byte[] afterDelete;
        using (var ms2 = new MemoryStream(saved))
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms2))
        {
            PdfAnnotationInfo sq = Assert.Single(editor.Pages.GetAnnotations(0));
            editor.Pages.RemoveAnnotation(0, sq.AnnotationId);
            afterDelete = SaveToBytes(editor);
        }

        using var ms3 = new MemoryStream(afterDelete);
        using PdfDocumentEditor reopened = PdfDocumentEditor.Open(ms3);
        Assert.Empty(reopened.Pages.GetAnnotations(0));
    }
}
