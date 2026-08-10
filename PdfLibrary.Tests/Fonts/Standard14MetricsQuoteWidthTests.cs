using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Pins the four quote-family glyph widths for all six by-name Standard-14 faces against the
/// published AFM values (URW base-35 NimbusSans-*/NimbusRoman-*, which carry Helvetica and Times
/// metrics).
///
/// <para>These exist because the tables aliased glyph names that share a CHARACTER CODE but not an
/// AFM width — <c>"quotesingle" or "quoteright" => 191</c> is correct for quotesingle and wrong for
/// quoteright. Each such line was right in one arm and wrong in the other, so this pins BOTH arms of
/// every pair: fixing the wrong arm must not disturb the right one.</para>
///
/// <para>Asserted against the AFM, never against what a substitute font happens to report. The
/// audit that found this initially scored the engine as correct and Liberation as wrong, which was
/// backwards — the AFM is the only authority here.</para>
/// </summary>
public class Standard14MetricsQuoteWidthTests
{
    [Theory]
    // face,             glyphName,       expected AFM width
    [InlineData("Helvetica", "quotesingle", 191)]
    [InlineData("Helvetica", "quoteright", 222)]   // was 191 — conflated with quotesingle
    [InlineData("Helvetica", "grave", 333)]
    [InlineData("Helvetica", "quoteleft", 222)]    // was 333 — conflated with grave
    [InlineData("Helvetica-Bold", "quotesingle", 238)]
    [InlineData("Helvetica-Bold", "quoteright", 278)]   // was 238
    [InlineData("Helvetica-Bold", "grave", 333)]
    [InlineData("Helvetica-Bold", "quoteleft", 278)]    // was 333
    [InlineData("Times-Roman", "quotesingle", 180)]     // was 333
    [InlineData("Times-Roman", "quoteright", 333)]
    [InlineData("Times-Roman", "grave", 333)]
    [InlineData("Times-Roman", "quoteleft", 333)]
    [InlineData("Times-Bold", "quotesingle", 278)]      // was 333
    [InlineData("Times-Bold", "quoteright", 333)]
    [InlineData("Times-Bold", "grave", 333)]
    [InlineData("Times-Bold", "quoteleft", 333)]
    [InlineData("Times-Italic", "quotesingle", 214)]    // was 333
    [InlineData("Times-Italic", "quoteright", 333)]
    [InlineData("Times-Italic", "grave", 333)]
    [InlineData("Times-Italic", "quoteleft", 333)]
    [InlineData("Times-BoldItalic", "quotesingle", 278)]  // was 333
    [InlineData("Times-BoldItalic", "quoteright", 333)]
    [InlineData("Times-BoldItalic", "grave", 333)]
    [InlineData("Times-BoldItalic", "quoteleft", 333)]
    public void QuoteGlyphWidthsMatchTheAfm(string baseFont, string glyphName, double expected)
    {
        Assert.Equal(expected, Standard14Metrics.WidthByName(baseFont, glyphName));
    }
}
