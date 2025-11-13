using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests;

public class PdfFontTests
{
    [Fact]
    public void Create_Type1Font_ReturnsCorrectType()
    {
        var fontDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type1"),
            [new PdfName("BaseFont")] = new PdfName("Helvetica")
        };

        var font = PdfFont.Create(fontDict);

        Assert.NotNull(font);
        Assert.IsType<Type1Font>(font);
        Assert.Equal(PdfFontType.Type1, font.FontType);
        Assert.Equal("Helvetica", font.BaseFont);
    }

    [Fact]
    public void Create_TrueTypeFont_ReturnsCorrectType()
    {
        var fontDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("TrueType"),
            [new PdfName("BaseFont")] = new PdfName("Arial")
        };

        var font = PdfFont.Create(fontDict);

        Assert.NotNull(font);
        Assert.IsType<TrueTypeFont>(font);
        Assert.Equal(PdfFontType.TrueType, font.FontType);
        Assert.Equal("Arial", font.BaseFont);
    }

    [Fact]
    public void Create_Type3Font_ReturnsCorrectType()
    {
        var fontDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type3"),
            [new PdfName("FontBBox")] = new PdfArray
            {
                new PdfInteger(0),
                new PdfInteger(0),
                new PdfInteger(1000),
                new PdfInteger(1000)
            },
            [new PdfName("FontMatrix")] = new PdfArray
            {
                new PdfReal(0.001),
                new PdfInteger(0),
                new PdfInteger(0),
                new PdfReal(0.001),
                new PdfInteger(0),
                new PdfInteger(0)
            }
        };

        var font = PdfFont.Create(fontDict);

        Assert.NotNull(font);
        Assert.IsType<Type3Font>(font);
        Assert.Equal(PdfFontType.Type3, font.FontType);
    }

    [Fact]
    public void Create_Type0Font_ReturnsCorrectType()
    {
        var fontDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type0"),
            [new PdfName("BaseFont")] = new PdfName("HeiseiMin-W3"),
            [new PdfName("Encoding")] = new PdfName("Identity-H")
        };

        var font = PdfFont.Create(fontDict);

        Assert.NotNull(font);
        Assert.IsType<Type0Font>(font);
        Assert.Equal(PdfFontType.Type0, font.FontType);
    }

    [Fact]
    public void Type1Font_StandardFont_ReturnsApproximateWidth()
    {
        var fontDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type1"),
            [new PdfName("BaseFont")] = new PdfName("Courier")
        };

        var font = PdfFont.Create(fontDict) as Type1Font;
        Assert.NotNull(font);

        // Courier is monospace, should return 600
        double width = font.GetCharacterWidth(65); // 'A'
        Assert.True(width > 0);
    }

    [Fact]
    public void Type1Font_WithWidthsArray_ReturnsSpecifiedWidth()
    {
        var fontDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type1"),
            [new PdfName("BaseFont")] = new PdfName("CustomFont"),
            [new PdfName("FirstChar")] = new PdfInteger(65),
            [new PdfName("LastChar")] = new PdfInteger(67),
            [new PdfName("Widths")] = new PdfArray
            {
                new PdfInteger(700), // Width for 'A'
                new PdfInteger(650), // Width for 'B'
                new PdfInteger(600)  // Width for 'C'
            }
        };

        var font = PdfFont.Create(fontDict);
        Assert.NotNull(font);

        Assert.Equal(700, font.GetCharacterWidth(65)); // 'A'
        Assert.Equal(650, font.GetCharacterWidth(66)); // 'B'
        Assert.Equal(600, font.GetCharacterWidth(67)); // 'C'
    }

    [Fact]
    public void DecodeCharacter_WithToUnicode_UsesToUnicode()
    {
        byte[] toUnicodeData = System.Text.Encoding.ASCII.GetBytes(@"
beginbfchar
<41> <0391>
endbfchar
");

        var toUnicodeStream = new PdfStream(
            new PdfDictionary(),
            toUnicodeData
        );

        var fontDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type1"),
            [new PdfName("BaseFont")] = new PdfName("TestFont"),
            [new PdfName("ToUnicode")] = toUnicodeStream
        };

        var font = PdfFont.Create(fontDict);
        Assert.NotNull(font);

        // Character code 0x41 should map to Greek Alpha via ToUnicode
        string decoded = font.DecodeCharacter(0x41);
        Assert.Equal("Α", decoded);
    }

    [Fact]
    public void DecodeCharacter_WithEncoding_UsesEncoding()
    {
        var encodingDict = new PdfDictionary
        {
            [new PdfName("BaseEncoding")] = new PdfName("StandardEncoding"),
            [new PdfName("Differences")] = new PdfArray
            {
                new PdfInteger(65),
                new PdfName("Alpha")
            }
        };

        var fontDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type1"),
            [new PdfName("BaseFont")] = new PdfName("TestFont"),
            [new PdfName("Encoding")] = encodingDict
        };

        var font = PdfFont.Create(fontDict);
        Assert.NotNull(font);

        // Character code 65 should map to Greek Alpha via encoding
        string decoded = font.DecodeCharacter(65);
        Assert.Equal("Α", decoded);
    }

    [Fact]
    public void Type3Font_HasFontMatrix()
    {
        var fontDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type3"),
            [new PdfName("FontBBox")] = new PdfArray
            {
                new PdfInteger(0),
                new PdfInteger(0),
                new PdfInteger(1000),
                new PdfInteger(1000)
            },
            [new PdfName("FontMatrix")] = new PdfArray
            {
                new PdfReal(0.001),
                new PdfInteger(0),
                new PdfInteger(0),
                new PdfReal(0.001),
                new PdfInteger(0),
                new PdfInteger(0)
            }
        };

        var font = PdfFont.Create(fontDict) as Type3Font;
        Assert.NotNull(font);

        double[] matrix = font.FontMatrix;
        Assert.NotNull(matrix);
        Assert.Equal(6, matrix.Length);
        Assert.Equal(0.001, matrix[0]);
    }
}
