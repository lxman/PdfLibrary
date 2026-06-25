using PdfLibrary.Builder;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing;

public class NavigationFacadeAdditiveTests
{
    private static PdfDocument OnePageDoc()
    {
        byte[] pdf = PdfDocumentBuilder.Create().AddPage(p => p.AddText("x", 100, 700)).ToByteArray();
        return PdfDocument.Load(new MemoryStream(pdf));
    }

    [Fact]
    public void NamedDestinations_Entries_YieldsNameDestinationPairs()
    {
        using PdfDocument doc = OnePageDoc();
        using PdfDocumentEditor editor = doc.Edit();
        editor.NamedDestinations.Set("a", PdfDestination.ToPage(0));
        editor.NamedDestinations.Set("b", PdfDestination.FitPage(0));

        Dictionary<string, PdfDestination> entries =
            editor.NamedDestinations.Entries().ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Equal(2, entries.Count);
        Assert.True(entries.ContainsKey("a"));
        Assert.True(entries.ContainsKey("b"));
    }

    [Fact]
    public void Outlines_Insert_PlacesItemAtIndex()
    {
        using PdfDocument doc = OnePageDoc();
        using PdfDocumentEditor editor = doc.Edit();
        editor.Outlines.Add("A", 0);
        editor.Outlines.Add("C", 0);

        editor.Outlines.Insert(1, "B", 0);

        Assert.Equal(new[] { "A", "B", "C" }, editor.Outlines.Select(o => o.Title));
    }
}
