using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Document;

public class PageColorantsTests
{
    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    private static PdfDictionary Type2(double[] c0, double[] c1)
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(2));
        d.Add(new PdfName("Domain"), Reals(0, 1));
        d.Add(new PdfName("C0"), Reals(c0));
        d.Add(new PdfName("C1"), Reals(c1));
        d.Add(new PdfName("N"), new PdfReal(1));
        return d;
    }

    // A single-page document whose page /Resources/ColorSpace holds the given named definitions.
    // Uses the verified in-memory API (new PdfDocument + AddObject + Trailer Root), mirroring
    // OutputIntentsTests.DocWithOutputIntents, extended with a minimal page tree (objs: catalog=1,
    // pages=2, page=3).
    private static PdfDocument DocWithPageColorSpaces(params (string Name, PdfArray Def)[] spaces)
    {
        var colorSpace = new PdfDictionary();
        foreach ((string name, PdfArray def) in spaces) colorSpace[new PdfName(name)] = def;
        var resources = new PdfDictionary { [new PdfName("ColorSpace")] = colorSpace };

        var doc = new PdfDocument();

        var pageDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Page"),
            [new PdfName("Parent")] = new PdfIndirectReference(2, 0),
            [new PdfName("Resources")] = resources,
            [new PdfName("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
        };
        doc.AddObject(3, 0, pageDict);

        var pagesDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Pages"),
            [new PdfName("Kids")] = new PdfArray(new PdfIndirectReference(3, 0)),
            [new PdfName("Count")] = new PdfInteger(1),
        };
        doc.AddObject(2, 0, pagesDict);

        var catalog = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("Pages")] = new PdfIndirectReference(2, 0),
        };
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    [Fact]
    public void ListsDistinctSpotAndProcessColorants()
    {
        var spot = new PdfArray(
            new PdfName("Separation"), new PdfName("PANTONE 185 C"), new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [0, 1, 0, 0]));
        var procN = new PdfArray(
            new PdfName("DeviceN"),
            new PdfArray(new PdfName("Cyan"), new PdfName("Black")),
            new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [1, 0, 0, 1]));

        PdfDocument doc = DocWithPageColorSpaces(("CS0", spot), ("CS1", procN));
        IReadOnlyList<PageColorant> colorants = doc.GetPageColorants(0);

        Assert.Contains(colorants, c => c.Name == "PANTONE 185 C" && c.Kind == ColorantKind.Spot);
        Assert.Contains(colorants, c => c.Name == "Cyan" && c.Kind == ColorantKind.Process);
        Assert.Contains(colorants, c => c.Name == "Black" && c.Kind == ColorantKind.Process);
        PageColorant spotC = colorants.First(c => c.Name == "PANTONE 185 C");
        Assert.Equal("DeviceCMYK", spotC.AlternateSpace);
        Assert.NotNull(spotC.TintRamp);
        Assert.Equal(256, spotC.TintRamp!.Count);
    }

    [Fact]
    public void DedupesByName()
    {
        var spot = new PdfArray(
            new PdfName("Separation"), new PdfName("PANTONE 185 C"), new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [0, 1, 0, 0]));
        // Same colorant name under two resource keys → one entry.
        PdfDocument doc = DocWithPageColorSpaces(("CS0", spot), ("CS1", spot));
        IReadOnlyList<PageColorant> colorants = doc.GetPageColorants(0);
        Assert.Single(colorants, c => c.Name == "PANTONE 185 C");
    }

    [Fact]
    public void NoColorSpaceResources_ReturnsEmpty()
    {
        PdfDocument doc = DocWithPageColorSpaces();
        Assert.Empty(doc.GetPageColorants(0));
    }

    [Fact]
    public void AllAndNone_NotEmittedAsPlates()
    {
        var none = new PdfArray(
            new PdfName("Separation"), new PdfName("None"), new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [0, 0, 0, 0]));
        PdfDocument doc = DocWithPageColorSpaces(("CS0", none));
        Assert.DoesNotContain(doc.GetPageColorants(0), c => c.Name == "None");
    }
}
