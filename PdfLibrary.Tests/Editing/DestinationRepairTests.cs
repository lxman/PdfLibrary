using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class DestinationRepairTests
{
    private static PdfObject? Deref(PdfDocument doc, PdfObject? o) =>
        o is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : o;

    private static List<string> CollectOutlineTitles(PdfDocument doc)
    {
        var titles = new List<string>();
        if (Deref(doc, doc.CatalogDictionary?.Get(new PdfName("Outlines"))) is not PdfDictionary outlines)
            return titles;
        Walk(outlines.Get(new PdfName("First")));
        return titles;

        void Walk(PdfObject? reference)
        {
            var guard = 0;
            while (reference is not null && guard++ < 10000)
            {
                if (Deref(doc, reference) is not PdfDictionary item) break;
                if (item.Get(new PdfName("Title")) is PdfString t) titles.Add(t.Value);
                Walk(item.Get(new PdfName("First")));
                reference = item.Get(new PdfName("Next"));
            }
        }
    }

    private static int CountLinkAnnotations(PdfDocument doc, PdfPage page)
    {
        if (Deref(doc, page.Dictionary.Get(new PdfName("Annots"))) is not PdfArray annots) return 0;
        return annots.Count(a =>
            Deref(doc, a) is PdfDictionary annot
            && annot.TryGetValue(PdfName.Subtype, out PdfObject s) && s is PdfName { Value: "Link" });
    }

    [Fact]
    public void RemoveAt_StripsOutlineItemTargetingDeletedPage_KeepsOthers()
    {
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("PAGE A", 100, 700))
            .AddPage(p => p.AddText("PAGE B", 100, 700))
            .AddBookmark("Bookmark A", 0)
            .AddBookmark("Bookmark B", 1)
            .ToByteArray();

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(bytes));
        PdfDocumentEditor edit = doc.Edit();
        edit.Pages.RemoveAt(1);

        List<string> titles = CollectOutlineTitles(doc);
        Assert.Contains("Bookmark A", titles);
        Assert.DoesNotContain("Bookmark B", titles);
    }

    [Fact]
    public void RemoveAt_RemovesLinkAnnotationTargetingDeletedPage()
    {
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(p => { p.AddText("HOME", 100, 700); p.AddLink(100, 680, 100, 20, 1); })
            .AddPage(p => p.AddText("DEST", 100, 700))
            .ToByteArray();

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(bytes));
        PdfDocumentEditor edit = doc.Edit();
        Assert.Equal(1, CountLinkAnnotations(doc, edit.Pages[0])); // sanity: link exists pre-delete
        edit.Pages.RemoveAt(1);
        Assert.Equal(0, CountLinkAnnotations(doc, edit.Pages[0]));
    }
}
