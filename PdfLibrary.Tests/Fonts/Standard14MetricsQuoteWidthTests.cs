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

    /// <summary>
    /// The catch-all default (<c>_ => 556</c> / <c>_ => 500</c>) is still present and still carries
    /// the whole Latin-1 range, because the by-name tables hold only ~98 ASCII names each. Removing
    /// it is L-4 — but not in isolation: <c>WidthByCode</c>'s by-code tables carry the same
    /// incompleteness (they cover only codes 32-126), and production callers chain
    /// <c>WidthByName(...) ?? WidthByCode(...)</c>, so nulling <c>WidthByName</c>'s catch-all alone
    /// would not change what a real font actually renders — the <c>WidthByCode</c> catch-all would
    /// still return the same value for any glyph outside the ASCII range. Both catch-alls need to be
    /// removed together, which is the larger scope tracked as L-4.
    ///
    /// <para>This test, however, calls <c>WidthByName</c> directly, bypassing that fallback chain — so
    /// it SHOULD fail once <c>WidthByName</c>'s own catch-all is removed, and that failure is the
    /// signal to delete it.</para>
    /// </summary>
    [Theory]
    [InlineData("Helvetica", 556)]
    [InlineData("Times-Roman", 500)]
    public void UnknownGlyphNamesStillHitTheDocumentedCatchAll(string baseFont, double catchAll)
    {
        Assert.Equal(catchAll, Standard14Metrics.WidthByName(baseFont, "__notAGlyphName__"));
        Assert.Equal(catchAll, Standard14Metrics.WidthByName(baseFont, "eacute"));
    }
}
