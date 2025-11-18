using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Embedded.Tables;
using PdfLibrary.Fonts.Embedded.Tables.TtTables;

namespace PdfLibrary.Tests.Fonts.Embedded;

/// <summary>
/// Tests for the high-level GlyphExtractor API that provides glyph outlines for rendering
/// </summary>
public class GlyphExtractorTests
{
    #region GlyphExtractor Basic Tests

    [Fact]
    public void GlyphExtractor_ExtractsSimpleGlyph_ReturnsOutlineWithContours()
    {
        // Arrange
        byte[] fontData = CreateMinimalTrueTypeFont(numGlyphs: 2);
        var extractor = new GlyphExtractor(fontData, numGlyphs: 2);

        // Act - Extract glyph 0 (triangular glyph with 3 points)
        GlyphOutline? outline = extractor.ExtractGlyph(0);

        // Assert
        Assert.NotNull(outline);
        Assert.Equal(0, outline.GlyphId);
        Assert.False(outline.IsComposite);
        Assert.False(outline.IsEmpty);
        Assert.Single(outline.Contours);
        Assert.Equal(3, outline.Contours[0].Points.Count);
        Assert.Equal(3, outline.TotalPoints);
    }

    [Fact]
    public void GlyphExtractor_ExtractsEmptyGlyph_ReturnsOutlineWithNoContours()
    {
        // Arrange - Font with an empty glyph (like space)
        byte[] fontData = CreateMinimalTrueTypeFontWithEmptyGlyph();
        var extractor = new GlyphExtractor(fontData, numGlyphs: 2);

        // Act - Extract glyph 1 (empty glyph)
        GlyphOutline? outline = extractor.ExtractGlyph(1);

        // Assert
        Assert.NotNull(outline);
        Assert.Equal(1, outline.GlyphId);
        Assert.Empty(outline.Contours);
        Assert.True(outline.IsEmpty);
        Assert.Equal(0, outline.TotalPoints);
        Assert.True(outline.Metrics.AdvanceWidth > 0); // Space has advance width
    }

    [Fact]
    public void GlyphExtractor_InvalidGlyphId_ReturnsNull()
    {
        // Arrange
        byte[] fontData = CreateMinimalTrueTypeFont(numGlyphs: 2);
        var extractor = new GlyphExtractor(fontData, numGlyphs: 2);

        // Act & Assert
        Assert.Null(extractor.ExtractGlyph(-1));
        Assert.Null(extractor.ExtractGlyph(2)); // Out of range
        Assert.Null(extractor.ExtractGlyph(100));
    }

    [Fact]
    public void GlyphExtractor_GetGlyphCount_ReturnsCorrectCount()
    {
        // Arrange
        byte[] fontData = CreateMinimalTrueTypeFont(numGlyphs: 5);
        var extractor = new GlyphExtractor(fontData, numGlyphs: 5);

        // Act & Assert
        Assert.Equal(5, extractor.GlyphCount);
    }

    #endregion

    #region GlyphMetrics Tests

    [Fact]
    public void GlyphExtractor_GetMetrics_ReturnsCorrectAdvanceAndBearing()
    {
        // Arrange
        byte[] fontData = CreateMinimalTrueTypeFont(numGlyphs: 2);
        var extractor = new GlyphExtractor(fontData, numGlyphs: 2);

        // Act
        GlyphMetrics metrics = extractor.GetMetrics(0);

        // Assert
        Assert.True(metrics.AdvanceWidth > 0);
        Assert.True(metrics.XMax > metrics.XMin);
        Assert.True(metrics.YMax > metrics.YMin);
        Assert.True(metrics.Width > 0);
        Assert.True(metrics.Height > 0);
    }

    [Fact]
    public void GlyphMetrics_Scale_CorrectlyScalesValues()
    {
        // Arrange
        var metrics = new GlyphMetrics(
            advanceWidth: 1000,
            leftSideBearing: 100,
            xMin: 100,
            yMin: 0,
            xMax: 900,
            yMax: 1000
        );

        // Act - Scale to 12pt font with 1000 units per em
        var scaled = metrics.Scale(fontSize: 12.0, unitsPerEm: 1000);

        // Assert
        Assert.Equal(12.0, scaled.advanceWidth);
        Assert.Equal(1.2, scaled.lsb);
        Assert.Equal(1.2, scaled.xMin);
        Assert.Equal(0.0, scaled.yMin);
        Assert.Equal(10.8, scaled.xMax);
        Assert.Equal(12.0, scaled.yMax);
    }

    [Fact]
    public void GlyphMetrics_CalculatesWidth_Correctly()
    {
        // Arrange
        var metrics = new GlyphMetrics(
            advanceWidth: 1000,
            leftSideBearing: 100,
            xMin: 100,
            yMin: 0,
            xMax: 900,
            yMax: 1000
        );

        // Act & Assert
        Assert.Equal(800, metrics.Width);
        Assert.Equal(1000, metrics.Height);
    }

    [Fact]
    public void GlyphMetrics_CalculatesRightSideBearing_Correctly()
    {
        // Arrange
        var metrics = new GlyphMetrics(
            advanceWidth: 1000,
            leftSideBearing: 100,
            xMin: 100,
            yMin: 0,
            xMax: 900,
            yMax: 1000
        );

        // Act & Assert
        // RSB = AdvanceWidth - LSB - Width
        // RSB = 1000 - 100 - 800 = 100
        Assert.Equal(100, metrics.RightSideBearing);
    }

    #endregion

    #region ContourPoint Tests

    [Fact]
    public void ContourPoint_OnCurveFlag_CorrectlyIdentifiesPointType()
    {
        // Arrange & Act
        var onCurve = new ContourPoint(100, 200, onCurve: true);
        var offCurve = new ContourPoint(150, 250, onCurve: false);

        // Assert
        Assert.True(onCurve.OnCurve);
        Assert.False(offCurve.OnCurve);
    }

    [Fact]
    public void ContourPoint_Scale_CorrectlyScalesCoordinates()
    {
        // Arrange
        var point = new ContourPoint(1000, 2000, onCurve: true);

        // Act - Scale to 12pt font with 1000 units per em
        var scaled = point.Scale(fontSize: 12.0, unitsPerEm: 1000);

        // Assert
        Assert.Equal(12.0, scaled.x);
        Assert.Equal(24.0, scaled.y);
    }

    [Fact]
    public void ContourPoint_Transform_AppliesMatrixCorrectly()
    {
        // Arrange
        var point = new ContourPoint(100, 100, onCurve: true);

        // Act - Apply identity transformation
        var transformed = point.Transform(
            a: 1.0, b: 0.0,
            c: 0.0, d: 1.0,
            e: 50.0, f: 50.0 // Translation only
        );

        // Assert
        Assert.Equal(150.0, transformed.X);
        Assert.Equal(150.0, transformed.Y);
        Assert.True(transformed.OnCurve);
    }

    [Fact]
    public void ContourPoint_Equality_ComparesWithinTolerance()
    {
        // Arrange
        var point1 = new ContourPoint(100.0001, 200.0001, onCurve: true);
        var point2 = new ContourPoint(100.0, 200.0, onCurve: true);
        var point3 = new ContourPoint(100.1, 200.1, onCurve: true);

        // Act & Assert
        Assert.True(point1.Equals(point2));  // Within tolerance
        Assert.False(point1.Equals(point3)); // Outside tolerance
    }

    #endregion

    #region GlyphContour Tests

    [Fact]
    public void GlyphContour_CountsOnCurvePoints_Correctly()
    {
        // Arrange
        var points = new List<ContourPoint>
        {
            new ContourPoint(0, 0, onCurve: true),
            new ContourPoint(50, 100, onCurve: false), // Control point
            new ContourPoint(100, 0, onCurve: true),
            new ContourPoint(150, 100, onCurve: false), // Control point
            new ContourPoint(200, 0, onCurve: true)
        };
        var contour = new GlyphContour(points);

        // Act & Assert
        Assert.Equal(3, contour.OnCurvePointCount);
        Assert.Equal(2, contour.OffCurvePointCount);
        Assert.True(contour.IsClosed);
    }

    [Fact]
    public void GlyphContour_GetBounds_CalculatesCorrectBoundingBox()
    {
        // Arrange
        var points = new List<ContourPoint>
        {
            new ContourPoint(10, 20, onCurve: true),
            new ContourPoint(100, 50, onCurve: true),
            new ContourPoint(50, 150, onCurve: true)
        };
        var contour = new GlyphContour(points);

        // Act
        var bounds = contour.GetBounds();

        // Assert
        Assert.Equal(10.0, bounds.minX);
        Assert.Equal(20.0, bounds.minY);
        Assert.Equal(100.0, bounds.maxX);
        Assert.Equal(150.0, bounds.maxY);
    }

    [Fact]
    public void GlyphContour_EmptyContour_ReturnsZeroBounds()
    {
        // Arrange
        var contour = new GlyphContour(new List<ContourPoint>());

        // Act
        var bounds = contour.GetBounds();

        // Assert
        Assert.Equal((0.0, 0.0, 0.0, 0.0), bounds);
    }

    #endregion

    #region GlyphOutline Tests

    [Fact]
    public void GlyphOutline_SimpleGlyph_HasCorrectProperties()
    {
        // Arrange
        var contours = new List<GlyphContour>
        {
            new GlyphContour(new List<ContourPoint>
            {
                new ContourPoint(0, 0, true),
                new ContourPoint(100, 0, true),
                new ContourPoint(50, 100, true)
            })
        };
        var metrics = new GlyphMetrics(1000, 100, 0, 0, 100, 100);

        // Act
        var outline = new GlyphOutline(42, contours, metrics);

        // Assert
        Assert.Equal(42, outline.GlyphId);
        Assert.Single(outline.Contours);
        Assert.Equal(3, outline.TotalPoints);
        Assert.False(outline.IsComposite);
        Assert.False(outline.IsEmpty);
    }

    [Fact]
    public void GlyphOutline_EmptyGlyph_IsEmpty()
    {
        // Arrange
        var metrics = new GlyphMetrics(500, 0, 0, 0, 0, 0);

        // Act
        var outline = new GlyphOutline(32, new List<GlyphContour>(), metrics); // Space glyph

        // Assert
        Assert.True(outline.IsEmpty);
        Assert.Equal(0, outline.TotalPoints);
    }

    [Fact]
    public void GlyphOutline_CompositeGlyph_HasComponentIds()
    {
        // Arrange
        var metrics = new GlyphMetrics(1000, 100, 0, 0, 100, 100);
        var componentIds = new List<int> { 10, 20 };

        // Act
        var outline = new GlyphOutline(
            glyphId: 42,
            contours: new List<GlyphContour>(),
            metrics: metrics,
            isComposite: true,
            componentGlyphIds: componentIds
        );

        // Assert
        Assert.True(outline.IsComposite);
        Assert.Equal(2, outline.ComponentGlyphIds.Count);
        Assert.Contains(10, outline.ComponentGlyphIds);
        Assert.Contains(20, outline.ComponentGlyphIds);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void GlyphExtractor_ExtractsMultipleGlyphs_AllHaveValidMetrics()
    {
        // Arrange
        byte[] fontData = CreateMinimalTrueTypeFont(numGlyphs: 3);
        var extractor = new GlyphExtractor(fontData, numGlyphs: 3);

        // Act & Assert
        for (int i = 0; i < 3; i++)
        {
            GlyphOutline? outline = extractor.ExtractGlyph(i);
            Assert.NotNull(outline);
            Assert.Equal(i, outline.GlyphId);
            Assert.NotNull(outline.Metrics);
            Assert.True(outline.Metrics.AdvanceWidth >= 0);
        }
    }

    [Fact]
    public void GlyphExtractor_GetNonEmptyGlyphIds_ReturnsOnlyNonEmpty()
    {
        // Arrange
        byte[] fontData = CreateMinimalTrueTypeFontWithEmptyGlyph();
        var extractor = new GlyphExtractor(fontData, numGlyphs: 2);

        // Act
        var nonEmptyIds = extractor.GetNonEmptyGlyphIds().ToList();

        // Assert
        Assert.Contains(0, nonEmptyIds); // Glyph 0 has contours
        Assert.DoesNotContain(1, nonEmptyIds); // Glyph 1 is empty
    }

    #endregion

    #region Helper Methods - Font Creation

    /// <summary>
    /// Create a minimal valid TrueType font with simple glyphs for testing
    /// </summary>
    private byte[] CreateMinimalTrueTypeFont(int numGlyphs)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // TrueType header
        WriteTableDirectory(bw, numGlyphs);

        return ms.ToArray();
    }

    /// <summary>
    /// Create font with at least one empty glyph
    /// </summary>
    private byte[] CreateMinimalTrueTypeFontWithEmptyGlyph()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // TrueType header with empty glyph pattern
        WriteTableDirectoryWithEmptyGlyph(bw);

        return ms.ToArray();
    }

    private void WriteTableDirectory(BinaryWriter bw, int numGlyphs)
    {
        // Write TrueType header (sfnt version 1.0)
        WriteBigEndian(bw, (uint)0x00010000); // version
        WriteBigEndian(bw, (ushort)6); // numTables (head, hhea, hmtx, loca, glyf, maxp)
        WriteBigEndian(bw, (ushort)0); // searchRange
        WriteBigEndian(bw, (ushort)0); // entrySelector
        WriteBigEndian(bw, (ushort)0); // rangeShift

        // Calculate table offsets
        uint tableRecordSize = 16;
        uint offset = 12 + (6 * tableRecordSize); // After header + 6 table records

        // head table
        byte[] headData = CreateHeadTable();
        WriteTableRecord(bw, "head", offset, headData);
        offset += (uint)headData.Length;

        // hhea table
        byte[] hheaData = CreateHheaTable(numGlyphs);
        WriteTableRecord(bw, "hhea", offset, hheaData);
        offset += (uint)hheaData.Length;

        // hmtx table
        byte[] hmtxData = CreateHmtxTable(numGlyphs);
        WriteTableRecord(bw, "hmtx", offset, hmtxData);
        offset += (uint)hmtxData.Length;

        // maxp table
        byte[] maxpData = CreateMaxpTable(numGlyphs);
        WriteTableRecord(bw, "maxp", offset, maxpData);
        offset += (uint)maxpData.Length;

        // loca table
        byte[] locaData = CreateLocaTable(numGlyphs);
        WriteTableRecord(bw, "loca", offset, locaData);
        offset += (uint)locaData.Length;

        // glyf table
        byte[] glyfData = CreateGlyfTable(numGlyphs);
        WriteTableRecord(bw, "glyf", offset, glyfData);

        // Write actual table data
        bw.Write(headData);
        bw.Write(hheaData);
        bw.Write(hmtxData);
        bw.Write(maxpData);
        bw.Write(locaData);
        bw.Write(glyfData);
    }

    private void WriteTableDirectoryWithEmptyGlyph(BinaryWriter bw)
    {
        // Similar to WriteTableDirectory but with specific glyf/loca setup for empty glyph
        WriteBigEndian(bw, (uint)0x00010000);
        WriteBigEndian(bw, (ushort)6);
        WriteBigEndian(bw, (ushort)0);
        WriteBigEndian(bw, (ushort)0);
        WriteBigEndian(bw, (ushort)0);

        uint offset = 12 + (6 * 16);

        byte[] headData = CreateHeadTable();
        WriteTableRecord(bw, "head", offset, headData);
        offset += (uint)headData.Length;

        byte[] hheaData = CreateHheaTable(2);
        WriteTableRecord(bw, "hhea", offset, hheaData);
        offset += (uint)hheaData.Length;

        byte[] hmtxData = CreateHmtxTableWithEmptyGlyph();
        WriteTableRecord(bw, "hmtx", offset, hmtxData);
        offset += (uint)hmtxData.Length;

        byte[] maxpData = CreateMaxpTable(2);
        WriteTableRecord(bw, "maxp", offset, maxpData);
        offset += (uint)maxpData.Length;

        byte[] locaData = CreateLocaTableWithEmptyGlyph();
        WriteTableRecord(bw, "loca", offset, locaData);
        offset += (uint)locaData.Length;

        byte[] glyfData = CreateGlyfTableWithEmptyGlyph();
        WriteTableRecord(bw, "glyf", offset, glyfData);

        bw.Write(headData);
        bw.Write(hheaData);
        bw.Write(hmtxData);
        bw.Write(maxpData);
        bw.Write(locaData);
        bw.Write(glyfData);
    }

    private void WriteTableRecord(BinaryWriter bw, string tag, uint offset, byte[] data)
    {
        // Table tag (4 bytes)
        bw.Write(System.Text.Encoding.ASCII.GetBytes(tag.PadRight(4)));

        // Checksum (simplified - just use 0)
        WriteBigEndian(bw, (uint)0);

        // Offset and length
        WriteBigEndian(bw, offset);
        WriteBigEndian(bw, (uint)data.Length);
    }

    private byte[] CreateHeadTable()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        WriteBigEndian(bw, (uint)0x00010000); // version
        WriteBigEndian(bw, (uint)0x00010000); // fontRevision
        WriteBigEndian(bw, (uint)0); // checkSumAdjustment
        WriteBigEndian(bw, (uint)0x5F0F3CF5); // magicNumber
        WriteBigEndian(bw, (ushort)0); // flags
        WriteBigEndian(bw, (ushort)1000); // unitsPerEm
        WriteBigEndian(bw, (long)0); // created
        WriteBigEndian(bw, (long)0); // modified
        WriteBigEndian(bw, (short)0); // xMin
        WriteBigEndian(bw, (short)0); // yMin
        WriteBigEndian(bw, (short)1000); // xMax
        WriteBigEndian(bw, (short)1000); // yMax
        WriteBigEndian(bw, (ushort)0); // macStyle
        WriteBigEndian(bw, (ushort)8); // lowestRecPPEM
        WriteBigEndian(bw, (short)2); // fontDirectionHint
        WriteBigEndian(bw, (short)0); // indexToLocFormat (0 = short)
        WriteBigEndian(bw, (short)0); // glyphDataFormat

        return ms.ToArray();
    }

    private byte[] CreateHheaTable(int numGlyphs)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        WriteBigEndian(bw, (uint)0x00010000); // version
        WriteBigEndian(bw, (short)800); // ascent
        WriteBigEndian(bw, (short)-200); // descent
        WriteBigEndian(bw, (short)0); // lineGap
        WriteBigEndian(bw, (ushort)1000); // advanceWidthMax
        WriteBigEndian(bw, (short)0); // minLeftSideBearing
        WriteBigEndian(bw, (short)0); // minRightSideBearing
        WriteBigEndian(bw, (short)1000); // xMaxExtent
        WriteBigEndian(bw, (short)1); // caretSlopeRise
        WriteBigEndian(bw, (short)0); // caretSlopeRun
        WriteBigEndian(bw, (short)0); // caretOffset
        WriteBigEndian(bw, (short)0); // reserved1
        WriteBigEndian(bw, (short)0); // reserved2
        WriteBigEndian(bw, (short)0); // reserved3
        WriteBigEndian(bw, (short)0); // reserved4
        WriteBigEndian(bw, (short)0); // metricDataFormat
        WriteBigEndian(bw, (ushort)numGlyphs); // numberOfHMetrics

        return ms.ToArray();
    }

    private byte[] CreateHmtxTable(int numGlyphs)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // Write metrics for each glyph
        for (int i = 0; i < numGlyphs; i++)
        {
            WriteBigEndian(bw, (ushort)600); // advanceWidth
            WriteBigEndian(bw, (short)50); // leftSideBearing
        }

        return ms.ToArray();
    }

    private byte[] CreateHmtxTableWithEmptyGlyph()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // Glyph 0 - normal glyph
        WriteBigEndian(bw, (ushort)600);
        WriteBigEndian(bw, (short)50);

        // Glyph 1 - empty glyph (like space) with advance but no outline
        WriteBigEndian(bw, (ushort)300);
        WriteBigEndian(bw, (short)0);

        return ms.ToArray();
    }

    private byte[] CreateMaxpTable(int numGlyphs)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        WriteBigEndian(bw, (uint)0x00010000); // version
        WriteBigEndian(bw, (ushort)numGlyphs); // numGlyphs

        return ms.ToArray();
    }

    private byte[] CreateLocaTable(int numGlyphs)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // Short format (divide actual offset by 2)
        // Each glyph is ~30 bytes (header 10 + simple glyph data ~19 + padding)
        ushort offset = 0;
        for (int i = 0; i <= numGlyphs; i++)
        {
            WriteBigEndian(bw, offset);
            offset += 15; // 30 bytes / 2
        }

        return ms.ToArray();
    }

    private byte[] CreateLocaTableWithEmptyGlyph()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        WriteBigEndian(bw, (ushort)0);  // Glyph 0 start
        WriteBigEndian(bw, (ushort)15); // Glyph 0 end / Glyph 1 start
        WriteBigEndian(bw, (ushort)15); // Glyph 1 end (same = empty)

        return ms.ToArray();
    }

    private byte[] CreateGlyfTable(int numGlyphs)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // Create simple triangular glyphs
        for (int i = 0; i < numGlyphs; i++)
        {
            // Header
            bw.Write(CreateGlyphHeader(1, 0, 0, 100, 100));

            // Simple glyph data
            bw.Write(CreateSimpleGlyphData(
                endPtsOfContours: [2],
                instructions: [],
                flags: [0x01, 0x01, 0x01],
                xCoordinates: [0, 100, 50],
                yCoordinates: [0, 0, 100]
            ));

            // Padding
            if (ms.Length % 2 != 0)
                bw.Write((byte)0);
        }

        return ms.ToArray();
    }

    private byte[] CreateGlyfTableWithEmptyGlyph()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // Glyph 0 - normal triangular glyph
        bw.Write(CreateGlyphHeader(1, 0, 0, 100, 100));
        bw.Write(CreateSimpleGlyphData(
            [2], [], [0x01, 0x01, 0x01],
            [0, 100, 50], [0, 0, 100]
        ));

        if (ms.Length % 2 != 0)
            bw.Write((byte)0);

        // Glyph 1 is empty - no data between offsets

        return ms.ToArray();
    }

    private byte[] CreateGlyphHeader(short numberOfContours, short xMin, short yMin, short xMax, short yMax)
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

    private byte[] CreateSimpleGlyphData(
        ushort[] endPtsOfContours,
        byte[] instructions,
        byte[] flags,
        short[] xCoordinates,
        short[] yCoordinates)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        foreach (ushort endPt in endPtsOfContours)
            WriteBigEndian(bw, endPt);

        WriteBigEndian(bw, (ushort)instructions.Length);
        bw.Write(instructions);

        bw.Write(flags);

        foreach (short x in xCoordinates)
            WriteBigEndian(bw, x);

        foreach (short y in yCoordinates)
            WriteBigEndian(bw, y);

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

    private static void WriteBigEndian(BinaryWriter bw, long value)
    {
        WriteBigEndian(bw, (uint)(value >> 32));
        WriteBigEndian(bw, (uint)(value & 0xFFFFFFFF));
    }

    #endregion
}
