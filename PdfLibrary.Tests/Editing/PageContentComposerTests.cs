using System.Text;
using PdfLibrary.Builder;
using PdfLibrary.Core;
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
        PdfDocument doc = PdfDocument.Load(new MemoryStream(
            PdfDocumentBuilder.Create().AddPage(p => p.AddText("body", 100, 700)).ToByteArray()));
        doc.Edit(); // materialize + flatten so /Resources and /Contents are normalized onto the page
        return (doc, PageTreeOps.PageDicts(doc)[0]);
    }

    /// <summary>Two-page document whose /Pages root holds /Resources as a single indirect reference
    /// and whose page kids declare no /Resources of their own — reproduces what
    /// PageTreeNormalizer.MaterializeInheritance produces for a genuinely inherited /Resources
    /// (both pages resolve the same PdfDictionary instance after Edit()).</summary>
    private static (PdfDocument doc, IReadOnlyList<PdfDictionary> pages) LoadTwoPagesSharedResources()
    {
        PdfDocument doc = PdfDocument.Load(new MemoryStream(
            PdfDocumentBuilder.Create()
                .AddPage(p => p.AddText("page0", 100, 700))
                .AddPage(p => p.AddText("page1", 100, 700))
                .ToByteArray()));

        IReadOnlyList<PdfDictionary> rawPages = PageTreeOps.PageDicts(doc);
        var shared = new PdfDictionary();
        PdfIndirectReference sharedRef = doc.RegisterObject(shared);
        foreach (PdfDictionary p in rawPages)
            p.Remove(new PdfName("Resources"));
        doc.PageTreeRootDictionary![new PdfName("Resources")] = sharedRef;

        doc.Edit();
        return (doc, PageTreeOps.PageDicts(doc));
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

    [Fact]
    public void RegisterXObject_DoesNotLeakToSiblingPage_WhenResourcesIsShared()
    {
        (PdfDocument doc, IReadOnlyList<PdfDictionary> pages) = LoadTwoPagesSharedResources();
        PdfDictionary page0 = pages[0];
        PdfDictionary page1 = pages[1];

        PdfIndirectReference xobjRef = doc.RegisterObject(new PdfDictionary());
        string name = PageContentComposer.RegisterXObject(doc, page0, xobjRef);
        Assert.Equal("Stamp0", name);

        var res0 = (PdfDictionary)page0[new PdfName("Resources")];
        var xobj0 = (PdfDictionary)res0[new PdfName("XObject")];
        Assert.True(xobj0.ContainsKey(new PdfName("Stamp0")), "Page 0 should have received the registered XObject.");

        // Page 1 was never stamped: its resolved /XObject dictionary must not see Stamp0.
        PdfObject res1Obj = page1[new PdfName("Resources")];
        PdfDictionary res1 = res1Obj is PdfIndirectReference r ? (PdfDictionary)doc.GetObject(r.ObjectNumber)! : (PdfDictionary)res1Obj;
        if (res1.TryGetValue(new PdfName("XObject"), out PdfObject xobj1Obj))
        {
            PdfDictionary xobj1 = xobj1Obj is PdfIndirectReference xr ? (PdfDictionary)doc.GetObject(xr.ObjectNumber)! : (PdfDictionary)xobj1Obj;
            Assert.False(xobj1.ContainsKey(new PdfName("Stamp0")), "Page 1 must not leak page 0's registered XObject.");
        }
        doc.Dispose();
    }

    [Fact]
    public void RegisterOpacity_DoesNotLeakToSiblingPage_WhenResourcesIsShared()
    {
        (PdfDocument doc, IReadOnlyList<PdfDictionary> pages) = LoadTwoPagesSharedResources();
        PdfDictionary page0 = pages[0];
        PdfDictionary page1 = pages[1];

        string name = PageContentComposer.RegisterOpacity(doc, page0, 0.3);
        Assert.Equal("GsStamp0", name);

        var res0 = (PdfDictionary)page0[new PdfName("Resources")];
        var gs0 = (PdfDictionary)res0[new PdfName("ExtGState")];
        Assert.True(gs0.ContainsKey(new PdfName("GsStamp0")), "Page 0 should have received the registered ExtGState.");

        PdfObject res1Obj = page1[new PdfName("Resources")];
        PdfDictionary res1 = res1Obj is PdfIndirectReference r ? (PdfDictionary)doc.GetObject(r.ObjectNumber)! : (PdfDictionary)res1Obj;
        if (res1.TryGetValue(new PdfName("ExtGState"), out PdfObject gs1Obj))
        {
            PdfDictionary gs1 = gs1Obj is PdfIndirectReference gr ? (PdfDictionary)doc.GetObject(gr.ObjectNumber)! : (PdfDictionary)gs1Obj;
            Assert.False(gs1.ContainsKey(new PdfName("GsStamp0")), "Page 1 must not leak page 0's registered ExtGState.");
        }
        doc.Dispose();
    }

    [Fact]
    public void RegisterXObject_ReusesPagePrivateResources_WithoutGratuitousCopy()
    {
        (PdfDocument doc, PdfDictionary page) = LoadOnePage();
        var originalResources = (PdfDictionary)page[new PdfName("Resources")];

        PdfIndirectReference xobjRef = doc.RegisterObject(new PdfDictionary());
        PageContentComposer.RegisterXObject(doc, page, xobjRef);

        var resourcesAfter = (PdfDictionary)page[new PdfName("Resources")];
        Assert.True(ReferenceEquals(originalResources, resourcesAfter),
            "A page that already owns a direct /Resources dictionary must not be copied on registration.");
        doc.Dispose();
    }

    [Fact]
    public void RegisterXObject_CreatesFreshResources_WhenPageHasNoneAtAll()
    {
        PdfDocument doc = PdfDocument.Load(new MemoryStream(
            PdfDocumentBuilder.Create().AddPage(p => p.AddText("body", 100, 700)).ToByteArray()));
        IReadOnlyList<PdfDictionary> rawPages = PageTreeOps.PageDicts(doc);
        rawPages[0].Remove(new PdfName("Resources"));
        doc.Edit();
        PdfDictionary page = PageTreeOps.PageDicts(doc)[0];
        Assert.False(page.ContainsKey(new PdfName("Resources")));

        PdfIndirectReference xobjRef = doc.RegisterObject(new PdfDictionary());
        string name = PageContentComposer.RegisterXObject(doc, page, xobjRef);

        Assert.Equal("Stamp0", name);
        var resources = (PdfDictionary)page[new PdfName("Resources")];
        var xobj = (PdfDictionary)resources[new PdfName("XObject")];
        Assert.True(xobj.ContainsKey(new PdfName("Stamp0")));
        doc.Dispose();
    }
}
