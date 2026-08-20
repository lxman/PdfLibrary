using System.Text;
using PdfLibrary.Fonts;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// ISO 19005-2 clause 6.1.13 test 10 bounds the largest CID a CMap DECLARES at 65535 (veraPDF
/// object <c>CMapFile</c>, property <c>maximalCID</c>). These pin
/// <see cref="CidCMap.MaxDeclaredCid"/>, the cheap scan that answers it without materialising the
/// code→CID dictionary <see cref="CidCMap.Parse"/> builds.
/// </summary>
public class CidCMapMaxCidTests
{
    private static long? Max(string cmap) => CidCMap.MaxDeclaredCid(Encoding.ASCII.GetBytes(cmap));

    [Fact]
    public void A_range_reports_its_top_CID_not_its_start()
    {
        // Exactly the shape both corpus fixtures carry: <3f00> <3fff> 65536 → 65536 + 0xFF = 65791.
        Assert.Equal(65791, Max("1 begincidrange\n<3f00> <3fff> 65536\nendcidrange"));
    }

    [Fact]
    public void The_maximum_is_taken_across_every_range()
    {
        Assert.Equal(65791, Max(
            "3 begincidrange\n"
            + "<0000> <00ff> 0\n"
            + "<3f00> <3fff> 65536\n"
            + "<2100> <21ff> 8448\n"
            + "endcidrange"));
    }

    [Fact]
    public void A_cidchar_entry_counts_too()
    {
        Assert.Equal(70000, Max("1 begincidchar\n<0041> 70000\nendcidchar"));
    }

    [Fact]
    public void The_maximum_can_come_from_a_cidchar_rather_than_a_range()
    {
        Assert.Equal(99999, Max(
            "1 begincidrange\n<0000> <00ff> 0\nendcidrange\n"
            + "1 begincidchar\n<0041> 99999\nendcidchar"));
    }

    [Theory]
    [InlineData("1 begincidrange\n<0000> <ffff> 0\nendcidrange", 65535)]   // exactly at the limit
    [InlineData("1 begincidchar\n<0041> 65535\nendcidchar", 65535)]
    public void A_conforming_CMap_reports_its_maximum_without_exceeding_the_limit(string cmap, long expected)
    {
        Assert.Equal(expected, Max(cmap));
    }

    [Fact]
    public void Data_declaring_no_CIDs_reports_null()
    {
        Assert.Null(Max("/CIDInit /ProcSet findresource begin\nend"));
        Assert.Null(Max(string.Empty));
    }

    [Fact]
    public void Malformed_data_reports_null_rather_than_throwing()
    {
        Assert.Null(Max("begincidrange <zz> <qq> notanumber endcidrange"));
    }

    [Fact]
    public void A_range_wider_than_the_span_guard_is_deliberately_ignored()
    {
        // DELIBERATE UNDER-REPORT, not an oversight. Parse skips a range whose CODE span exceeds
        // MaxRangeSpan (0xFFFF) as corrupt; this scan applies the same guard so the two agree on
        // what a legitimate range is. A wider codespace is legal in ISO 32000, but this engine's
        // CID handling assumes two bytes throughout, and flagging a shape the rest of the engine
        // treats as corrupt would risk a false positive on a document no corpus fixture covers.
        // If this test ever "fails" because someone widened the scan, that is the decision being
        // reversed — reverse it deliberately, not incidentally.
        Assert.Null(Max("1 begincidrange\n<000000> <ffffff> 1\nendcidrange"));

        // ...and a wide range does not suppress a legitimate one beside it. 70000 + 0xFF = 70255,
        // the same start-plus-span arithmetic the first test in this file pins.
        Assert.Equal(70255, Max(
            "2 begincidrange\n<000000> <ffffff> 1\n<0000> <00ff> 70000\nendcidrange"));
    }
}
