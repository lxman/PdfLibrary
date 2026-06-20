using PdfLibrary.Core.Primitives;
using Xunit;

namespace PdfLibrary.Tests.Core;

public class PdfStringTextTests
{
    [Fact]
    public void FromText_Ascii_RoundTripsViaGetText_AndIsLiteral()
    {
        PdfString s = PdfString.FromText("Hello");
        Assert.Equal("Hello", s.GetText());
        Assert.StartsWith("(", s.ToPdfString());
    }

    [Fact]
    public void FromText_Cjk_RoundTripsViaGetText_AndIsHex()
    {
        PdfString s = PdfString.FromText("日本語");
        Assert.Equal("日本語", s.GetText());
        Assert.StartsWith("<FEFF", s.ToPdfString());
    }

    [Fact]
    public void FromByteLiteral_MatchesLatin1Bytes()
    {
        PdfString s = PdfString.FromByteLiteral("ID-token");
        Assert.Equal(System.Text.Encoding.Latin1.GetBytes("ID-token"), s.Bytes);
    }
}
