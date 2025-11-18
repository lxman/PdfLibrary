using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables;

namespace PdfLibrary.Tests.Fonts.Embedded;

public class CmapFormatTests
{
    #region Format 0 Tests

    [Fact]
    public void CmapFormat0_ParsesAndMapsCorrectly()
    {
        // Arrange - Format 0 is simple byte encoding (0-255)
        byte[] data = CreateCmapFormat0Data();
        var reader = new BigEndianReader(data);

        // Act
        var cmap = new CmapSubtablesFormat0(reader);

        // Assert
        Assert.Equal(65, cmap.GetGlyphId(0)); // First byte maps to glyph 65
        Assert.Equal(66, cmap.GetGlyphId(1)); // Second byte maps to glyph 66
        Assert.Equal(90, cmap.GetGlyphId(25)); // 'Z' position
    }

    #endregion

    #region Format 2 Tests

    [Fact]
    public void CmapFormat2_ParsesSubHeaderStructure()
    {
        // Arrange - Format 2 for mixed 8/16-bit (Asian fonts)
        byte[] data = CreateCmapFormat2Data();
        var reader = new BigEndianReader(data);

        // Act
        var cmap = new CmapSubtablesFormat2(reader);

        // Assert
        Assert.NotNull(cmap);
        Assert.Equal(256, cmap.SubHeaderKeys.Count);
        Assert.NotEmpty(cmap.SubHeaders);
    }

    #endregion

    #region Format 4 Tests (Already tested in FontTableTests, but adding edge cases)

    [Fact]
    public void CmapFormat4_HandlesMultipleSegments()
    {
        // Arrange - Multiple non-contiguous ranges
        byte[] data = CreateCmapFormat4WithSegments(
            (0x0041, 0x005A, 100), // A-Z -> glyphs 100-125
            (0x0061, 0x007A, 200)  // a-z -> glyphs 200-225
        );
        var reader = new BigEndianReader(data);

        // Act
        var cmap = new CmapSubtablesFormat4(reader);

        // Assert
        Assert.Equal(100, cmap.GetGlyphId(0x0041)); // 'A'
        Assert.Equal(125, cmap.GetGlyphId(0x005A)); // 'Z'
        Assert.Equal(200, cmap.GetGlyphId(0x0061)); // 'a'
        Assert.Equal(0, cmap.GetGlyphId(0x0040));   // '@' not mapped
    }

    #endregion

    #region Format 6 Tests

    [Fact]
    public void CmapFormat6_TrimmedArrayMapping()
    {
        // Arrange - Format 6 maps contiguous range starting at firstCode
        byte[] data = CreateCmapFormat6Data(
            firstCode: 0x0100,
            glyphIds: [50, 51, 52, 53, 54]
        );
        var reader = new BigEndianReader(data);

        // Act
        var cmap = new CmapSubtablesFormat6(reader);

        // Assert
        Assert.Equal(50, cmap.GetGlyphId(0x0100));
        Assert.Equal(51, cmap.GetGlyphId(0x0101));
        Assert.Equal(54, cmap.GetGlyphId(0x0104));
        Assert.Equal(0, cmap.GetGlyphId(0x00FF));  // Before range
        Assert.Equal(0, cmap.GetGlyphId(0x0105));  // After range
    }

    [Fact]
    public void CmapFormat6_WithZeroFirstCode()
    {
        // Arrange
        byte[] data = CreateCmapFormat6Data(
            firstCode: 0,
            glyphIds: [1, 2, 3]
        );
        var reader = new BigEndianReader(data);

        // Act
        var cmap = new CmapSubtablesFormat6(reader);

        // Assert
        Assert.Equal(1, cmap.GetGlyphId(0));
        Assert.Equal(2, cmap.GetGlyphId(1));
        Assert.Equal(3, cmap.GetGlyphId(2));
    }

    #endregion

    #region Format 10 Tests

    [Fact]
    public void CmapFormat10_Handles32BitTrimmedArray()
    {
        // Arrange - Format 10 is like Format 6 but with 32-bit support
        // Test within BMP since GetGlyphId takes ushort
        byte[] data = CreateCmapFormat10Data(
            startChar: 0xF000,  // Private Use Area in BMP
            glyphIds: [100, 101, 102]
        );
        var reader = new BigEndianReader(data);

        // Act
        var cmap = new CmapSubtablesFormat10(reader);

        // Assert
        Assert.Equal(100, cmap.GetGlyphId(0xF000));
        Assert.Equal(101, cmap.GetGlyphId(0xF001));
        Assert.Equal(102, cmap.GetGlyphId(0xF002));
        Assert.Equal(0, cmap.GetGlyphId(0xEFFF));  // Before range
    }

    #endregion

    #region Format 12 Tests

    [Fact]
    public void CmapFormat12_MapsSequentialGroups()
    {
        // Arrange - Format 12 with sequential mapping groups
        // Test within BMP since GetGlyphId takes ushort
        byte[] data = CreateCmapFormat12Data(
            new SequentialMapGroupData(0x0041, 0x005A, 100), // A-Z -> 100-125
            new SequentialMapGroupData(0xF000, 0xF010, 500)  // Private Use Area
        );
        var reader = new BigEndianReader(data);

        // Act
        var cmap = new CmapSubtablesFormat12(reader);

        // Assert
        Assert.Equal(100, cmap.GetGlyphId(0x0041));   // 'A'
        Assert.Equal(125, cmap.GetGlyphId(0x005A));   // 'Z'
        Assert.Equal(500, cmap.GetGlyphId(0xF000));   // Private Use Area start
        Assert.Equal(516, cmap.GetGlyphId(0xF010));   // Private Use Area end
        Assert.Equal(0, cmap.GetGlyphId(0x0040));     // Not mapped
    }

    [Fact]
    public void CmapFormat12_HandlesLargeUnicodeValues()
    {
        // Arrange - Test with supplementary plane characters
        byte[] data = CreateCmapFormat12Data(
            new SequentialMapGroupData(0x20000, 0x20010, 1000)
        );
        var reader = new BigEndianReader(data);

        // Act
        var cmap = new CmapSubtablesFormat12(reader);

        // Assert - Note: GetGlyphId takes ushort, so we test within BMP
        // This tests the structure parsing, not the full 32-bit lookup
        Assert.NotNull(cmap.Groups);
        Assert.Single(cmap.Groups);
        Assert.Equal(0x20000u, cmap.Groups[0].StartCharCode);
        Assert.Equal(0x20010u, cmap.Groups[0].EndCharCode);
        Assert.Equal(1000u, cmap.Groups[0].StartGlyphId);
    }

    #endregion

    #region Format 13 Tests

    [Fact]
    public void CmapFormat13_MapsConstantGroups()
    {
        // Arrange - Format 13 maps ranges to single glyph (many-to-one)
        byte[] data = CreateCmapFormat13Data(
            new ConstantMapGroupData(0x0100, 0x01FF, 999), // Range maps to .notdef
            new ConstantMapGroupData(0x0200, 0x02FF, 100)  // Another range
        );
        var reader = new BigEndianReader(data);

        // Act
        var cmap = new CmapSubtablesFormat13(reader);

        // Assert
        Assert.Equal(999, cmap.GetGlyphId(0x0100));
        Assert.Equal(999, cmap.GetGlyphId(0x0150)); // Same glyph
        Assert.Equal(999, cmap.GetGlyphId(0x01FF)); // Still same glyph
        Assert.Equal(100, cmap.GetGlyphId(0x0200)); // Different group
        Assert.Equal(0, cmap.GetGlyphId(0x0300));   // Not mapped
    }

    #endregion

    #region Format 14 Tests

    [Fact]
    public void CmapFormat14_ParsesVariationSelectors()
    {
        // Arrange - Format 14 for variation sequences (emoji, etc.)
        byte[] data = CreateCmapFormat14Data();
        var reader = new BigEndianReader(data);

        // Act
        var cmap = new CmapSubtablesFormat14(reader);

        // Assert
        Assert.NotNull(cmap);
        Assert.Equal(-1, cmap.Language); // Format 14 always has language -1
        Assert.NotEmpty(cmap.VarSelectorRecords);
    }

    #endregion

    #region Helper Methods

    private static byte[] CreateCmapFormat0Data()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        WriteBigEndian(bw, (ushort)0);      // format
        WriteBigEndian(bw, (ushort)262);    // length
        WriteBigEndian(bw, (ushort)0);      // language

        // Write 256 glyph IDs (simple mapping: code point i -> glyph i+65)
        for (var i = 0; i < 256; i++)
        {
            bw.Write((byte)(i + 65));
        }

        return ms.ToArray();
    }

    private static byte[] CreateCmapFormat2Data()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // Format 2: High-byte mapping through table
        // Structure:
        // - Header (8 bytes): format, length, reserved, language
        // - SubHeaderKeys (512 bytes): 256 ushorts, offsets from position 8
        // - SubHeaders (8 bytes each): FirstCode, EntryCount, IdDelta, IdRangeOffset
        // - GlyphIndexArray (variable): ushorts indexed by subheaders

        const ushort totalLength = 1040; // 8 + 512 + 8 + 512

        // Header (position 0-7)
        WriteBigEndian(bw, (ushort)2);          // format
        WriteBigEndian(bw, totalLength);        // length
        WriteBigEndian(bw, (ushort)0);          // reserved
        WriteBigEndian(bw, (ushort)0);          // language

        // SubHeaderKeys (position 8-519): 256 ushorts
        // All point to offset 512 (position 8 + 512 = 520 - first subheader)
        for (var i = 0; i < 256; i++)
        {
            WriteBigEndian(bw, (ushort)512);    // Offset to first (and only) subheader
        }

        // Position is now 520
        // SubHeader 0 (position 520-527): Covers entire single-byte range (0-255)
        WriteBigEndian(bw, (ushort)0);          // FirstCode = 0
        WriteBigEndian(bw, (ushort)256);        // EntryCount = 256 (covers 0-255)
        WriteBigEndian(bw, (short)0);           // IdDelta = 0
        WriteBigEndian(bw, (ushort)0);          // IdRangeOffset = 0 (array follows immediately)

        // Position is now 528
        // GlyphIndexArray: 256 glyph indices (identity mapping: char i -> glyph i)
        for (var i = 0; i < 256; i++)
        {
            WriteBigEndian(bw, (ushort)i);
        }

        return ms.ToArray();
    }

    private static byte[] CreateCmapFormat4WithSegments(params (ushort startCode, ushort endCode, ushort startGlyphId)[] segments)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        var segCount = (ushort)(segments.Length + 1); // +1 for terminator

        WriteBigEndian(bw, (ushort)4);                   // format
        WriteBigEndian(bw, (ushort)(16 + segCount * 8)); // length
        WriteBigEndian(bw, (ushort)0);                   // language
        WriteBigEndian(bw, (ushort)(segCount * 2));      // segCountX2

        var searchRange = (ushort)(2 * (1 << (int)Math.Floor(Math.Log(segCount, 2))));
        WriteBigEndian(bw, searchRange);
        WriteBigEndian(bw, (ushort)Math.Floor(Math.Log(segCount, 2)));
        WriteBigEndian(bw, (ushort)(segCount * 2 - searchRange));

        // endCode array
        foreach ((ushort startCode, ushort endCode, ushort startGlyphId) seg in segments)
            WriteBigEndian(bw, seg.endCode);
        WriteBigEndian(bw, (ushort)0xFFFF);

        WriteBigEndian(bw, (ushort)0); // reservedPad

        // startCode array
        foreach ((ushort startCode, ushort endCode, ushort startGlyphId) seg in segments)
            WriteBigEndian(bw, seg.startCode);
        WriteBigEndian(bw, (ushort)0xFFFF);

        // idDelta array
        foreach ((ushort startCode, ushort endCode, ushort startGlyphId) seg in segments)
        {
            var delta = (short)(seg.startGlyphId - seg.startCode);
            WriteBigEndian(bw, delta);
        }
        WriteBigEndian(bw, (short)1);

        // idRangeOffset array (all zeros)
        for (var i = 0; i < segCount; i++)
            WriteBigEndian(bw, (ushort)0);

        return ms.ToArray();
    }

    private static byte[] CreateCmapFormat6Data(ushort firstCode, ushort[] glyphIds)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        WriteBigEndian(bw, (ushort)6);                      // format
        WriteBigEndian(bw, (ushort)(10 + glyphIds.Length * 2)); // length
        WriteBigEndian(bw, (ushort)0);                      // language
        WriteBigEndian(bw, firstCode);                      // firstCode
        WriteBigEndian(bw, (ushort)glyphIds.Length);        // entryCount

        foreach (ushort glyphId in glyphIds)
            WriteBigEndian(bw, glyphId);

        return ms.ToArray();
    }

    private static byte[] CreateCmapFormat10Data(uint startChar, ushort[] glyphIds)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        WriteBigEndian(bw, (ushort)10);                     // format
        WriteBigEndian(bw, (ushort)0);                      // reserved
        WriteBigEndian(bw, (uint)(20 + glyphIds.Length * 2)); // length
        WriteBigEndian(bw, 0);                              // language
        WriteBigEndian(bw, startChar);                      // startCharCode
        WriteBigEndian(bw, (uint)glyphIds.Length);          // numChars

        foreach (ushort glyphId in glyphIds)
            WriteBigEndian(bw, glyphId);

        return ms.ToArray();
    }

    private static byte[] CreateCmapFormat12Data(params SequentialMapGroupData[] groups)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        WriteBigEndian(bw, (ushort)12);                     // format
        WriteBigEndian(bw, (ushort)0);                      // reserved
        WriteBigEndian(bw, (uint)(16 + groups.Length * 12)); // length
        WriteBigEndian(bw, 0);                              // language
        WriteBigEndian(bw, (uint)groups.Length);            // numGroups

        foreach (SequentialMapGroupData group in groups)
        {
            WriteBigEndian(bw, group.StartCharCode);
            WriteBigEndian(bw, group.EndCharCode);
            WriteBigEndian(bw, group.StartGlyphId);
        }

        return ms.ToArray();
    }

    private static byte[] CreateCmapFormat13Data(params ConstantMapGroupData[] groups)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        WriteBigEndian(bw, (ushort)13);                     // format
        WriteBigEndian(bw, (ushort)0);                      // reserved
        WriteBigEndian(bw, (uint)(16 + groups.Length * 12)); // length
        WriteBigEndian(bw, 0);                              // language
        WriteBigEndian(bw, groups.Length);                  // numGroups

        foreach (ConstantMapGroupData group in groups)
        {
            WriteBigEndian(bw, group.StartCharCode);
            WriteBigEndian(bw, group.EndCharCode);
            WriteBigEndian(bw, group.GlyphId);
        }

        return ms.ToArray();
    }

    private static byte[] CreateCmapFormat14Data()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        WriteBigEndian(bw, (ushort)14);     // format
        WriteBigEndian(bw, 21u);            // length (header + 1 minimal record)
        WriteBigEndian(bw, 1u);             // numVarSelectorRecords

        // One variation selector record
        WriteUInt24(bw, 0xFE00);            // varSelector (U+FE00)
        WriteBigEndian(bw, 0u);             // defaultUVSOffset (none)
        WriteBigEndian(bw, 0u);             // nonDefaultUVSOffset (none)

        return ms.ToArray();
    }

    private static void WriteBigEndian(BinaryWriter bw, ushort value)
    {
        bw.Write((byte)(value >> 8));
        bw.Write((byte)(value & 0xFF));
    }

    private static void WriteBigEndian(BinaryWriter bw, short value)
    {
        WriteBigEndian(bw, (ushort)value);
    }

    private static void WriteBigEndian(BinaryWriter bw, uint value)
    {
        bw.Write((byte)(value >> 24));
        bw.Write((byte)((value >> 16) & 0xFF));
        bw.Write((byte)((value >> 8) & 0xFF));
        bw.Write((byte)(value & 0xFF));
    }

    private static void WriteBigEndian(BinaryWriter bw, int value)
    {
        WriteBigEndian(bw, (uint)value);
    }

    private static void WriteUInt24(BinaryWriter bw, uint value)
    {
        bw.Write((byte)((value >> 16) & 0xFF));
        bw.Write((byte)((value >> 8) & 0xFF));
        bw.Write((byte)(value & 0xFF));
    }

    #endregion

    #region Helper Data Structures

    private record SequentialMapGroupData(uint StartCharCode, uint EndCharCode, uint StartGlyphId);
    private record ConstantMapGroupData(uint StartCharCode, uint EndCharCode, uint GlyphId);

    #endregion
}
