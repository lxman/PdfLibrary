using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Tests.Content;

public class TextExtractionQualityTests
{
    [Fact]
    public void ExtractTextWithQuality_ReadableLatinText_Passes()
    {
        TextExtractionResult result = Page("A normal, searchable contract text layer.").ExtractTextWithQuality();

        Assert.True(result.IsLikelyReadable);
        Assert.Equal("A normal, searchable contract text layer.", result.Text);
        Assert.Equal(1, result.PrintableAsciiRatio);
        Assert.Equal(0, result.UnexpectedControlCharacterCount);
        Assert.Equal(0, result.ReplacementCharacterCount);
    }

    [Fact]
    public void ExtractTextWithQuality_RawGlyphIndices_Fails()
    {
        string rawGlyphIndices = new(Enumerable.Range(0, 32).Select(i => (char)i).ToArray());

        var result = new TextExtractionResult(rawGlyphIndices);

        Assert.False(result.IsLikelyReadable);
        Assert.True(result.UnexpectedControlCharacterCount > 0);
    }

    [Fact]
    public void ExtractTextWithQuality_BrokenUnicodeMapOutput_Fails()
    {
        var result = new TextExtractionResult(new string('\uFFFD', 100));

        Assert.False(result.IsLikelyReadable);
        Assert.Equal(100, result.ReplacementCharacterCount);
        Assert.Equal(0, result.PrintableAsciiRatio);
    }

    private static PdfPage Page(string text)
    {
        byte[] content = Encoding.Latin1.GetBytes($"BT /F1 12 Tf 0 0 Td ({Escape(text)}) Tj ET");
        return new PdfPage(new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Page"),
            [new PdfName("MediaBox")] = new PdfArray
            {
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
            },
            [new PdfName("Contents")] = new PdfStream(new PdfDictionary(), content),
        });
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
