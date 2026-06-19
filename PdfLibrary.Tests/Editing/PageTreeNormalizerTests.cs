using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class PageTreeNormalizerTests
{
    // Builds: catalog(3) -> Pages root(1) -> [ intermediate(4) -> [ page(5), page(6) ] ]
    // with MediaBox inheritable on the intermediate node only.
    private static PdfDocument BuildBalanced()
    {
        var doc = new PdfDocument();

        var page5 = new PdfDictionary(); page5[PdfName.TypeName] = new PdfName("Page");
        var page6 = new PdfDictionary(); page6[PdfName.TypeName] = new PdfName("Page");
        doc.AddObject(5, 0, page5);
        doc.AddObject(6, 0, page6);

        var mid = new PdfDictionary();
        mid[PdfName.TypeName] = new PdfName("Pages");
        mid[new PdfName("Kids")] = new PdfArray(new PdfIndirectReference(5, 0), new PdfIndirectReference(6, 0));
        mid[new PdfName("Count")] = new PdfInteger(2);
        mid[new PdfName("MediaBox")] = new PdfArray(new PdfReal(0), new PdfReal(0), new PdfReal(200), new PdfReal(200));
        doc.AddObject(4, 0, mid);
        page5[new PdfName("Parent")] = new PdfIndirectReference(4, 0);
        page6[new PdfName("Parent")] = new PdfIndirectReference(4, 0);

        var root = new PdfDictionary();
        root[PdfName.TypeName] = new PdfName("Pages");
        root[new PdfName("Kids")] = new PdfArray(new PdfIndirectReference(4, 0));
        root[new PdfName("Count")] = new PdfInteger(2);
        doc.AddObject(1, 0, root);
        mid[new PdfName("Parent")] = new PdfIndirectReference(1, 0);

        var catalog = new PdfDictionary();
        catalog[PdfName.TypeName] = new PdfName("Catalog");
        catalog[new PdfName("Pages")] = new PdfIndirectReference(1, 0);
        doc.AddObject(3, 0, catalog);
        doc.Trailer.Root = new PdfIndirectReference(3, 0);
        return doc;
    }

    [Fact]
    public void Normalize_FlattensToSingleLevel_WithCorrectCount()
    {
        using PdfDocument doc = BuildBalanced();
        PageTreeNormalizer.Normalize(doc);

        PdfDictionary root = doc.PageTreeRootDictionary!;
        var kids = (PdfArray)root[new PdfName("Kids")];
        Assert.Equal(2, kids.Count);
        Assert.All(kids, k => Assert.IsType<PdfIndirectReference>(k));
        Assert.Equal(2, ((PdfInteger)root[new PdfName("Count")]).Value);
    }

    [Fact]
    public void Normalize_MaterializesInheritedMediaBox_OntoPages()
    {
        using PdfDocument doc = BuildBalanced();
        PageTreeNormalizer.Normalize(doc);

        foreach (PdfDictionary page in PageTreeOps.PageDicts(doc))
        {
            Assert.True(page.ContainsKey(new PdfName("MediaBox")), "page should carry materialized MediaBox");
            Assert.Equal(1, ((PdfIndirectReference)page[new PdfName("Parent")]).ObjectNumber);
        }
    }
}
