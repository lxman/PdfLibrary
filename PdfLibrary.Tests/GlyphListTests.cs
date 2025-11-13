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
}
