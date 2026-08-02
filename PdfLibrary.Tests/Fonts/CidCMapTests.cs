using System.Text;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>B-1: CidCMap parses the CID-keyed CMap dialect (cidchar/cidrange — CID operands are
/// DECIMAL, unlike the bf* dialect's hex destinations) used by embedded Type0 /Encoding streams.</summary>
public class CidCMapTests
{
    private static CidCMap Parse(string text) => CidCMap.Parse(Encoding.ASCII.GetBytes(text));

    [Fact]
    public void CidChar_MapsSingleCodes()
    {
        CidCMap m = Parse("2 begincidchar\n<0041> 34\n<3042> 843\nendcidchar\n");
        Assert.Equal(34, m.MapCodeToCid(0x0041));
        Assert.Equal(843, m.MapCodeToCid(0x3042));
        Assert.Null(m.MapCodeToCid(0x0042));
    }

    [Fact]
    public void CidRange_IncrementsAcrossTheRange()
    {
        CidCMap m = Parse("1 begincidrange\n<0020> <0024> 1\nendcidrange\n");
        Assert.Equal(1, m.MapCodeToCid(0x20));
        Assert.Equal(3, m.MapCodeToCid(0x22));
        Assert.Equal(5, m.MapCodeToCid(0x24));
        Assert.Null(m.MapCodeToCid(0x25));
    }

    [Fact]
    public void MultipleBlocks_AllParsed()
    {
        CidCMap m = Parse(
            "1 begincidchar\n<00> 7\nendcidchar\n" +
            "1 begincidrange\n<10> <11> 100\nendcidrange\n" +
            "1 begincidchar\n<20> 200\nendcidchar\n");
        Assert.Equal(7, m.MapCodeToCid(0x00));
        Assert.Equal(101, m.MapCodeToCid(0x11));
        Assert.Equal(200, m.MapCodeToCid(0x20));
        Assert.Equal(4, m.MappingCount);
    }

    [Fact]
    public void UseCMap_IsRecordedNotFollowed()
    {
        CidCMap m = Parse("/Adobe-Japan1-UCS2 usecmap\n1 begincidchar\n<41> 34\nendcidchar\n");
        Assert.Equal("Adobe-Japan1-UCS2", m.UseCMapName);
        Assert.Equal(34, m.MapCodeToCid(0x41));   // local operators still parse
    }

    [Fact]
    public void MalformedInput_DegradesToEmpty()
    {
        Assert.Equal(0, Parse("not a cmap at all").MappingCount);
        Assert.Equal(0, Parse("begincidrange <zz> <yy> x endcidrange").MappingCount);
        Assert.Equal(0, CidCMap.Parse([]).MappingCount);
    }

    [Fact]
    public void AbsurdRange_IsSkippedNotMaterialized()
    {
        // A corrupt hi value must not allocate 16M entries; ranges wider than 0xFFFF are dropped.
        CidCMap m = Parse("1 begincidrange\n<000000> <FFFFFF> 1\nendcidrange\n");
        Assert.Equal(0, m.MappingCount);
    }
}
