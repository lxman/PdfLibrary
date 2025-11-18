using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Embedded.Tables.TtTables;
using PdfLibrary.Fonts.Embedded.Tables.TtTables.Glyf;

namespace PdfLibrary.Tests.Fonts.Embedded;

public class GlyfLocaTableTests
{
    #region LocaTable Tests

    [Fact]
    public void LocaTable_ParsesShortFormat_ReturnsDoubledOffsets()
    {
        // Arrange - Short format stores offsets / 2
        byte[] data = CreateLocaTableShortFormat(
            [0, 10, 20, 30] // Will be doubled to 0, 20, 40, 60
        );
        var table = new LocaTable(data);

        // Act
        table.Process(numGlyphs: 3, isShort: true);

        // Assert
        Assert.Equal(4, table.Offsets.Length); // numGlyphs + 1
        Assert.Equal(0u, table.Offsets[0]);
        Assert.Equal(20u, table.Offsets[1]);
        Assert.Equal(40u, table.Offsets[2]);
        Assert.Equal(60u, table.Offsets[3]);
    }

    [Fact]
    public void LocaTable_ParsesLongFormat_ReturnsDirectOffsets()
    {
        // Arrange - Long format stores actual offsets
        byte[] data = CreateLocaTableLongFormat(
            [0, 100, 250, 500]
        );
        var table = new LocaTable(data);

        // Act
        table.Process(numGlyphs: 3, isShort: false);

        // Assert
        Assert.Equal(4, table.Offsets.Length);
        Assert.Equal(0u, table.Offsets[0]);
        Assert.Equal(100u, table.Offsets[1]);
        Assert.Equal(250u, table.Offsets[2]);
        Assert.Equal(500u, table.Offsets[3]);
    }

    [Fact]
    public void LocaTable_HandlesEmptyGlyphs()
    {
        // Arrange - Empty glyphs have same offset as next glyph
        byte[] data = CreateLocaTableShortFormat(
            [0, 0, 10, 10, 20] // Glyphs 0 and 2 are empty
        );
        var table = new LocaTable(data);

        // Act
        table.Process(numGlyphs: 4, isShort: true);

        // Assert
        Assert.Equal(0u, table.Offsets[0]);
        Assert.Equal(0u, table.Offsets[1]);   // Empty glyph
        Assert.Equal(20u, table.Offsets[2]);
        Assert.Equal(20u, table.Offsets[3]);  // Empty glyph
        Assert.Equal(40u, table.Offsets[4]);
    }

    #endregion

    #region GlyphHeader Tests

    [Fact]
    public void GlyphHeader_ParsesSimpleGlyphHeader()
    {
        // Arrange
        byte[] data = CreateGlyphHeaderData(
            numberOfContours: 2,
            xMin: -50,
            yMin: -100,
            xMax: 450,
            yMax: 600
        );

        // Act
        var header = new GlyphHeader(data);

        // Assert
        Assert.Equal(2, header.NumberOfContours);
        Assert.Equal(-50, header.XMin);
        Assert.Equal(-100, header.YMin);
        Assert.Equal(450, header.XMax);
        Assert.Equal(600, header.YMax);
    }

    [Fact]
    public void GlyphHeader_ParsesCompositeGlyphHeader()
    {
        // Arrange - Negative contour count indicates composite
        byte[] data = CreateGlyphHeaderData(
            numberOfContours: -1,
            xMin: 0,
            yMin: 0,
            xMax: 1000,
            yMax: 1000
        );

        // Act
        var header = new GlyphHeader(data);

        // Assert
        Assert.Equal(-1, header.NumberOfContours);
        Assert.True(header.NumberOfContours < 0); // Composite indicator
    }

    #endregion

    #region SimpleGlyph Tests

    [Fact]
    public void SimpleGlyph_ParsesSingleContour()
    {
        // Arrange - Simple triangular glyph
        byte[] glyphData = CreateSimpleGlyphData(
            endPtsOfContours: [2], // One contour with 3 points
            instructions: [],
            flags: [0x01, 0x01, 0x01], // All on-curve
            xCoordinates: [0, 100, 50],
            yCoordinates: [0, 0, 100]
        );
        var header = new GlyphHeader(CreateGlyphHeaderData(1, 0, 0, 100, 100));
        var reader = new BigEndianReader(glyphData);

        // Act
        var glyph = new SimpleGlyph(reader, header);

        // Assert
        Assert.Single(glyph.EndPtsOfContours);
        Assert.Equal(2, glyph.EndPtsOfContours[0]); // Index 2 = 3 points (0,1,2)
        Assert.Equal(3, glyph.Coordinates.Count);
        Assert.True(glyph.Coordinates[0].OnCurve);
        Assert.True(glyph.Coordinates[1].OnCurve);
        Assert.True(glyph.Coordinates[2].OnCurve);
    }

    [Fact]
    public void SimpleGlyph_ParsesMultipleContours()
    {
        // Arrange - Glyph with 2 contours (like 'B')
        byte[] glyphData = CreateSimpleGlyphData(
            endPtsOfContours: [3, 7], // First contour: 4 points, second: 4 points
            instructions: [],
            flags: [0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01],
            xCoordinates: [0, 100, 100, 0, 20, 80, 80, 20],
            yCoordinates: [0, 0, 100, 100, 20, 20, 80, 80]
        );
        var header = new GlyphHeader(CreateGlyphHeaderData(2, 0, 0, 100, 100));
        var reader = new BigEndianReader(glyphData);

        // Act
        var glyph = new SimpleGlyph(reader, header);

        // Assert
        Assert.Equal(2, glyph.EndPtsOfContours.Count);
        Assert.Equal(3, glyph.EndPtsOfContours[0]);  // First contour ends at index 3
        Assert.Equal(7, glyph.EndPtsOfContours[1]);  // Second contour ends at index 7
        Assert.Equal(8, glyph.Coordinates.Count);
    }

    [Fact]
    public void SimpleGlyph_HandlesFlagRepeat()
    {
        // Arrange - Flags with repeat compression
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // endPtsOfContours
        WriteBigEndian(bw, (ushort)4); // 5 points

        // instructions
        WriteBigEndian(bw, (ushort)0); // No instructions

        // Flags with repeat: flag 0x01, repeat 4 times (total 5 points)
        bw.Write((byte)0x09); // Flag with Repeat bit set (0x01 | 0x08)
        bw.Write((byte)4);    // Repeat count

        // X coordinates (5 shorts)
        for (var i = 0; i < 5; i++)
            WriteBigEndian(bw, (short)0);

        // Y coordinates (5 shorts)
        for (var i = 0; i < 5; i++)
            WriteBigEndian(bw, (short)0);

        var header = new GlyphHeader(CreateGlyphHeaderData(1, 0, 0, 100, 100));
        var reader = new BigEndianReader(ms.ToArray());

        // Act
        var glyph = new SimpleGlyph(reader, header);

        // Assert
        Assert.Equal(5, glyph.Coordinates.Count);
        Assert.All(glyph.Coordinates, coord => Assert.True(coord.OnCurve));
    }

    #endregion

    #region CompositeGlyph Tests

    [Fact]
    public void CompositeGlyph_ParsesBasicStructure()
    {
        // Arrange - Minimal composite glyph
        byte[] glyphData = CreateCompositeGlyphData(
            flags: 0x0001, // ArgsAreWords
            glyphIndex: 42
        );
        var header = new GlyphHeader(CreateGlyphHeaderData(-1, 0, 0, 100, 100));
        var reader = new BigEndianReader(glyphData);

        // Act
        var glyph = new CompositeGlyph(reader, header);

        // Assert
        Assert.Equal(42, glyph.GlyphIndex);
    }

    #endregion

    #region GlyphTable Integration Tests

    [Fact]
    public void GlyphTable_ProcessesSimpleGlyphs()
    {
        // Arrange
        byte[] glyfData = CreateGlyfTableWithSimpleGlyphs();
        // Offsets: Glyph0=0-29 bytes, Glyph1=30-64 bytes
        // Short format stores offset/2, so [0, 15, 32]
        byte[] locaData = CreateLocaTableShortFormat([0, 15, 32]);

        var glyfTable = new GlyphTable(glyfData);
        var locaTable = new LocaTable(locaData);
        locaTable.Process(numGlyphs: 2, isShort: true);

        // Act
        glyfTable.Process(numGlyphs: 2, locaTable);

        // Assert
        Assert.Equal(2, glyfTable.Glyphs.Count);
        Assert.NotNull(glyfTable.GetGlyphData(0));
        Assert.NotNull(glyfTable.GetGlyphData(1));
    }

    [Fact]
    public void GlyphTable_SkipsEmptyGlyphs()
    {
        // Arrange - Glyph 1 is empty (same offset as glyph 2)
        byte[] glyfData = CreateGlyfTableWithEmptyGlyph();
        // Offsets: Glyph0=0-29, Glyph1=empty at 30, Glyph2=30-64
        // Short format stores offset/2, so [0, 15, 15, 32]
        byte[] locaData = CreateLocaTableShortFormat([0, 15, 15, 32]);

        var glyfTable = new GlyphTable(glyfData);
        var locaTable = new LocaTable(locaData);
        locaTable.Process(numGlyphs: 3, isShort: true);

        // Act
        glyfTable.Process(numGlyphs: 3, locaTable);

        // Assert
        Assert.Equal(2, glyfTable.Glyphs.Count); // Only non-empty glyphs
        Assert.NotNull(glyfTable.GetGlyphData(0));
        Assert.Null(glyfTable.GetGlyphData(1));    // Empty
        Assert.NotNull(glyfTable.GetGlyphData(2));
    }

    #endregion

    #region Helper Methods

    private static byte[] CreateLocaTableShortFormat(ushort[] offsets)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        foreach (ushort offset in offsets)
            WriteBigEndian(bw, offset);

        return ms.ToArray();
    }

    private static byte[] CreateLocaTableLongFormat(uint[] offsets)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        foreach (uint offset in offsets)
            WriteBigEndian(bw, offset);

        return ms.ToArray();
    }

    private static byte[] CreateGlyphHeaderData(
        short numberOfContours,
        short xMin,
        short yMin,
        short xMax,
        short yMax)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        WriteBigEndian(bw, numberOfContours);
        WriteBigEndian(bw, xMin);
        WriteBigEndian(bw, yMin);
        WriteBigEndian(bw, xMax);
        WriteBigEndian(bw, yMax);

        return ms.ToArray();
    }

    private static byte[] CreateSimpleGlyphData(
        ushort[] endPtsOfContours,
        byte[] instructions,
        byte[] flags,
        short[] xCoordinates,
        short[] yCoordinates)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // End points of contours
        foreach (ushort endPt in endPtsOfContours)
            WriteBigEndian(bw, endPt);

        // Instructions
        WriteBigEndian(bw, (ushort)instructions.Length);
        bw.Write(instructions);

        // Flags (no repeat compression in this helper)
        bw.Write(flags);

        // X coordinates (as shorts, not compressed)
        foreach (short x in xCoordinates)
            WriteBigEndian(bw, x);

        // Y coordinates (as shorts, not compressed)
        foreach (short y in yCoordinates)
            WriteBigEndian(bw, y);

        return ms.ToArray();
    }

    private static byte[] CreateCompositeGlyphData(ushort flags, ushort glyphIndex)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        WriteBigEndian(bw, flags);
        WriteBigEndian(bw, glyphIndex);

        // Minimal args (2 shorts if ArgsAreXyValues)
        if ((flags & 0x0002) != 0)
        {
            WriteBigEndian(bw, (short)0);
            WriteBigEndian(bw, (short)0);
        }

        return ms.ToArray();
    }

    private byte[] CreateGlyfTableWithSimpleGlyphs()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // Glyph 0 (at offset 0): Header=10 bytes + Data=19 bytes = 29 bytes total
        bw.Write(CreateGlyphHeaderData(1, 0, 0, 100, 100));
        bw.Write(CreateSimpleGlyphData(
            [2],
            [],
            [0x01, 0x01, 0x01],
            [0, 100, 50],
            [0, 0, 100]
        ));
        // Add padding byte to align to 2-byte boundary (29 bytes -> 30 bytes)
        bw.Write((byte)0);

        // Glyph 1 (at offset 30): Header=10 bytes + Data=24 bytes = 34 bytes total
        bw.Write(CreateGlyphHeaderData(1, 0, 0, 100, 100));
        bw.Write(CreateSimpleGlyphData(
            [3],
            [],
            [0x01, 0x01, 0x01, 0x01],
            [0, 100, 100, 0],
            [0, 0, 100, 100]
        ));

        return ms.ToArray();
    }

    private byte[] CreateGlyfTableWithEmptyGlyph()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // Glyph 0 (at offset 0): Header=10 bytes + Data=19 bytes = 29 bytes total
        bw.Write(CreateGlyphHeaderData(1, 0, 0, 100, 100));
        bw.Write(CreateSimpleGlyphData(
            [2],
            [],
            [0x01, 0x01, 0x01],
            [0, 100, 50],
            [0, 0, 100]
        ));
        // Add padding byte to align to 2-byte boundary (29 bytes -> 30 bytes)
        bw.Write((byte)0);

        // Glyph 1 is empty (at offset 30, same as glyph 2 - no data between offsets)

        // Glyph 2 (at offset 30): Header=10 bytes + Data=24 bytes = 34 bytes total
        bw.Write(CreateGlyphHeaderData(1, 0, 0, 100, 100));
        bw.Write(CreateSimpleGlyphData(
            [3],
            [],
            [0x01, 0x01, 0x01, 0x01],
            [0, 100, 100, 0],
            [0, 0, 100, 100]
        ));

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

    #endregion
}
