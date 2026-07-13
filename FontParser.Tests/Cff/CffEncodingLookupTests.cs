using FontParser.Reader;
using FontParser.Tables.Cff.Type1;
using Xunit;

namespace FontParser.Tests;

/// <summary>
/// Code→GID resolution through a CFF built-in Encoding (Adobe TN#5176 §12). This is the path a
/// symbolic CFF simple font uses when its PDF font dict has no usable /Encoding — e.g. the
/// URWIFX+txsys symbol subset in PDFUA-Ref-2-03, whose format-1 encoding maps code 15→bullet
/// (GID 1) and code 16→space (GID 2). GID 0 (.notdef) is never encoded.
/// </summary>
public class CffEncodingLookupTests
{
    [Fact]
    public void Format1_SequentialRange_MapsCodeToGid()
    {
        // nRanges=1, range First=15, NumberLeft=1  => codes 15,16 -> GID 1,2 (the txsys case).
        var enc = new Encoding1(new BigEndianReader(new byte[] { 1, 15, 1 }));

        Assert.Equal(1, CffEncodingLookup.GetGlyphId(enc, 15));
        Assert.Equal(2, CffEncodingLookup.GetGlyphId(enc, 16));
        Assert.Equal(0, CffEncodingLookup.GetGlyphId(enc, 14)); // below range → unmapped
        Assert.Equal(0, CffEncodingLookup.GetGlyphId(enc, 17)); // above range → unmapped
    }

    [Fact]
    public void Format1_MultipleRanges_AssignsGidsAcrossRangesInOrder()
    {
        // Two ranges: [First=65,NumberLeft=1] then [First=200,NumberLeft=0]
        // => code 65->GID1, 66->GID2, 200->GID3.
        var enc = new Encoding1(new BigEndianReader(new byte[] { 2, 65, 1, 200, 0 }));

        Assert.Equal(1, CffEncodingLookup.GetGlyphId(enc, 65));
        Assert.Equal(2, CffEncodingLookup.GetGlyphId(enc, 66));
        Assert.Equal(3, CffEncodingLookup.GetGlyphId(enc, 200));
        Assert.Equal(0, CffEncodingLookup.GetGlyphId(enc, 67));
    }

    [Fact]
    public void Format0_CodeArray_MapsCodeToGid()
    {
        // nCodes=2, codes[0]=65 (GID1), codes[1]=66 (GID2).
        var enc = new Encoding0(new BigEndianReader(new byte[] { 2, 65, 66 }));

        Assert.Equal(1, CffEncodingLookup.GetGlyphId(enc, 65));
        Assert.Equal(2, CffEncodingLookup.GetGlyphId(enc, 66));
        Assert.Equal(0, CffEncodingLookup.GetGlyphId(enc, 99));
    }

    [Fact]
    public void NullEncoding_ReturnsZero()
    {
        Assert.Equal(0, CffEncodingLookup.GetGlyphId(null, 15));
    }
}
