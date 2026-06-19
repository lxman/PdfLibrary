using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class PdfDocumentCreateEmptyTests
{
    [Fact]
    public void CreateEmpty_HasCatalogAndEmptyPageTree()
    {
        using var doc = PdfDocument.CreateEmpty();
        Assert.NotNull(doc.CatalogDictionary);
        Assert.NotNull(doc.PageTreeRootDictionary);
        Assert.Equal(0, doc.PageCount);
    }

    [Fact]
    public void CreateEmpty_RoundTripsThroughSaveAndLoad()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.CreateEmpty())
            doc.Save(ms);
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(0, reloaded.PageCount);
    }
}
