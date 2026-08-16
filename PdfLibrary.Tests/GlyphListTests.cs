using PdfLibrary.Fonts;

namespace PdfLibrary.Tests;

public class GlyphListTests
{
    [Theory]
    [InlineData("A", "A")]
    [InlineData("B", "B")]
    [InlineData("a", "a")]
    [InlineData("z", "z")]
    [InlineData("space", " ")]
    [InlineData("period", ".")]
    [InlineData("comma", ",")]
    public void GetUnicode_BasicGlyphs_ReturnsCorrectUnicode(string glyphName, string expectedUnicode)
    {
        string? unicode = GlyphList.GetUnicode(glyphName);
        Assert.Equal(expectedUnicode, unicode);
    }

    [Theory]
    [InlineData("Agrave", "À")]
    [InlineData("Aacute", "Á")]
    [InlineData("Ccedilla", "Ç")]
    [InlineData("eacute", "é")]
    [InlineData("ntilde", "ñ")]
    public void GetUnicode_AccentedCharacters_ReturnsCorrectUnicode(string glyphName, string expectedUnicode)
    {
        string? unicode = GlyphList.GetUnicode(glyphName);
        Assert.Equal(expectedUnicode, unicode);
    }

    [Theory]
    [InlineData("Alpha", "Α")]
    [InlineData("Beta", "Β")]
    [InlineData("Gamma", "Γ")]
    [InlineData("alpha", "α")]
    [InlineData("beta", "β")]
    [InlineData("gamma", "γ")]
    public void GetUnicode_GreekLetters_ReturnsCorrectUnicode(string glyphName, string expectedUnicode)
    {
        string? unicode = GlyphList.GetUnicode(glyphName);
        Assert.Equal(expectedUnicode, unicode);
    }

    [Theory]
    [InlineData("endash", "–")]
    [InlineData("emdash", "—")]
    [InlineData("bullet", "•")]
    [InlineData("ellipsis", "…")]
    public void GetUnicode_Punctuation_ReturnsCorrectUnicode(string glyphName, string expectedUnicode)
    {
        string? unicode = GlyphList.GetUnicode(glyphName);
        Assert.Equal(expectedUnicode, unicode);
    }

    [Theory]
    [InlineData("fi")]
    [InlineData("fl")]
    public void GetUnicode_Ligatures_ReturnsNonNull(string glyphName)
    {
        string? unicode = GlyphList.GetUnicode(glyphName);
        Assert.NotNull(unicode);
        Assert.NotEmpty(unicode);
    }

    [Theory]
    [InlineData("uni0041", "A")]  // Unicode for 'A'
    [InlineData("uni0061", "a")]  // Unicode for 'a'
    [InlineData("uni00A9", "©")]  // Unicode for copyright
    [InlineData("uni20AC", "€")]  // Unicode for Euro sign
    public void GetUnicode_UniFormat_ReturnsCorrectUnicode(string glyphName, string expectedUnicode)
    {
        string? unicode = GlyphList.GetUnicode(glyphName);
        Assert.Equal(expectedUnicode, unicode);
    }

    [Fact]
    public void GetUnicode_UnknownGlyph_ReturnsNull()
    {
        string? unicode = GlyphList.GetUnicode("UnknownGlyphName");
        Assert.Null(unicode);
    }

    [Fact]
    public void GetUnicode_InvalidUniFormat_ReturnsNull()
    {
        // Too short for uni format
        string? unicode = GlyphList.GetUnicode("uni00");
        Assert.Null(unicode);
    }

    [Fact]
    public void GetUnicode_NonHexUniFormat_ReturnsNull()
    {
        // Invalid hex characters
        string? unicode = GlyphList.GetUnicode("uniXYZW");
        Assert.Null(unicode);
    }

    // Issue 28 prerequisite: the eight spacing-modifier accents were absent from the AGL table, so
    // SetCharacterName could never derive their Unicode and DecodeCharacter fell to Latin-1.
    [Theory]
    [InlineData("circumflex", "ˆ")]
    [InlineData("tilde", "˜")]
    [InlineData("breve", "˘")]
    [InlineData("dotaccent", "˙")]
    [InlineData("ring", "˚")]
    [InlineData("hungarumlaut", "˝")]
    [InlineData("ogonek", "˛")]
    [InlineData("caron", "ˇ")]
    public void Spacing_accents_round_trip(string name, string unicode)
    {
        Assert.Equal(unicode, GlyphList.GetUnicode(name));
        Assert.Equal(name, GlyphList.GetGlyphName(unicode));
    }

    // Task 10 (spec Amendment 2): the hand-built table lacked most of the AGL, causing an
    // unmapped-Unicode false positive on names outside the ~350-entry hand subset (afii*, angle,
    // aleph, universal, ...). Expected values verified against the vendored glyphlist.txt itself
    // (pinned commit 4036a9c, see Resources/Agl/LICENSE.md), not memory. Note: the AGL maps
    // afii10034 to U+0420 CYRILLIC CAPITAL LETTER ER (Р), not U+0424 EF (Ф) as might be assumed.
    [Theory]
    [InlineData("afii10034", "Р")]  // Р - CYRILLIC CAPITAL LETTER ER
    [InlineData("universal", "∀")]  // ∀
    [InlineData("aleph", "ℵ")]      // ℵ
    [InlineData("angle", "∠")]      // ∠
    public void GetUnicode_AglSupplementNames_ReturnsCorrectUnicode(string glyphName, string expectedUnicode)
    {
        string? unicode = GlyphList.GetUnicode(glyphName);
        Assert.Equal(expectedUnicode, unicode);
    }

    // Multi-codepoint AGL entry (Hebrew dalet + hataf patah combining mark) round-trips its full
    // string value. Verified against glyphlist.txt: dalethatafpatah;05D3 05B2.
    [Fact]
    public void GetUnicode_MultiCodepointAglEntry_RoundTripsFullString()
    {
        string expected = char.ConvertFromUtf32(0x05D3) + char.ConvertFromUtf32(0x05B2);
        Assert.Equal(expected, GlyphList.GetUnicode("dalethatafpatah"));
        Assert.Equal("dalethatafpatah", GlyphList.GetGlyphName(expected));
    }

    // Stability pins: the hand table stays FIRST, so completing it from the AGL must not churn any
    // existing forward entry or any existing first-name-wins reverse-map choice.
    [Fact]
    public void GetGlyphName_QuoteSingle_StableAfterAglCompletion()
    {
        Assert.Equal("quotesingle", GlyphList.GetGlyphName("'"));
    }

    [Fact]
    public void GetGlyphName_Circumflex_StableAfterAglCompletion()
    {
        Assert.Equal("circumflex", GlyphList.GetGlyphName("ˆ"));
    }

    [Fact]
    public void GetUnicode_QuoteRight_StableAfterAglCompletion()
    {
        Assert.Equal("’", GlyphList.GetUnicode("quoteright"));
    }
}
