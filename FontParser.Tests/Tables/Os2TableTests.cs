using FontParser.Tables.Os2;

namespace FontParser.Tests.Tables;

public class Os2TableTests
{
    /// <summary>A version-4 OS/2 table with known values at every offset this reader touches.</summary>
    private static byte[] BuildOs2(ushort version, ushort weightClass, ushort fsType,
        short typoAscender, short typoDescender, short xHeight, short capHeight)
    {
        var data = new byte[100];
        void U16(int offset, ushort v)
        {
            data[offset] = (byte)(v >> 8);
            data[offset + 1] = (byte)(v & 0xFF);
        }
        void S16(int offset, short v) => U16(offset, unchecked((ushort)v));

        U16(0, version);
        S16(2, 600);              // xAvgCharWidth, not read but must not shift anything
        U16(4, weightClass);
        U16(8, fsType);
        S16(68, typoAscender);
        S16(70, typoDescender);
        S16(74, 0);               // sTypoLineGap
        S16(86, xHeight);
        S16(88, capHeight);
        return data;
    }

    [Fact]
    public void Reads_the_fields_the_descriptor_needs()
    {
        var table = new Os2Table(BuildOs2(4, 700, 0, 1854, -434, 1062, 1409));

        Assert.Equal(4, table.Version);
        Assert.Equal(700, table.UsWeightClass);
        Assert.Equal(1409, table.SCapHeight);
        Assert.Equal(1062, table.SxHeight);
        Assert.Equal(1854, table.STypoAscender);
        Assert.Equal(-434, table.STypoDescender);
    }

    [Fact]
    public void A_short_version_0_table_does_not_throw_and_reports_no_CapHeight()
    {
        // A version-0 table is 78 bytes: sCapHeight does not exist in it at all. This exercises
        // the BytesRemaining length guard (offsets 86/88 are past the end of a 78-byte buffer),
        // NOT the version guard — see Version_below_2_ignores_CapHeight_bytes_even_when_present
        // below for the test that isolates the version check itself.
        byte[] v0 = BuildOs2(0, 400, 0, 1500, -400, 0, 0)[..78];

        var table = new Os2Table(v0);

        Assert.Equal(0, table.Version);
        Assert.Equal(400, table.UsWeightClass);
        Assert.Equal(0, table.SCapHeight);   // sentinel: absent, not zero-because-parsed
        Assert.Equal(1500, table.STypoAscender);
    }

    [Fact]
    public void A_truncated_table_does_not_throw()
    {
        Os2Table table = new(BuildOs2(4, 400, 0, 1500, -400, 1000, 1400)[..20]);

        Assert.Equal(400, table.UsWeightClass);
        Assert.Equal(0, table.SCapHeight);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    public void Version_below_2_ignores_CapHeight_bytes_even_when_present(ushort version)
    {
        // Full-length buffer (100 bytes) — offsets 86/88 are well within range and hold non-zero
        // garbage, so BytesRemaining alone would happily return it. Only the explicit
        // `if (Version < 2) return;` guard in Os2Table stops that garbage from surfacing. This is
        // the test that isolates the version guard: it fails if that line is removed.
        byte[] data = BuildOs2(version, 400, 0, 1500, -400, /*xHeight*/ 1234, /*capHeight*/ 5678);

        var table = new Os2Table(data);

        Assert.Equal(version, table.Version);
        Assert.Equal(0, table.SxHeight);
        Assert.Equal(0, table.SCapHeight);
    }

    [Theory]
    [InlineData(0x0000, false)] // Installable
    [InlineData(0x0002, true)]  // Restricted License Embedding
    [InlineData(0x0004, false)] // Preview & Print
    [InlineData(0x0008, false)] // Editable
    [InlineData(0x0006, true)]  // Restricted bit set alongside another — restricted still wins
    public void FsType_bit_1_is_the_only_embedding_prohibition(int fsType, bool restricted)
    {
        var table = new Os2Table(BuildOs2(4, 400, (ushort)fsType, 1500, -400, 1000, 1400));

        Assert.Equal(restricted, table.EmbeddingRestricted);
    }
}
