using System.Collections.Generic;
using System.Linq;
using FontParser.Tables.Cff.Type1;
using FontParser.Tables.Cff.Type1.Charsets;
using Xunit;

namespace FontParser.Tests;

/// <summary>
/// Top DICT charset handling for the PREDEFINED charsets (Adobe TN #5176 Table 9 and §14). The operator
/// is optional and defaults to 0 (ISOAdobe), and a predefined value 0/1/2 names a built-in charset rather
/// than an offset — there is no table in the font data to read. Both cases used to be mishandled: an
/// absent operator threw out of the constructor, and an explicit <c>charset 0</c> seeked to byte 0 and
/// parsed the CFF header as a charset table.
/// </summary>
public class Type1TablePredefinedCharsetTests
{
    [Fact]
    public void AbsentCharsetOperator_ParsesAsIsoAdobe()
    {
        var t = new Type1Table(MinimalCff.Build(charsetOperand: null, numGlyphs: 4)); // must NOT throw

        var f0 = Assert.IsType<CharsetsFormat0>(t.CharSet);
        Assert.Equal(new ushort[] { 1, 2, 3 }, f0.Glyphs); // GID i -> SID i, .notdef not encoded
        Assert.Equal(4, t.RawCharStrings.Count);
    }

    [Fact]
    public void ExplicitCharsetZero_MatchesAbsentOperator_AndDoesNotParseTheHeader()
    {
        var absent = new Type1Table(MinimalCff.Build(charsetOperand: null, numGlyphs: 4));
        var explicitZero = new Type1Table(MinimalCff.Build(charsetOperand: 0, numGlyphs: 4));

        var a = Assert.IsType<CharsetsFormat0>(absent.CharSet);
        var e = Assert.IsType<CharsetsFormat0>(explicitZero.CharSet);
        Assert.Equal(a.Glyphs, e.Glyphs);

        // The CFF header is 01 00 04 01, so parsing it as a format-0 charset would yield SID 0x0004
        // for GID 1 (and then read on into the Name INDEX). The identity mapping proves it did not.
        Assert.Equal(new ushort[] { 1, 2, 3 }, e.Glyphs);
    }

    [Fact]
    public void IsoAdobe_StopsAtSid228_ForAFontWithMoreGlyphs()
    {
        var t = new Type1Table(MinimalCff.Build(charsetOperand: 0, numGlyphs: 300));

        var f0 = Assert.IsType<CharsetsFormat0>(t.CharSet);
        Assert.Equal(228, f0.Glyphs.Count); // ISOAdobe defines SIDs 1..228 and no more
        Assert.Equal(228, f0.Glyphs[^1]);
    }

    [Theory]
    [InlineData(1)] // Expert
    [InlineData(2)] // ExpertSubset
    public void PredefinedExpertCharsets_ParseWithoutThrowing_AndLeaveCharsetNull(int operand)
    {
        var t = new Type1Table(MinimalCff.Build(charsetOperand: operand, numGlyphs: 4));

        // Expert/ExpertSubset are not the identity mapping and are not implemented; null is the honest
        // answer. What must NOT happen is reading a format byte at offset 1/2 (inside the CFF header).
        Assert.Null(t.CharSet);
        Assert.Equal(4, t.RawCharStrings.Count);
    }

    [Fact]
    public void CustomCharset_StillParsedFromTheTable()
    {
        // Regression guard for the offset > 2 path: a real format-0 table must still win over the default.
        var t = new Type1Table(MinimalCff.Build(charsetOperand: null, numGlyphs: 4,
            customCharsetSids: new ushort[] { 40, 41, 42 }));

        var f0 = Assert.IsType<CharsetsFormat0>(t.CharSet);
        Assert.Equal(new ushort[] { 40, 41, 42 }, f0.Glyphs);
        Assert.NotEmpty(t.RawCharset);
    }

    /// <summary>
    /// Builds a minimal but structurally valid non-CID CFF: header, Name INDEX, Top DICT INDEX, empty
    /// String and Global Subr INDEXes, a CharStrings INDEX of <c>endchar</c>-only glyphs and a Private
    /// DICT. Every Top DICT number uses the fixed 5-byte (0x1D) integer form so the dict length — and
    /// therefore every absolute offset in it — is known before the bytes are laid out.
    /// </summary>
    private static class MinimalCff
    {
        private static readonly byte[] FontName = "TestFont"u8.ToArray();

        public static byte[] Build(int? charsetOperand, int numGlyphs, ushort[]? customCharsetSids = null)
        {
            // Sizes of every section, so the Top DICT's absolute offsets can be computed up front.
            int nameIndexSize = 2 + 1 + 2 + FontName.Length;
            int topDictLen = (charsetOperand is null && customCharsetSids is null ? 0 : 6) // charset
                             + 6                                                          // CharStrings
                             + 11;                                                        // Private
            int topDictIndexSize = 2 + 1 + 2 + topDictLen;
            int charStringsSize = 2 + 1 + 2 * (numGlyphs + 1) + numGlyphs;
            byte[] privateDict = { 0x1D, 0, 0, 0, 0, 0x15 }; // nominalWidthX 0
            byte[] charsetTable = BuildCharsetTable(customCharsetSids);

            // Layout: header | name | topDict | string | globalSubr | pad | charset | charStrings | private
            int charsetOffset = 4 + nameIndexSize + topDictIndexSize + 2 + 2 + 1;
            int charStringsOffset = charsetOffset + charsetTable.Length;
            int privateOffset = charStringsOffset + charStringsSize;

            var top = new List<byte>();
            if (customCharsetSids is not null) AppendNumberOp(top, charsetOffset, 15);
            else if (charsetOperand is not null) AppendNumberOp(top, charsetOperand.Value, 15);
            AppendNumberOp(top, charStringsOffset, 17);
            AppendInt32(top, privateDict.Length);
            AppendInt32(top, privateOffset);
            top.Add(18);
            Assert.Equal(topDictLen, top.Count); // the offsets above depend on this

            var data = new List<byte> { 0x01, 0x00, 0x04, 0x01 }; // major, minor, hdrSize, offSize
            AppendIndex(data, new List<byte[]> { FontName });
            AppendIndex(data, new List<byte[]> { top.ToArray() });
            data.AddRange(new byte[] { 0x00, 0x00 }); // empty String INDEX
            data.AddRange(new byte[] { 0x00, 0x00 }); // empty Global Subr INDEX
            data.Add(0xFF);                           // Encoding format byte: neither 0 nor 1 => no Encoding
            data.AddRange(charsetTable);
            AppendIndex(data, Enumerable.Repeat(new byte[] { 0x0E }, numGlyphs).ToList(), offSize: 2);
            data.AddRange(privateDict);
            return data.ToArray();
        }

        private static byte[] BuildCharsetTable(ushort[]? sids)
        {
            if (sids is null) return [];
            var table = new List<byte> { 0x00 }; // format 0
            foreach (ushort sid in sids)
            {
                table.Add((byte)(sid >> 8));
                table.Add((byte)sid);
            }
            return table.ToArray();
        }

        private static void AppendNumberOp(List<byte> dict, int value, byte op)
        {
            AppendInt32(dict, value);
            dict.Add(op);
        }

        private static void AppendInt32(List<byte> dict, int value)
        {
            dict.Add(0x1D);
            dict.Add((byte)(value >> 24));
            dict.Add((byte)(value >> 16));
            dict.Add((byte)(value >> 8));
            dict.Add((byte)value);
        }

        private static void AppendIndex(List<byte> data, List<byte[]> entries, int offSize = 1)
        {
            data.Add((byte)(entries.Count >> 8));
            data.Add((byte)entries.Count);
            data.Add((byte)offSize);
            var offset = 1;
            AppendOffset(data, offset, offSize);
            foreach (byte[] entry in entries)
            {
                offset += entry.Length;
                AppendOffset(data, offset, offSize);
            }
            foreach (byte[] entry in entries) data.AddRange(entry);
        }

        private static void AppendOffset(List<byte> data, int offset, int offSize)
        {
            for (int shift = (offSize - 1) * 8; shift >= 0; shift -= 8)
                data.Add((byte)(offset >> shift));
        }
    }
}
