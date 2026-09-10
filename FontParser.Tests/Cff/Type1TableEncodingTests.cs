using CffTestFixtures;
using FontParser.Tables.Cff.Type1;
using Xunit;

namespace FontParser.Tests;

/// <summary>Top DICT Encoding handling (Adobe TN #5176 Table 9 and section 12).</summary>
public class Type1TableEncodingTests
{
    [Fact]
    public void CustomEncoding_IsReadFromTopDictOffset_NotAfterGlobalSubrIndex()
    {
        // Format 0: GID 1 -> code 65, GID 2 -> code 66. The fixture leaves the old positional
        // location at 0xFF and adds two more padding bytes before the real table, so this only
        // resolves when Type1Table follows Top DICT operator 16.
        byte[] cff = MinimalCff.Build(
            charsetOperand: null,
            numGlyphs: 3,
            customCharsetSids: [40, 41],
            customEncodingTable: [0, 2, 65, 66],
            encodingPaddingLength: 2);

        var table = new Type1Table(cff);
        var encoding = Assert.IsType<Encoding0>(table.Encoding);

        Assert.Equal(new byte[] { 65, 66 }, encoding.CodeArray);
        Assert.Equal((ushort)1, CffEncodingLookup.GetGlyphId(encoding, 65));
        Assert.Equal((ushort)2, CffEncodingLookup.GetGlyphId(encoding, 66));
        Assert.Equal((ushort)0, CffEncodingLookup.GetGlyphId(encoding, 64));
    }
}
