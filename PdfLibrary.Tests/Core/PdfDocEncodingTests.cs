using System.Globalization;
using PdfLibrary.Core.Primitives;
using Xunit;

namespace PdfLibrary.Tests.Core;

public class PdfDocEncodingTests
{
    [Theory]
    [InlineData("Hello, World!")]
    [InlineData("Café René")]
    [InlineData("a•b—c")]
    public void Encode_RepresentableText_StaysSingleByte_AndRoundTrips(string text)
    {
        byte[] bytes = PdfDocEncoding.Encode(text);
        Assert.False(bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF);
        Assert.Equal(text, PdfDocEncoding.Decode(bytes));
    }

    [Theory]
    [InlineData("日本語のタイトル")]
    [InlineData("emoji \U0001F600 here")]
    [InlineData("Zoë Ā")]
    public void Encode_NonRepresentableText_UsesUtf16BeWithBom_AndRoundTrips(string text)
    {
        byte[] bytes = PdfDocEncoding.Encode(text);
        Assert.True(bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF);
        Assert.Equal(text, PdfDocEncoding.Decode(bytes));
    }

    [Theory]
    [InlineData(0x80, '•')]
    [InlineData(0x84, '—')]
    [InlineData(0x8D, '“')]
    [InlineData(0x92, '™')]
    [InlineData(0x93, 'ﬁ')]
    [InlineData(0xA0, '€')]
    [InlineData(0x18, '˘')]
    [InlineData(0xA1, '¡')]
    public void Decode_DivergentCodePoints_MatchAnnexD(byte b, char expected)
    {
        Assert.Equal(expected.ToString(), PdfDocEncoding.Decode(new[] { b }));
        Assert.Equal(new[] { b }, PdfDocEncoding.Encode(expected.ToString()));
    }

    [Fact]
    public void Decode_DetectsBomVariants()
    {
        Assert.Equal("Hi", PdfDocEncoding.Decode(new byte[] { 0xFE, 0xFF, 0x00, (byte)'H', 0x00, (byte)'i' }));
        Assert.Equal("Hi", PdfDocEncoding.Decode(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'H', (byte)'i' }));
        Assert.Equal("", PdfDocEncoding.Decode(new byte[] { 0xFE, 0xFF }));
    }

    [Fact]
    public void RoundTrip_IsCultureIndependent()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "fr-FR", "" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal("Café €", PdfDocEncoding.Decode(PdfDocEncoding.Encode("Café €")));
                Assert.Equal("日本語", PdfDocEncoding.Decode(PdfDocEncoding.Encode("日本語")));
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
