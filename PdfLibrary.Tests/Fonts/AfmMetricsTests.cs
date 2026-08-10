using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// The parser must key on the AFM's N (name) field and ignore C entirely. AFM C codes are
/// StandardEncoding — Helvetica's file says `C 39 ; WX 222 ; N quoteright` where WinAnsi 39 is
/// quotesingle at 191. Mixing those two readings is precisely the conflation L-1 had to fix, so
/// these tests pin the name-keyed reading directly.
/// </summary>
public class AfmMetricsTests
{
    [Fact]
    public void ParsesEveryGlyphInTheFace()
    {
        // 315 is the Core-14 glyph count for the text faces, measured from the vendored file.
        Assert.Equal(315, AfmMetrics.ForFace("Helvetica")!.Count);
        Assert.Equal(190, AfmMetrics.ForFace("Symbol")!.Count);
    }

    [Theory]
    // Straight from the vendored AFMs. quoteleft/quoteright are the L-1 values, re-derived here
    // from the data rather than restated from the plan that fixed them.
    [InlineData("Helvetica", "quoteright", 222)]
    [InlineData("Helvetica", "quoteleft", 222)]
    [InlineData("Helvetica", "quotesingle", 191)]
    [InlineData("Times-Roman", "quotesingle", 180)]
    public void ReadsTheNameKeyedWidth(string face, string glyph, double expected)
    {
        Assert.Equal(expected, AfmMetrics.ForFace(face)![glyph]);
    }

    [Theory]
    // `C -1` rows: glyphs with no StandardEncoding code. These are the Latin-1 coverage the
    // hand-written tables never had, and a parser that skips C -1 silently loses all of them.
    [InlineData("eacute", 556)]
    [InlineData("copyright", 737)]
    public void IncludesUnencodedGlyphs(string glyph, double expected)
    {
        Assert.Equal(expected, AfmMetrics.ForFace("Helvetica")![glyph]);
    }

    [Theory]
    // The glyphs the old catch-all got wrong: a flat 556 for Helvetica against these real values.
    [InlineData("bullet", 350)]
    [InlineData("emdash", 1000)]
    [InlineData("quotedblleft", 333)]
    public void CarriesTheGlyphsTheCatchAllFabricated(string glyph, double expected)
    {
        Assert.Equal(expected, AfmMetrics.ForFace("Helvetica")![glyph]);
    }

    [Fact]
    public void ReturnsNullForAFaceWithNoVendoredAfm()
    {
        // Courier is uniformly 600 and deliberately not vendored; Standard14Metrics answers it with
        // a flat arm. The parser must say "I have nothing" rather than invent a table.
        Assert.Null(AfmMetrics.ForFace("Courier"));
        Assert.Null(AfmMetrics.ForFace("NotAFace"));
    }

    [Fact]
    public void TheSameInstanceIsReturnedOnEveryCall()
    {
        // Parsed once, cached. A per-call parse would re-decompress 77 KB on every glyph lookup.
        Assert.Same(AfmMetrics.ForFace("Helvetica"), AfmMetrics.ForFace("Helvetica"));
    }
}
