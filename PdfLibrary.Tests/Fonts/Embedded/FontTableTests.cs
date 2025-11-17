using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Embedded.Tables;

namespace PdfLibrary.Tests.Fonts.Embedded;

public class FontTableTests
{
    #region HeadTable Tests

    [Fact]
    public void HeadTable_ParsesValidData_ReturnsCorrectValues()
    {
        // Arrange - Create valid head table data
        var data = CreateHeadTableData(
            version: 0x00010000,
            fontRevision: 0x00010000,
            unitsPerEm: 1000,
            xMin: -100,
            yMin: -200,
            xMax: 900,
            yMax: 800,
            macStyle: MacStyle.Bold | MacStyle.Italic,
            indexToLocFormat: IndexToLocFormat.Offset32
        );

        // Act
        var table = new HeadTable(data);

        // Assert
        Assert.Equal(1, table.MajorVersion);
        Assert.Equal(0, table.MinorVersion);
        Assert.Equal(1.0f, table.FontRevision, 0.01f);
        Assert.Equal((ushort)1000, table.UnitsPerEm);
        Assert.Equal((short)-100, table.XMin);
        Assert.Equal((short)-200, table.YMin);
        Assert.Equal((short)900, table.XMax);
        Assert.Equal((short)800, table.YMax);
        Assert.True(table.MacStyle.HasFlag(MacStyle.Bold));
        Assert.True(table.MacStyle.HasFlag(MacStyle.Italic));
        Assert.Equal(IndexToLocFormat.Offset32, table.IndexToLocFormat);
    }

    [Fact]
    public void HeadTable_ParsesMinimalData_ReturnsDefaults()
    {
        // Arrange - Minimal valid head table
        var data = CreateHeadTableData(
            version: 0x00010000,
            fontRevision: 0x00010000,
            unitsPerEm: 2048
        );

        // Act
        var table = new HeadTable(data);

        // Assert
        Assert.Equal((ushort)2048, table.UnitsPerEm);
        Assert.Equal(0x5F0F3CF5u, table.MagicNumber);
    }

    #endregion

    #region HheaTable Tests

    [Fact]
    public void HheaTable_ParsesValidData_ReturnsCorrectValues()
    {
        // Arrange
        var data = CreateHheaTableData(
            ascender: 750,
            descender: -250,
            lineGap: 0,
            advanceWidthMax: 1000,
            numberOfHMetrics: 256
        );

        // Act
        var table = new HheaTable(data);

        // Assert
        Assert.Equal(750, table.Ascender);
        Assert.Equal(-250, table.Descender);
        Assert.Equal(0, table.LineGap);
        Assert.Equal((ushort)1000, table.AdvanceWidthMax);
        Assert.Equal((ushort)256, table.NumberOfHMetrics);
    }

    #endregion

    #region MaxpTable Tests

    [Fact]
    public void MaxpTable_ParsesVersion10_ReturnsAllFields()
    {
        // Arrange
        var data = CreateMaxpTableData(
            version: 0x00010000,
            numGlyphs: 512,
            maxPoints: 100,
            maxContours: 10
        );

        // Act
        var table = new MaxpTable(data);

        // Assert
        Assert.Equal(0x00010000u, table.Version);
        Assert.Equal((ushort)512, table.NumGlyphs);
        Assert.Equal((ushort)100, table.MaxPoints);
        Assert.Equal((ushort)10, table.MaxContours);
    }

    [Fact]
    public void MaxpTable_ParsesVersion05_ReturnsOnlyBasicFields()
    {
        // Arrange
        var data = CreateMaxpTableData(
            version: 0x00005000,
            numGlyphs: 256
        );

        // Act
        var table = new MaxpTable(data);

        // Assert
        Assert.Equal(0x00005000u, table.Version);
        Assert.Equal((ushort)256, table.NumGlyphs);
        Assert.Equal((ushort)0, table.MaxPoints); // Not set for version 0.5
    }

    #endregion

    #region HmtxTable Tests

    [Fact]
    public void HmtxTable_ParsesValidData_ReturnsCorrectMetrics()
    {
        // Arrange
        var data = CreateHmtxTableData(
            new[] { (ushort)500, (ushort)600, (ushort)700 }, // advance widths
            new[] { (short)50, (short)60, (short)70 }         // left side bearings
        );

        // Act
        var table = new HmtxTable(data);
        table.Process(3, 3);

        // Assert
        Assert.Equal(3, table.LongHMetricRecords.Count);
        Assert.Equal((ushort)500, table.LongHMetricRecords[0].AdvanceWidth);
        Assert.Equal((short)50, table.LongHMetricRecords[0].LeftSideBearing);
        Assert.Equal((ushort)600, table.GetAdvanceWidth(1));
        Assert.Equal((short)70, table.GetLeftSideBearing(2));
    }

    [Fact]
    public void HmtxTable_WithMonospaceFont_SharesLastAdvanceWidth()
    {
        // Arrange - 2 long metrics, but 5 total glyphs (last 3 share advance width)
        var data = CreateHmtxTableDataWithSharedAdvance(
            longMetrics: new[] { (ushort)500, (ushort)500 },
            longLsbs: new[] { (short)50, (short)60 },
            additionalLsbs: new[] { (short)70, (short)80, (short)90 }
        );

        // Act
        var table = new HmtxTable(data);
        table.Process(2, 5);

        // Assert
        Assert.Equal(2, table.LongHMetricRecords.Count);
        Assert.Equal(3, table.LeftSideBearings.Count);
        Assert.Equal((ushort)500, table.GetAdvanceWidth(0)); // From long metric
        Assert.Equal((ushort)500, table.GetAdvanceWidth(4)); // Shared from last long metric
        Assert.Equal((short)90, table.GetLeftSideBearing(4)); // From additional LSBs
    }

    #endregion

    #region NameTable Tests

    [Fact]
    public void NameTable_ParsesValidData_ExtractsNames()
    {
        // Arrange
        var data = CreateNameTableData(
            ("Family", "Arial"),
            ("Subfamily", "Bold"),
            ("Full Name", "Arial Bold"),
            ("PostScript Name", "Arial-Bold")
        );

        // Act
        var table = new NameTable(data);

        // Assert
        Assert.Equal("Arial", table.GetFamilyName());
        Assert.Equal("Bold", table.GetSubfamilyName());
        Assert.Equal("Arial Bold", table.GetFullName());
        Assert.Equal("Arial-Bold", table.GetPostScriptName());
    }

    [Fact]
    public void NameTable_PrefersWindowsUnicode_OverOtherPlatforms()
    {
        // Arrange - Create table with both Macintosh and Windows entries
        var data = CreateNameTableWithMultiplePlatforms(
            macFamily: "Arial-Mac",
            windowsFamily: "Arial-Win"
        );

        // Act
        var table = new NameTable(data);

        // Assert
        Assert.Equal("Arial-Win", table.GetFamilyName()); // Should prefer Windows
    }

    #endregion

    #region CmapTable Tests

    [Fact]
    public void CmapTable_Format4_MapsCharactersToGlyphs()
    {
        // Arrange - Create Format 4 cmap for basic ASCII
        var data = CreateCmapTableFormat4(
            ('A', (ushort)33),
            ('Z', (ushort)58),
            ('a', (ushort)59),
            ('z', (ushort)84)
        );

        // Act
        var table = new CmapTable(data);

        // Assert
        Assert.Equal((ushort)33, table.GetGlyphId((ushort)'A'));
        Assert.Equal((ushort)59, table.GetGlyphId((ushort)'a'));
        Assert.Equal((ushort)0, table.GetGlyphId((ushort)'@')); // Not mapped
    }

    [Fact]
    public void CmapTable_GetPreferredUnicodeEncoding_ReturnsWindowsBMP()
    {
        // Arrange
        var data = CreateCmapTableFormat4(('A', (ushort)1));

        // Act
        var table = new CmapTable(data);
        var encoding = table.GetPreferredUnicodeEncoding();

        // Assert
        Assert.NotNull(encoding);
        Assert.Equal(PlatformId.Windows, encoding.Encoding.PlatformId);
        Assert.Equal(WindowsEncodingId.UnicodeBmp, encoding.Encoding.WindowsEncoding);
    }

    #endregion

    #region TrueTypeParser Tests

    [Fact]
    public void TrueTypeParser_ParsesTableDirectory_FindsTables()
    {
        // Arrange
        var fontData = CreateMinimalTrueTypeFont(
            ("head", CreateHeadTableData(version: 0x00010000, fontRevision: 0x00010000, unitsPerEm: 1000)),
            ("maxp", CreateMaxpTableData(version: 0x00010000, numGlyphs: 100))
        );

        // Act
        var parser = new TrueTypeParser(fontData);

        // Assert
        Assert.True(parser.HasTable("head"));
        Assert.True(parser.HasTable("maxp"));
        Assert.False(parser.HasTable("cmap"));
    }

    [Fact]
    public void TrueTypeParser_GetTable_ReturnsCorrectData()
    {
        // Arrange
        var expectedHeadData = CreateHeadTableData(
            version: 0x00010000,
            fontRevision: 0x00010000,
            unitsPerEm: 2048
        );
        var fontData = CreateMinimalTrueTypeFont(
            ("head", expectedHeadData)
        );

        // Act
        var parser = new TrueTypeParser(fontData);
        var headData = parser.GetTable("head");

        // Assert
        Assert.NotNull(headData);
        Assert.Equal(expectedHeadData.Length, headData.Length);
    }

    #endregion

    #region Test Data Helper Methods

    private byte[] CreateHeadTableData(
        uint version,
        uint fontRevision,
        ushort unitsPerEm,
        short xMin = 0,
        short yMin = 0,
        short xMax = 1000,
        short yMax = 1000,
        MacStyle macStyle = MacStyle.Bold,
        IndexToLocFormat indexToLocFormat = IndexToLocFormat.Offset16)
    {
        var writer = new MemoryStream();
        var bw = new BinaryWriter(writer);

        WriteBigEndian(bw, (ushort)(version >> 16));           // majorVersion
        WriteBigEndian(bw, (ushort)(version & 0xFFFF));        // minorVersion
        WriteBigEndian(bw, fontRevision);                      // fontRevision
        WriteBigEndian(bw, 0u);                                 // checkSumAdjustment
        WriteBigEndian(bw, 0x5F0F3CF5u);                        // magicNumber
        WriteBigEndian(bw, (ushort)0);                          // flags
        WriteBigEndian(bw, unitsPerEm);
        WriteBigEndian(bw, 0L);                                 // created
        WriteBigEndian(bw, 0L);                                 // modified
        WriteBigEndian(bw, xMin);
        WriteBigEndian(bw, yMin);
        WriteBigEndian(bw, xMax);
        WriteBigEndian(bw, yMax);
        WriteBigEndian(bw, (ushort)macStyle);
        WriteBigEndian(bw, (ushort)10);                         // lowestRecPPEM
        WriteBigEndian(bw, (short)2);                           // fontDirectionHint
        WriteBigEndian(bw, (short)indexToLocFormat);
        WriteBigEndian(bw, (short)0);                           // glyphDataFormat

        return writer.ToArray();
    }

    private byte[] CreateHheaTableData(
        short ascender,
        short descender,
        short lineGap,
        ushort advanceWidthMax,
        ushort numberOfHMetrics)
    {
        var writer = new MemoryStream();
        var bw = new BinaryWriter(writer);

        WriteBigEndian(bw, (ushort)1);          // majorVersion
        WriteBigEndian(bw, (ushort)0);          // minorVersion
        WriteBigEndian(bw, ascender);
        WriteBigEndian(bw, descender);
        WriteBigEndian(bw, lineGap);
        WriteBigEndian(bw, advanceWidthMax);
        WriteBigEndian(bw, (short)0);           // minLeftSideBearing
        WriteBigEndian(bw, (short)0);           // minRightSideBearing
        WriteBigEndian(bw, (short)0);           // xMaxExtent
        WriteBigEndian(bw, (short)1);           // caretSlopeRise
        WriteBigEndian(bw, (short)0);           // caretSlopeRun
        WriteBigEndian(bw, (short)0);           // caretOffset
        WriteBigEndian(bw, (short)0);           // reserved
        WriteBigEndian(bw, (short)0);           // reserved
        WriteBigEndian(bw, (short)0);           // reserved
        WriteBigEndian(bw, (short)0);           // reserved
        WriteBigEndian(bw, (short)0);           // metricDataFormat
        WriteBigEndian(bw, numberOfHMetrics);

        return writer.ToArray();
    }

    private byte[] CreateMaxpTableData(
        uint version,
        ushort numGlyphs,
        ushort maxPoints = 0,
        ushort maxContours = 0)
    {
        var writer = new MemoryStream();
        var bw = new BinaryWriter(writer);

        WriteBigEndian(bw, version);
        WriteBigEndian(bw, numGlyphs);

        if (version == 0x00010000)
        {
            WriteBigEndian(bw, maxPoints);
            WriteBigEndian(bw, maxContours);
            // Write remaining version 1.0 fields as zeros
            for (int i = 0; i < 11; i++)
                WriteBigEndian(bw, (ushort)0);
        }

        return writer.ToArray();
    }

    private byte[] CreateHmtxTableData(ushort[] advanceWidths, short[] leftSideBearings)
    {
        var writer = new MemoryStream();
        var bw = new BinaryWriter(writer);

        for (int i = 0; i < advanceWidths.Length; i++)
        {
            WriteBigEndian(bw, advanceWidths[i]);
            WriteBigEndian(bw, leftSideBearings[i]);
        }

        return writer.ToArray();
    }

    private byte[] CreateHmtxTableDataWithSharedAdvance(
        ushort[] longMetrics,
        short[] longLsbs,
        short[] additionalLsbs)
    {
        var writer = new MemoryStream();
        var bw = new BinaryWriter(writer);

        for (int i = 0; i < longMetrics.Length; i++)
        {
            WriteBigEndian(bw, longMetrics[i]);
            WriteBigEndian(bw, longLsbs[i]);
        }

        foreach (var lsb in additionalLsbs)
        {
            WriteBigEndian(bw, lsb);
        }

        return writer.ToArray();
    }

    private byte[] CreateNameTableData(params (string nameId, string value)[] names)
    {
        var writer = new MemoryStream();
        var bw = new BinaryWriter(writer);

        WriteBigEndian(bw, (ushort)0);              // format
        WriteBigEndian(bw, (ushort)names.Length);   // count
        WriteBigEndian(bw, (ushort)(6 + names.Length * 12)); // stringOffset

        // Write name records
        ushort offset = 0;
        foreach (var (nameId, value) in names)
        {
            WriteBigEndian(bw, (ushort)PlatformId.Windows);     // platformID
            WriteBigEndian(bw, (ushort)WindowsEncodingId.UnicodeBmp);  // encodingID
            WriteBigEndian(bw, (ushort)0x0409);                 // languageID (en-US)
            WriteBigEndian(bw, GetNameId(nameId));              // nameID
            WriteBigEndian(bw, (ushort)(value.Length * 2));     // length (UTF-16)
            WriteBigEndian(bw, offset);                          // offset
            offset += (ushort)(value.Length * 2);
        }

        // Write string data (UTF-16BE)
        foreach (var (_, value) in names)
        {
            foreach (char c in value)
                WriteBigEndian(bw, (ushort)c);
        }

        return writer.ToArray();
    }

    private byte[] CreateNameTableWithMultiplePlatforms(string macFamily, string windowsFamily)
    {
        var writer = new MemoryStream();
        var bw = new BinaryWriter(writer);

        WriteBigEndian(bw, (ushort)0);      // format
        WriteBigEndian(bw, (ushort)2);      // count
        WriteBigEndian(bw, (ushort)30);     // stringOffset

        // Macintosh record
        WriteBigEndian(bw, (ushort)PlatformId.Macintosh);
        WriteBigEndian(bw, (ushort)MacintoshEncodingId.Roman);
        WriteBigEndian(bw, (ushort)0);
        WriteBigEndian(bw, (ushort)1);      // Family nameID
        WriteBigEndian(bw, (ushort)macFamily.Length);
        WriteBigEndian(bw, (ushort)0);

        // Windows record
        WriteBigEndian(bw, (ushort)PlatformId.Windows);
        WriteBigEndian(bw, (ushort)WindowsEncodingId.UnicodeBmp);
        WriteBigEndian(bw, (ushort)0x0409);
        WriteBigEndian(bw, (ushort)1);      // Family nameID
        WriteBigEndian(bw, (ushort)(windowsFamily.Length * 2));
        WriteBigEndian(bw, (ushort)macFamily.Length);

        // String data
        foreach (char c in macFamily)
            bw.Write((byte)c);
        foreach (char c in windowsFamily)
            WriteBigEndian(bw, (ushort)c);

        return writer.ToArray();
    }

    private byte[] CreateCmapTableFormat4(params (char character, ushort glyphId)[] mappings)
    {
        var writer = new MemoryStream();
        var bw = new BinaryWriter(writer);

        // Cmap header
        WriteBigEndian(bw, (ushort)0);      // version
        WriteBigEndian(bw, (ushort)1);      // numTables

        // Encoding record
        WriteBigEndian(bw, (ushort)PlatformId.Windows);
        WriteBigEndian(bw, (ushort)WindowsEncodingId.UnicodeBmp);
        WriteBigEndian(bw, 12u);            // offset to subtable

        // Group mappings into segments by consecutive ranges with same delta
        var segments = new List<(ushort start, ushort end, short delta)>();
        var sorted = mappings.OrderBy(m => m.character).ToArray();

        ushort segStart = (ushort)sorted[0].character;
        ushort segEnd = (ushort)sorted[0].character;
        short segDelta = (short)(sorted[0].glyphId - sorted[0].character);

        for (int i = 1; i < sorted.Length; i++)
        {
            ushort charCode = (ushort)sorted[i].character;
            short delta = (short)(sorted[i].glyphId - sorted[i].character);

            // Check if this continues the current segment
            if (charCode == segEnd + 1 && delta == segDelta)
            {
                segEnd = charCode;
            }
            else
            {
                // Save current segment and start new one
                segments.Add((segStart, segEnd, segDelta));
                segStart = charCode;
                segEnd = charCode;
                segDelta = delta;
            }
        }
        segments.Add((segStart, segEnd, segDelta)); // Add last segment

        ushort segCount = (ushort)(segments.Count + 1); // +1 for terminator

        // Format 4 subtable
        WriteBigEndian(bw, (ushort)4);      // format
        ushort length = (ushort)(16 + segCount * 8);
        WriteBigEndian(bw, length);         // length
        WriteBigEndian(bw, (ushort)0);      // language

        WriteBigEndian(bw, (ushort)(segCount * 2));  // segCountX2
        ushort searchRange = (ushort)(2 * (1 << (int)Math.Floor(Math.Log(segCount, 2))));
        WriteBigEndian(bw, searchRange);
        WriteBigEndian(bw, (ushort)Math.Floor(Math.Log(segCount, 2)));  // entrySelector
        WriteBigEndian(bw, (ushort)(segCount * 2 - searchRange));       // rangeShift

        // endCode array
        foreach (var seg in segments)
            WriteBigEndian(bw, seg.end);
        WriteBigEndian(bw, (ushort)0xFFFF); // Terminator segment

        WriteBigEndian(bw, (ushort)0);      // reservedPad

        // startCode array
        foreach (var seg in segments)
            WriteBigEndian(bw, seg.start);
        WriteBigEndian(bw, (ushort)0xFFFF);

        // idDelta array
        foreach (var seg in segments)
            WriteBigEndian(bw, seg.delta);
        WriteBigEndian(bw, (short)1);

        // idRangeOffset array (all zeros - using delta method)
        for (int i = 0; i < segCount; i++)
            WriteBigEndian(bw, (ushort)0);

        return writer.ToArray();
    }

    private byte[] CreateMinimalTrueTypeFont(params (string tag, byte[] data)[] tables)
    {
        var writer = new MemoryStream();
        var bw = new BinaryWriter(writer);

        // Write SFNT header
        WriteBigEndian(bw, 0x00010000u);    // version (TrueType)
        WriteBigEndian(bw, (ushort)tables.Length);  // numTables
        WriteBigEndian(bw, (ushort)128);    // searchRange
        WriteBigEndian(bw, (ushort)3);      // entrySelector
        WriteBigEndian(bw, (ushort)0);      // rangeShift

        // Calculate offsets
        uint offset = (uint)(12 + tables.Length * 16);
        var tableRecords = new List<(string tag, uint checksum, uint offset, uint length)>();

        foreach (var (tag, data) in tables)
        {
            tableRecords.Add((tag, 0, offset, (uint)data.Length));
            offset += (uint)((data.Length + 3) & ~3); // Pad to 4-byte boundary
        }

        // Write table directory
        foreach (var (tag, checksum, tableOffset, length) in tableRecords)
        {
            foreach (char c in tag.PadRight(4))
                bw.Write((byte)c);
            WriteBigEndian(bw, checksum);
            WriteBigEndian(bw, tableOffset);
            WriteBigEndian(bw, length);
        }

        // Write table data
        foreach (var (_, data) in tables)
        {
            bw.Write(data);
            // Pad to 4-byte boundary
            int padding = (4 - (data.Length % 4)) % 4;
            for (int i = 0; i < padding; i++)
                bw.Write((byte)0);
        }

        return writer.ToArray();
    }

    private ushort GetNameId(string nameIdStr)
    {
        return nameIdStr switch
        {
            "Family" => 1,
            "Subfamily" => 2,
            "Full Name" => 4,
            "PostScript Name" => 6,
            _ => 0
        };
    }

    private void WriteBigEndian(BinaryWriter bw, ushort value)
    {
        bw.Write((byte)(value >> 8));
        bw.Write((byte)(value & 0xFF));
    }

    private void WriteBigEndian(BinaryWriter bw, short value)
    {
        WriteBigEndian(bw, (ushort)value);
    }

    private void WriteBigEndian(BinaryWriter bw, uint value)
    {
        bw.Write((byte)(value >> 24));
        bw.Write((byte)((value >> 16) & 0xFF));
        bw.Write((byte)((value >> 8) & 0xFF));
        bw.Write((byte)(value & 0xFF));
    }

    private void WriteBigEndian(BinaryWriter bw, long value)
    {
        WriteBigEndian(bw, (uint)(value >> 32));
        WriteBigEndian(bw, (uint)(value & 0xFFFFFFFF));
    }

    #endregion
}
