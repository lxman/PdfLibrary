using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>Issue 34: LoadCidToGidMap stored only non-zero entries, so MapCidToGid could not
/// distinguish "CID beyond the map's range" (identity fallback, correct) from "CID the map
/// explicitly declares GID 0" (a legitimate ".notdef, no glyph" answer it must report as 0).</summary>
public class CidToGidMapExplicitZeroTests
{
    /// <summary>CIDFontType2 descendant with a direct /CIDToGIDMap stream. Entries (big-endian
    /// 2-byte GIDs, indexed by CID): cid0→0, cid1→5, cid2→0. Covered range = 3 CIDs.</summary>
    private static CidFont FontWithMap()
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Font"));
        dict.Set("Subtype", new PdfName("CIDFontType2"));
        dict.Set("BaseFont", new PdfName("Test"));
        dict.Set("CIDToGIDMap", new PdfStream(new PdfDictionary(), [0, 0, 0, 5, 0, 0]));
        return new CidFont(dict);
    }

    [Fact]
    public void A_cid_the_map_explicitly_sends_to_zero_reports_gid_zero()
        => Assert.Equal(0, FontWithMap().MapCidToGid(2));

    [Fact]
    public void A_nonzero_entry_still_reports_its_gid()
        => Assert.Equal(5, FontWithMap().MapCidToGid(1));

    [Fact]
    public void A_cid_beyond_the_maps_covered_range_keeps_the_identity_fallback()
        => Assert.Equal(7, FontWithMap().MapCidToGid(7));

    [Fact]
    public void An_identity_name_keeps_identity()
    {
        var dict = new PdfDictionary();
        dict.Set("Subtype", new PdfName("CIDFontType2"));
        dict.Set("BaseFont", new PdfName("Test"));
        dict.Set("CIDToGIDMap", new PdfName("Identity"));
        Assert.Equal(42, new CidFont(dict).MapCidToGid(42));
    }
}
