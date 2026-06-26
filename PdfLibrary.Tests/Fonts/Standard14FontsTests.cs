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

    [Fact]
    public void TextFamilies_IncludeMacOsLiteral_BeforeDejaVuFallback()
    {
        // ToList() because SubstituteFileBaseNames returns IReadOnlyList<string>, which has no IndexOf.
        var helv = Standard14Fonts.SubstituteFileBaseNames("Helvetica").ToList();
        var times = Standard14Fonts.SubstituteFileBaseNames("Times-Bold").ToList();
        var cour = Standard14Fonts.SubstituteFileBaseNames("Courier-Oblique").ToList();

        Assert.True(helv.IndexOf("Helvetica") >= 0 && helv.IndexOf("Helvetica") < helv.IndexOf("DejaVuSans"));
        Assert.True(times.IndexOf("Times") >= 0 && times.IndexOf("Times") < times.IndexOf("DejaVuSerif-Bold"));
        Assert.True(cour.IndexOf("Courier") >= 0 && cour.IndexOf("Courier") < cour.IndexOf("DejaVuSansMono-Oblique"));
    }

    [Fact]
    public void TimesNewRoman_And_CourierNew_Aliases_Resolve()
    {
        Assert.Equal(
            Standard14Fonts.SubstituteFileBaseNames("Times-Roman"),
            Standard14Fonts.SubstituteFileBaseNames("TimesNewRoman"));
        Assert.Equal(
            Standard14Fonts.SubstituteFileBaseNames("Courier"),
            Standard14Fonts.SubstituteFileBaseNames("CourierNew"));
    }
}
