using System.Text;
using PdfLibrary.Builder;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Stamping;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class PageContentComposerTests
{
    private static (PdfDocument doc, PdfDictionary page) LoadOnePage()
    {
        var doc = PdfDocument.Load(new MemoryStream(
            PdfDocumentBuilder.Create().AddPage(p => p.AddText("body", 100, 700)).ToByteArray()));
        doc.Edit(); // materialize + flatten so /Resources and /Contents are normalized onto the page
        return (doc, PageTreeOps.PageDicts(doc)[0]);
    }

    [Fact]
    public void RegisterXObject_PicksUniqueNames()
    {
        (PdfDocument doc, PdfDictionary page) = LoadOnePage();
        PdfIndirectReference a = doc.RegisterObject(new PdfDictionary());
        PdfIndirectReference b = doc.RegisterObject(new PdfDictionary());
        Assert.Equal("Stamp0", PageContentComposer.RegisterXObject(doc, page, a));
        Assert.Equal("Stamp1", PageContentComposer.RegisterXObject(doc, page, b));
        doc.Dispose();
    }

    [Fact]
    public void Overlay_AppendsInvocation_AfterWrappedExistingContent()
    {
        (PdfDocument doc, PdfDictionary page) = LoadOnePage();
        PdfArray contents = PageContentComposer.EnsureContentsArray(doc, page);
        int before = contents.Count;
        PageContentComposer.WrapExisting(doc, contents);
        PageContentComposer.AddInvocation(doc, contents, "q /Stamp0 Do Q"u8.ToArray(), underlay: false);

        Assert.Equal(before + 3, contents.Count);
        var last = (PdfStream)doc.GetObject(((PdfIndirectReference)contents[^1]).ObjectNumber)!;
        Assert.Contains("Stamp0", Encoding.ASCII.GetString(last.GetDecodedData()));
        doc.Dispose();
    }

    [Fact]
    public void Underlay_PrependsInvocation()
    {
        (PdfDocument doc, PdfDictionary page) = LoadOnePage();
        PdfArray contents = PageContentComposer.EnsureContentsArray(doc, page);
        PageContentComposer.AddInvocation(doc, contents, "q /Stamp0 Do Q"u8.ToArray(), underlay: true);
        var first = (PdfStream)doc.GetObject(((PdfIndirectReference)contents[0]).ObjectNumber)!;
        Assert.Contains("Stamp0", Encoding.ASCII.GetString(first.GetDecodedData()));
        doc.Dispose();
    }

    [Fact]
    public void RegisterOpacity_AddsExtGStateWithCa()
    {
        (PdfDocument doc, PdfDictionary page) = LoadOnePage();
        string name = PageContentComposer.RegisterOpacity(doc, page, 0.3);
        Assert.StartsWith("GsStamp", name);
        var res = (PdfDictionary)page[new PdfName("Resources")];
        var gss = (PdfDictionary)res[new PdfName("ExtGState")];
        var gs = (PdfDictionary)doc.GetObject(((PdfIndirectReference)gss[new PdfName(name)]).ObjectNumber)!;
        Assert.Equal(0.3, ((PdfReal)gs[new PdfName("ca")]).Value, 3);
        doc.Dispose();
    }
}
