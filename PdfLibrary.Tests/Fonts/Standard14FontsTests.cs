using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class Standard14FontsTests
{
    [Fact]
    public void Helvetica_MapsToSansCandidates_InPriorityOrder()
    {
        IReadOnlyList<string> c = Standard14Fonts.SubstituteFileBaseNames("Helvetica");
        Assert.Equal("arial", c[0]);
        Assert.Contains("LiberationSans-Regular", c);
        Assert.Contains("NimbusSans-Regular", c);
        Assert.Contains("DejaVuSans", c);
    }

    [Fact]
    public void BoldStyle_SelectsBoldFiles()
    {
        IReadOnlyList<string> c = Standard14Fonts.SubstituteFileBaseNames("Helvetica-Bold");
        Assert.Equal("arialbd", c[0]);
        Assert.Contains("LiberationSans-Bold", c);
    }

    [Fact]
    public void SubsetPrefix_IsStripped()
    {
        IReadOnlyList<string> c = Standard14Fonts.SubstituteFileBaseNames("ABCDEF+Times-Italic");
        Assert.Contains("LiberationSerif-Italic", c);
    }

    [Fact]
    public void ArialAlias_MapsToHelveticaSubstitutes()
    {
        Assert.Equal(
            Standard14Fonts.SubstituteFileBaseNames("Helvetica"),
            Standard14Fonts.SubstituteFileBaseNames("Arial"));
    }

    [Fact]
    public void Symbol_And_ZapfDingbats_MapToUrwFiles()
    {
        Assert.Contains("StandardSymbolsPS", Standard14Fonts.SubstituteFileBaseNames("Symbol"));
        Assert.Contains("D050000L", Standard14Fonts.SubstituteFileBaseNames("ZapfDingbats"));
    }

    [Fact]
    public void UnknownFont_ReturnsEmpty()
    {
        Assert.Empty(Standard14Fonts.SubstituteFileBaseNames("Wingdings3"));
    }
}
