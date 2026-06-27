using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing;

public class AnnotationReadRemoveTests
{
    private static byte[] OnePagePdf() =>
        PdfDocumentBuilder.Create().AddPage(p => p.AddText("x", 100, 700)).ToByteArray();

    [Fact]
    public void GetAnnotations_ReturnsAddedAnnotations()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(OnePagePdf()));
        using PdfDocumentEditor editor = doc.Edit();
        editor.Pages.AddNote(0, 50, 750, "a note");
        editor.Pages.AddExternalLink(0, new PdfRect(10, 10, 100, 30), "https://example.com");

        IReadOnlyList<PdfAnnotationInfo> annots = editor.Pages.GetAnnotations(0);

        Assert.Equal(2, annots.Count);
        Assert.Contains(annots, a => a is { Subtype: "Text", Contents: "a note" });
        Assert.Contains(annots, a => a.Subtype == "Link");
    }

    [Fact]
    public void GetAnnotations_NoAnnotations_ReturnsEmpty()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(OnePagePdf()));
        using PdfDocumentEditor editor = doc.Edit();

        Assert.Empty(editor.Pages.GetAnnotations(0));
    }

    [Fact]
    public void RemoveAnnotationAt_RemovesOne()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(OnePagePdf()));
        using PdfDocumentEditor editor = doc.Edit();
        editor.Pages.AddNote(0, 50, 750, "a note");
        editor.Pages.AddExternalLink(0, new PdfRect(10, 10, 100, 30), "https://example.com");

        editor.Pages.RemoveAnnotationAt(0, 0);

        Assert.Single(editor.Pages.GetAnnotations(0));
    }

    [Fact]
    public void RemoveAnnotationAt_OutOfRange_Throws()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(OnePagePdf()));
        using PdfDocumentEditor editor = doc.Edit();
        editor.Pages.AddNote(0, 50, 750, "a note");

        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Pages.RemoveAnnotationAt(0, 5));
    }
}
