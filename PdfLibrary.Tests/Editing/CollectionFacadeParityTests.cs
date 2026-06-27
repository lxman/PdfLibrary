using PdfLibrary.Builder;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing;

public class CollectionFacadeParityTests
{
    private static PdfDocument OnePageDoc()
    {
        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("x", 100, 700))
            .ToByteArray();
        return PdfDocument.Load(new MemoryStream(pdf));
    }

    // (Contains is already available via LINQ on IReadOnlyCollection<string>; the genuine gap
    // is a string indexer, parallel to PdfFormFields' this[string].)
    [Fact]
    public void NamedDestinations_StringIndexer_ReturnsDestinationOrNull()
    {
        using PdfDocument doc = OnePageDoc();
        using PdfDocumentEditor editor = doc.Edit();
        editor.NamedDestinations.Set("intro", PdfDestination.ToPage(0));

        Assert.NotNull(editor.NamedDestinations["intro"]);
        Assert.Null(editor.NamedDestinations["missing"]);
    }

    [Fact]
    public void Outlines_RemoveAt_RemovesTopLevelItemByIndex()
    {
        using PdfDocument doc = OnePageDoc();
        using PdfDocumentEditor editor = doc.Edit();
        editor.Outlines.Add("A", 0);
        editor.Outlines.Add("B", 0);

        editor.Outlines.RemoveAt(0);

        Assert.Single(editor.Outlines);
        Assert.Equal("B", editor.Outlines[0].Title);
    }

    [Fact]
    public void Forms_Count_ReportsFieldCount()
    {
        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p =>
            {
                p.AddTextField("field1", 100, 700, 200, 20);
                p.AddTextField("field2", 100, 650, 200, 20);
            })
            .ToByteArray();
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        using PdfDocumentEditor editor = doc.Edit();

        Assert.Equal(2, editor.Forms.Count);
    }
}
