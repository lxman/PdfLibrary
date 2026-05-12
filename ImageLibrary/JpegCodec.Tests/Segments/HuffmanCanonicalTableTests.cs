using JpegCodec.Segments;

namespace JpegCodec.Tests.Segments;

public class HuffmanCanonicalTableTests
{
    // T.81 Annex K, Table K.3 — Standard luminance DC table.
    private static readonly byte[] LumaDcBits =
        [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] LumaDcValues =
        [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B];

    // Expected per T.81 §K.3.1 / Table F.1 — (length, code) by SSSS symbol.
    // Code is binary in spec; here we store as integer with the binary
    // representation interpreted MSB-first in 'length' bits.
    private static readonly (byte length, int code)[] LumaDcExpected =
    [
        (2, 0b00),        // SSSS 0
        (3, 0b010),       // SSSS 1
        (3, 0b011),       // SSSS 2
        (3, 0b100),       // SSSS 3
        (3, 0b101),       // SSSS 4
        (3, 0b110),       // SSSS 5
        (4, 0b1110),      // SSSS 6
        (5, 0b11110),     // SSSS 7
        (6, 0b111110),    // SSSS 8
        (7, 0b1111110),   // SSSS 9
        (8, 0b11111110),  // SSSS A
        (9, 0b111111110), // SSSS B
    ];

    [Fact]
    public void BuildLuma_DcTable_MatchesAnnexK3_1()
    {
        var table = HuffmanCanonicalTable.Build(LumaDcBits, LumaDcValues);

        Assert.Equal(LumaDcExpected.Length, table.HuffCode.Length);
        for (var i = 0; i < LumaDcExpected.Length; i++)
        {
            Assert.Equal(LumaDcExpected[i].length, table.HuffSize[i]);
            Assert.Equal(LumaDcExpected[i].code, table.HuffCode[i]);
        }
    }

    [Fact]
    public void Build_PopulatesMinMaxValPtr_ForDecoder()
    {
        var table = HuffmanCanonicalTable.Build(LumaDcBits, LumaDcValues);

        // No codes of length 1.
        Assert.Equal(0, table.MinCode[0]);
        Assert.Equal(-1, table.MaxCode[0]);

        // Length 2: one code (00). Min=Max=0.
        Assert.Equal(0, table.MinCode[1]);
        Assert.Equal(0, table.MaxCode[1]);
        Assert.Equal(0, table.ValPtr[1]);

        // Length 3: five codes (010..110). Min=010=2, Max=110=6.
        Assert.Equal(0b010, table.MinCode[2]);
        Assert.Equal(0b110, table.MaxCode[2]);
        Assert.Equal(1, table.ValPtr[2]);

        // Length 4: one code (1110).
        Assert.Equal(0b1110, table.MinCode[3]);
        Assert.Equal(0b1110, table.MaxCode[3]);
        Assert.Equal(6, table.ValPtr[3]);

        // Length 9 (longest in this table): one code (111111110).
        Assert.Equal(0b111111110, table.MinCode[8]);
        Assert.Equal(0b111111110, table.MaxCode[8]);
        Assert.Equal(11, table.ValPtr[8]);

        // Length 10+ unused.
        Assert.Equal(-1, table.MaxCode[9]);
        Assert.Equal(-1, table.MaxCode[15]);
    }

    [Fact]
    public void BuildLookup_FastPath_8BitPrefix()
    {
        var table = HuffmanCanonicalTable.Build(LumaDcBits, LumaDcValues);

        // SSSS=0, code=00, length 2 — top 2 bits 00 → entries 0x00..0x3F.
        for (var p = 0; p < 0x40; p++)
        {
            (byte len, byte sym) = HuffmanCanonicalTable.DecodeFastEntry(table.FastLookup[p]);
            Assert.Equal(2, len);
            Assert.Equal(0, sym);
        }

        // SSSS=1, code=010 (length 3) — top 3 bits 010 → 0x40..0x5F.
        for (var p = 0x40; p < 0x60; p++)
        {
            (byte len, byte sym) = HuffmanCanonicalTable.DecodeFastEntry(table.FastLookup[p]);
            Assert.Equal(3, len);
            Assert.Equal(1, sym);
        }

        // SSSS=6, code=1110 (length 4) — top 4 bits 1110 → 0xE0..0xEF.
        for (var p = 0xE0; p < 0xF0; p++)
        {
            (byte len, byte sym) = HuffmanCanonicalTable.DecodeFastEntry(table.FastLookup[p]);
            Assert.Equal(4, len);
            Assert.Equal(6, sym);
        }

        // SSSS=A, code=11111110 (length 8) — exactly one entry 0xFE.
        {
            (byte len, byte sym) = HuffmanCanonicalTable.DecodeFastEntry(table.FastLookup[0xFE]);
            Assert.Equal(8, len);
            Assert.Equal(0x0A, sym);
        }

        // SSSS=B, code=111111110 (length 9) — does not fit in 8 bits,
        // every entry whose top 8 bits are 11111111 must fall back
        // (length=0).
        {
            (byte len, _) = HuffmanCanonicalTable.DecodeFastEntry(table.FastLookup[0xFF]);
            Assert.Equal(0, len);
        }
    }

    [Fact]
    public void Build_RejectsBitsHuffvalLengthMismatch()
    {
        // Bits sum says 12, but we pass only 10 values.
        Assert.Throws<InvalidOperationException>(() =>
            HuffmanCanonicalTable.Build(LumaDcBits, LumaDcValues.Take(10).ToArray()));
    }

    [Fact]
    public void Build_RejectsAllZeroBits()
    {
        var zero = new byte[16];
        Assert.Throws<InvalidOperationException>(() =>
            HuffmanCanonicalTable.Build(zero, []));
    }

    [Fact]
    public void Build_RejectsOversubscribedBits()
    {
        // 3 codes of length 1 — impossible (only 2^1 = 2 codes fit).
        var oversubscribed = new byte[16];
        oversubscribed[0] = 3;
        byte[] values = [0, 0, 0];
        Assert.Throws<InvalidOperationException>(() =>
            HuffmanCanonicalTable.Build(oversubscribed, values));
    }

    [Fact]
    public void Build_SingleLength1Code_Works()
    {
        var bits = new byte[16];
        bits[0] = 1;
        byte[] values = [0x42];
        var table = HuffmanCanonicalTable.Build(bits, values);
        Assert.Equal(1, table.HuffSize[0]);
        Assert.Equal(0, table.HuffCode[0]);
        // Fast lookup: code 0 length 1 → top half of table; code 1 length
        // 1 would also exist but BITS says only 1 code at this length, so
        // entries 0x80..0xFF stay unset.
        for (var p = 0; p < 0x80; p++)
            Assert.Equal(0x42, table.FastLookup[p] & 0xFF);
        for (var p = 0x80; p < 0x100; p++)
            Assert.Equal(0, table.FastLookup[p]);
    }
}
