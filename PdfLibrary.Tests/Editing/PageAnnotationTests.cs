using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class PageAnnotationTests
{
    private static byte[] TwoPages() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("HOME", 100, 700))
            .AddPage(p => p.AddText("DEST", 100, 700))
            .ToByteArray();

    private static List<PdfDictionary> Annots(PdfDocument doc, int pageIndex)
    {
        PdfDictionary page = PageTreeOps.PageDicts(doc)[pageIndex];
        var list = new List<PdfDictionary>();
        PdfObject? a = page.Get(new PdfName("Annots"));
        if (a is PdfIndirectReference r) a = doc.GetObject(r.ObjectNumber);
        if (a is PdfArray arr)
            foreach (PdfObject e in arr)
                if ((e is PdfIndirectReference er ? doc.GetObject(er.ObjectNumber) : e) is PdfDictionary d)
                    list.Add(d);
        return list;
    }

    private static string Subtype(PdfDictionary annot) => ((PdfName)annot[PdfName.Subtype]).Value;

    [Fact]
    public void AddNote_AppearsInAnnotsWithContents()
    {
        using var ms = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(TwoPages())))
        {
            PdfDocumentEditor edit = doc.Edit();
            edit.Pages.AddNote(0, 300, 700, "hello note");
            edit.Save(ms);
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        PdfDictionary note = Assert.Single(Annots(reloaded, 0));
        Assert.Equal("Text", Subtype(note));
        Assert.Equal("hello note", ((PdfString)note[new PdfName("Contents")]).Value);
    }

    [Fact]
    public void AddLink_InternalDest_ResolvesToTargetPage()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(TwoPages()));
        PdfDocumentEditor edit = doc.Edit();
        edit.Pages.AddLink(0, new PdfRect(100, 680, 200, 700), targetPageIndex: 1);

        PdfDictionary link = Assert.Single(Annots(doc, 0));
        Assert.Equal("Link", Subtype(link));
        var dest = (PdfArray)link[new PdfName("Dest")];
        var targetRef = (PdfIndirectReference)dest[0];
        var page1Ref = (PdfIndirectReference)PageTreeOps.Kids(doc)[1];
        Assert.Equal(page1Ref.ObjectNumber, targetRef.ObjectNumber);
    }

    [Fact]
    public void AddExternalLink_HasUriAction()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(TwoPages()));
        PdfDocumentEditor edit = doc.Edit();
        edit.Pages.AddExternalLink(0, new PdfRect(100, 680, 200, 700), "https://example.com");

        PdfDictionary link = Assert.Single(Annots(doc, 0));
        var action = (PdfDictionary)link[new PdfName("A")];
        Assert.Equal("URI", ((PdfName)action[new PdfName("S")]).Value);
        Assert.Equal("https://example.com", ((PdfString)action[new PdfName("URI")]).Value);
    }

    [Fact]
    public void AddHighlight_HasQuadPointsAndColor()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(TwoPages()));
        PdfDocumentEditor edit = doc.Edit();
        edit.Pages.AddHighlight(0, new PdfRect(100, 690, 160, 702));

        PdfDictionary hl = Assert.Single(Annots(doc, 0));
        Assert.Equal("Highlight", Subtype(hl));
        Assert.Equal(8, ((PdfArray)hl[new PdfName("QuadPoints")]).Count);
        Assert.True(hl.ContainsKey(new PdfName("C")));
    }
}
