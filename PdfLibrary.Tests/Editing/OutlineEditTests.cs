using PdfLibrary.Builder;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Optimization;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class OutlineEditTests
{
    private static byte[] ThreePageDoc() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("PAGE 0", 100, 700))
            .AddPage(p => p.AddText("PAGE 1", 100, 700))
            .AddPage(p => p.AddText("PAGE 2", 100, 700))
            .ToByteArray();

    private static byte[] SaveReload(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Edit().Save(ms, new PdfSaveOptions { RemoveOrphans = true });
        return ms.ToArray();
    }

    private static PdfObject? Deref(PdfDocument doc, PdfObject? o) =>
        o is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : o;

    [Fact]
    public void BuildNestedTree_SaveReload_TitlesNestingDestinationsResolve()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        edit.Outlines.Add("Chapter 1", PdfDestination.FitPage(0), c =>
        {
            c.Add("Section 1.1", 1);
            c.Add("Section 1.2", 2);
        });
        edit.Outlines.Add("Chapter 2", 2);

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfDocumentEditor edit2 = reloaded.Edit();

        Assert.Equal(2, edit2.Outlines.Count);
        Assert.Equal("Chapter 1", edit2.Outlines[0].Title);
        Assert.Equal("Chapter 2", edit2.Outlines[1].Title);

        PdfOutlineItem ch1 = edit2.Outlines[0];
        Assert.Equal(2, ch1.Children.Count);
        Assert.Equal("Section 1.1", ch1.Children[0].Title);
        Assert.Equal("Section 1.2", ch1.Children[1].Title);

        Assert.Equal(0, ch1.Destination!.PageIndex);
        Assert.Equal(1, ch1.Children[0].Destination!.PageIndex);
        Assert.Equal(2, ch1.Children[1].Destination!.PageIndex);
        Assert.Equal(2, edit2.Outlines[1].Destination!.PageIndex);
    }

    [Fact]
    public void EditTitleInPlace_RoundTrips()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.Outlines.Add("Original", 0);

        edit.Outlines[0].Title = "Renamed";

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        Assert.Equal("Renamed", reloaded.Edit().Outlines[0].Title);
    }

    [Fact]
    public void Remove_Branch_GoneAfterReload()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.Outlines.Add("Keep", 0);
        edit.Outlines.Add("Drop", PdfDestination.ToPage(1), c => c.Add("Drop child", 2));

        edit.Outlines[1].Remove();

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfOutlineCollection outlines = reloaded.Edit().Outlines;
        Assert.Single(outlines);
        Assert.Equal("Keep", outlines[0].Title);
    }

    [Fact]
    public void ClosedItem_NegativeCount_PreservedOnReload()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        PdfOutlineItem ch = edit.Outlines.Add("Closed Chapter", PdfDestination.ToPage(0), c =>
        {
            c.Add("kid a", 1);
            c.Add("kid b", 2);
        });
        ch.IsOpen = false;

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));

        var outlines = (PdfDictionary)Deref(reloaded, reloaded.CatalogDictionary!.Get(new PdfName("Outlines")))!;
        var first = (PdfDictionary)Deref(reloaded, outlines.Get(new PdfName("First")))!;
        var count = (PdfInteger)first.Get(new PdfName("Count"))!;
        Assert.True(count.Value < 0, $"expected negative /Count, got {count.Value}");

        Assert.False(reloaded.Edit().Outlines[0].IsOpen);
    }

    [Fact]
    public void Clear_RemovesOutlines()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.Outlines.Add("Something", 0);
        edit.Outlines.Clear();

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        Assert.Null(reloaded.CatalogDictionary!.Get(new PdfName("Outlines")));
        Assert.Empty(reloaded.Edit().Outlines);
    }

    [Fact]
    public void NonAsciiTitle_RoundTrips()
    {
        const string title = "Chapître résumé éè 中文";
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.Outlines.Add(title, 0);

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        Assert.Equal(title, reloaded.Edit().Outlines[0].Title);
    }

    [Fact]
    public void Destination_ResolvesFromGoToActionWhenNoDest()
    {
        // Many real-world PDFs express an outline target with an /A GoTo action rather than a
        // direct /Dest (ISO 32000-2 §12.3.3). Build a bookmark to page 2 (which writes /Dest),
        // rewrite it into an /A << /S /GoTo /D [...] >> action, and confirm it still resolves.
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.Outlines.Add("Chapter", 2);

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfOutlineItem item = reloaded.Edit().Outlines[0];
        Assert.Equal(2, item.Destination!.PageIndex);   // resolves via /Dest today

        // Move the destination array from /Dest into a GoTo action under /A.
        PdfDictionary dict = item.Node.Dict;
        PdfObject destArray = dict.Get(new PdfName("Dest"))!;
        dict.Remove(new PdfName("Dest"));
        var action = new PdfDictionary
        {
            [new PdfName("S")] = new PdfName("GoTo"),
            [new PdfName("D")] = destArray,
        };
        dict[new PdfName("A")] = action;

        Assert.Null(dict.Get(new PdfName("Dest")));
        Assert.Equal(2, item.Destination!.PageIndex);   // now resolves via the /A GoTo action
    }
}
