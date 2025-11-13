using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests;

public class FontEncodingTests
{
    [Fact]
    public void StandardEncoding_DecodesBasicAscii()
    {
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding("StandardEncoding");

        // Test basic ASCII characters
        Assert.Equal("A", encoding.DecodeCharacter(65));
        Assert.Equal("Z", encoding.DecodeCharacter(90));
        Assert.Equal("a", encoding.DecodeCharacter(97));
        Assert.Equal("z", encoding.DecodeCharacter(122));
        Assert.Equal("0", encoding.DecodeCharacter(48));
        Assert.Equal("9", encoding.DecodeCharacter(57));
        Assert.Equal(" ", encoding.DecodeCharacter(32));
    }

    [Fact]
    public void WinAnsiEncoding_DecodesExtendedCharacters()
    {
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");

        // Test extended ASCII characters
        Assert.Equal("©", encoding.DecodeCharacter(169)); // Copyright
        Assert.Equal("®", encoding.DecodeCharacter(174)); // Registered
        Assert.Equal("™", encoding.DecodeCharacter(153)); // Trademark
    }

    [Fact]
    public void MacRomanEncoding_DecodesCorrectly()
    {
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding("MacRomanEncoding");

        // Test basic ASCII
        Assert.Equal("A", encoding.DecodeCharacter(65));
        Assert.Equal("a", encoding.DecodeCharacter(97));
    }

    [Fact]
    public void SymbolEncoding_DecodesGreekLetters()
    {
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding("SymbolEncoding");

        // Test symbol font characters
        Assert.Equal(" ", encoding.DecodeCharacter(32)); // Space
        Assert.Equal("Α", encoding.DecodeCharacter(65)); // Alpha
        Assert.Equal("Β", encoding.DecodeCharacter(66)); // Beta
        Assert.Equal("Γ", encoding.DecodeCharacter(71)); // Gamma
    }

    [Fact]
    public void CustomEncoding_AppliesDifferences()
    {
        var encodingDict = new PdfDictionary
        {
            [new PdfName("BaseEncoding")] = new PdfName("StandardEncoding"),
            [new PdfName("Differences")] = new PdfArray
            {
                new PdfInteger(65), // Start at character code 65
                new PdfName("Alpha"), // Replace 'A' with Greek Alpha
                new PdfName("Beta")   // Replace 'B' with Greek Beta
            }
        };

        PdfFontEncoding encoding = PdfFontEncoding.FromDictionary(encodingDict);

        // Test that differences were applied
        Assert.Equal("Α", encoding.DecodeCharacter(65)); // Greek Alpha instead of 'A'
        Assert.Equal("Β", encoding.DecodeCharacter(66)); // Greek Beta instead of 'B'
        Assert.Equal("C", encoding.DecodeCharacter(67)); // 'C' unchanged
    }

    [Fact]
    public void Encoding_FallsBackToLatin1()
    {
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding("StandardEncoding");

        // Test character with no mapping falls back to Latin-1
        string result = encoding.DecodeCharacter(200);
        Assert.NotEmpty(result);
    }

    [Theory]
    [InlineData("StandardEncoding")]
    [InlineData("WinAnsiEncoding")]
    [InlineData("MacRomanEncoding")]
    [InlineData("SymbolEncoding")]
    [InlineData("ZapfDingbatsEncoding")]
    public void GetStandardEncoding_ReturnsNonNull(string encodingName)
    {
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding(encodingName);
        Assert.NotNull(encoding);
    }

    [Fact]
    public void GetStandardEncoding_UnknownName_ReturnsStandardEncoding()
    {
        PdfFontEncoding encoding = PdfFontEncoding.GetStandardEncoding("UnknownEncoding");
        Assert.NotNull(encoding);

        // Should decode basic ASCII
        Assert.Equal("A", encoding.DecodeCharacter(65));
    }
}
