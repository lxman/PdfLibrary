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

    // A Type 0 (Sampled) function whose /Domain declares 2 inputs, used as the tint transform of a
    // Separation (always single-input per ISO 32000-1 §8.6.6.4). PdfFunction.Create succeeds, but
    // SampledFunction.Evaluate reads a second input slot from the 1-element array BuildTintRamp supplies
    // and throws IndexOutOfRangeException. Mirrors TintRampTests.MismatchedDomainSampledFunction — see
    // that test for the direct BuildTintRamp-level repro; this one asserts the guarantee holds all the
    // way up through the public GetPageColorants API.
    private static PdfStream MismatchedDomainSampledFunction()
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(0));
        d.Add(new PdfName("Domain"), Reals(0, 1, 0, 1)); // declares 2 inputs
        d.Add(new PdfName("Range"), Reals(0, 1));
        d.Add(new PdfName("Size"), new PdfArray(new PdfInteger(2), new PdfInteger(2)));
        d.Add(new PdfName("BitsPerSample"), new PdfInteger(8));
        return new PdfStream(d, new byte[4]); // 2*2 samples * 1 output * 1 byte/sample
    }

    [Fact]
    public void ThrowingTintTransform_ColorantStillListedWithNullRamp()
    {
        var spot = new PdfArray(
            new PdfName("Separation"), new PdfName("PANTONE BROKEN"), new PdfName("DeviceCMYK"),
            MismatchedDomainSampledFunction());

        PdfDocument doc = DocWithPageColorSpaces(("CS0", spot));

        IReadOnlyList<PageColorant> colorants = doc.GetPageColorants(0); // must not throw

        PageColorant broken = Assert.Single(colorants, c => c.Name == "PANTONE BROKEN");
        Assert.Equal(ColorantKind.Spot, broken.Kind);
        Assert.Null(broken.TintRamp);
    }

    [Fact]
    public void IndexedOverSeparation_DiscoversBaseSpot()
    {
        var sep = new PdfArray(
            new PdfName("Separation"), new PdfName("PANTONE 032 C"), new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [0, 1, 0, 0]));
        var indexed = new PdfArray(
            new PdfName("Indexed"), sep, new PdfInteger(1),
            new PdfString(new byte[] { 0, 0, 0, 0, 255, 255, 255, 255 })); // lookup ignored by discovery
        PdfDocument doc = DocWithPageColorSpaces(("CS0", indexed));

        IReadOnlyList<PageColorant> colorants = doc.GetPageColorants(0);

        Assert.Contains(colorants, c => c.Name == "PANTONE 032 C" && c.Kind == ColorantKind.Spot);
    }

    // A single-page doc whose page /Resources is exactly the supplied dictionary (extends the page-tree
    // builder used by DocWithPageColorSpaces to arbitrary resources: XObject, Pattern, …).
    private static PdfDocument DocWithResources(PdfDictionary resources)
    {
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
        doc.AddObject(2, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Pages"),
            [new PdfName("Kids")] = new PdfArray(new PdfIndirectReference(3, 0)),
            [new PdfName("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("Pages")] = new PdfIndirectReference(2, 0),
        });
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    private static PdfArray Sep(string name) => new(
        new PdfName("Separation"), new PdfName(name), new PdfName("DeviceCMYK"),
        Type2([0, 0, 0, 0], [0, 1, 0, 0]));

    private static PdfStream ImageXObject(PdfObject colorSpace)
    {
        var d = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("ColorSpace")] = colorSpace,
        };
        return new PdfStream(d, new byte[] { 0 });
    }

    private static PdfStream FormXObject(PdfDictionary resources)
    {
        var d = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Form"),
            [new PdfName("BBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(1), new PdfInteger(1)),
            [new PdfName("Resources")] = resources,
        };
        return new PdfStream(d, System.Array.Empty<byte>());
    }

    private static PdfDictionary Dict(string key, PdfObject value) => new() { [new PdfName(key)] = value };

    [Fact]
    public void ImageOnlySpot_DiscoveredFromImageColorSpace()
    {
        var resources = Dict("XObject", Dict("Im0", ImageXObject(Sep("IMAGE ONLY SPOT"))));
        PdfDocument doc = DocWithResources(resources);

        Assert.Contains(doc.GetPageColorants(0), c => c.Name == "IMAGE ONLY SPOT" && c.Kind == ColorantKind.Spot);
    }

    [Fact]
    public void FormWrappedImageSpot_DiscoveredViaRecursion()
    {
        PdfStream inner = ImageXObject(Sep("PLACED LOGO SPOT"));
        PdfStream form = FormXObject(Dict("XObject", Dict("Im0", inner)));
        PdfDocument doc = DocWithResources(Dict("XObject", Dict("Fm0", form)));

        Assert.Contains(doc.GetPageColorants(0), c => c.Name == "PLACED LOGO SPOT" && c.Kind == ColorantKind.Spot);
    }

    [Fact]
    public void IndexedOverSeparationImage_DiscoveredViaRecursion()
    {
        var indexed = new PdfArray(new PdfName("Indexed"), Sep("DUOTONE SPOT"),
            new PdfInteger(1), new PdfString(new byte[] { 0, 0, 0, 0, 255, 255, 255, 255 }));
        PdfDocument doc = DocWithResources(Dict("XObject", Dict("Im0", ImageXObject(indexed))));

        Assert.Contains(doc.GetPageColorants(0), c => c.Name == "DUOTONE SPOT" && c.Kind == ColorantKind.Spot);
    }

    [Fact]
    public void PageLevelOnly_NoNestedResources_ColorantListStable()
    {
        // A page with a single page-level Separation and no XObjects/Patterns → exactly one colorant,
        // no phantom additions from the new walk (byte-identity floor).
        PdfDocument doc = DocWithResources(Dict("ColorSpace", Dict("CS0", Sep("ONLY SPOT"))));

        IReadOnlyList<PageColorant> colorants = doc.GetPageColorants(0);
        Assert.Single(colorants);
        Assert.Equal("ONLY SPOT", colorants[0].Name);
    }

    [Fact]
    public void TilingPatternOnlySpot_DiscoveredViaRecursion()
    {
        var tiling = new PdfStream(new PdfDictionary
        {
            [new PdfName("PatternType")] = new PdfInteger(1),
            [new PdfName("Resources")] = Dict("ColorSpace", Dict("CS0", Sep("PATTERN SPOT"))),
        }, System.Array.Empty<byte>());
        PdfDocument doc = DocWithResources(Dict("Pattern", Dict("P0", tiling)));

        Assert.Contains(doc.GetPageColorants(0), c => c.Name == "PATTERN SPOT" && c.Kind == ColorantKind.Spot);
    }

    [Fact]
    public void SelfReferencingForm_Terminates_AndFindsSpotOnce()
    {
        // Form obj 10 whose /Resources references (a) a Separation and (b) itself → the walk must
        // terminate (graphSeen guard on parsed docs; the depth cap is the backstop) and list the spot
        // exactly once (the `seen` set dedupes across recursion levels).
        var doc = new PdfDocument();
        var formResources = new PdfDictionary
        {
            [new PdfName("ColorSpace")] = Dict("CS0", Sep("CYCLIC SPOT")),
            [new PdfName("XObject")] = Dict("Self", new PdfIndirectReference(10, 0)),
        };
        var form = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Form"),
            [new PdfName("BBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(1), new PdfInteger(1)),
            [new PdfName("Resources")] = formResources,
        };
        doc.AddObject(10, 0, new PdfStream(form, System.Array.Empty<byte>()));
        var pageDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Page"),
            [new PdfName("Parent")] = new PdfIndirectReference(2, 0),
            [new PdfName("Resources")] = Dict("XObject", Dict("Fm0", new PdfIndirectReference(10, 0))),
            [new PdfName("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
        };
        doc.AddObject(3, 0, pageDict);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Pages"),
            [new PdfName("Kids")] = new PdfArray(new PdfIndirectReference(3, 0)),
            [new PdfName("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("Pages")] = new PdfIndirectReference(2, 0),
        });
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);

        IReadOnlyList<PageColorant> colorants = doc.GetPageColorants(0); // must return, not hang
        Assert.Single(colorants, c => c.Name == "CYCLIC SPOT");
    }
}
