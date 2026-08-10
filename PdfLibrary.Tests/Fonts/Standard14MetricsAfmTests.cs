using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// The widths the hand-written tables got wrong or never had. Every expected value comes from the
/// vendored Adobe AFM, which is the authority — not from any font file, and not from the previous
/// implementation.
/// </summary>
public class Standard14MetricsAfmTests
{
    [Theory]
    // Was a fabricated 556 (Helvetica) / 500 (Times) from the catch-all.
    [InlineData("Helvetica", "bullet", 350)]
    [InlineData("Helvetica", "emdash", 1000)]
    [InlineData("Helvetica", "eacute", 556)]      // the catch-all happened to get this one right
    [InlineData("Helvetica", "copyright", 737)]
    [InlineData("Times-Roman", "bullet", 350)]
    [InlineData("Times-Roman", "emdash", 1000)]
    // The hyphen/minus conflation: one arm served both, at the hyphen width. minus carries the
    // PLUS width in every Core-14 face.
    [InlineData("Helvetica", "hyphen", 333)]
    [InlineData("Helvetica", "minus", 584)]
    [InlineData("Helvetica-Bold", "minus", 584)]
    [InlineData("Times-Roman", "minus", 564)]
    [InlineData("Times-Bold", "minus", 570)]
    [InlineData("Times-Italic", "minus", 675)]
    // NOT 570. `minus` equals `plus` in five faces but NOT this one — Times-BoldItalic plus is 570
    // and minus is 606. Verified against two independent AFM sources. Do not "correct" this to 570
    // on the strength of the pattern; the pattern is what is wrong.
    [InlineData("Times-BoldItalic", "minus", 606)]
    // Symbol and ZapfDingbats returned null outright before — no metrics at all.
    [InlineData("Symbol", "alpha", 631)]
    [InlineData("ZapfDingbats", "a9", 577)]
    public void WidthByNameMatchesTheAfm(string baseFont, string glyph, double expected)
    {
        Assert.Equal(expected, Standard14Metrics.WidthByName(baseFont, glyph));
    }

    [Theory]
    // Oblique faces alias to their upright counterparts — verified width-identical in the AFMs.
    [InlineData("Helvetica-Oblique", "bullet", 350)]
    [InlineData("Helvetica-BoldOblique", "minus", 584)]
    public void ObliqueFacesShareTheUprightWidths(string baseFont, string glyph, double expected)
    {
        Assert.Equal(expected, Standard14Metrics.WidthByName(baseFont, glyph));
    }

    [Theory]
    // Courier is monospaced: every glyph 600, no AFM vendored.
    [InlineData("Courier", "bullet")]
    [InlineData("Courier-Bold", "eacute")]
    [InlineData("Courier-BoldOblique", "minus")]
    public void CourierIsFlatSixHundred(string baseFont, string glyph)
    {
        Assert.Equal(600, Standard14Metrics.WidthByName(baseFont, glyph));
    }

    [Fact]
    public void AnUnknownGlyphNameNowReturnsNullInsteadOfAFabricatedWidth()
    {
        // THE headline behaviour change. The catch-alls returned 556/500 for any unrecognised name,
        // which meant 143 of 214 WinAnsi names got a number nobody measured.
        Assert.Null(Standard14Metrics.WidthByName("Helvetica", "__notAGlyphName__"));
        Assert.Null(Standard14Metrics.WidthByName("Times-Roman", "__notAGlyphName__"));
    }

    [Fact]
    public void AnUnknownBaseFontStillReturnsNull()
    {
        Assert.Null(Standard14Metrics.WidthByName("FooCorpSans", "A"));
        Assert.Null(Standard14Metrics.WidthByName("Helvetica", null));
    }
}
