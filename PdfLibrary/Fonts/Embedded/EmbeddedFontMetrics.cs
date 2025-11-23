using FontParser.Tables;
using FontParser.Tables.Cff;
using FontParser.Tables.Cff.Type1;
using FontParser.Tables.Cff.Type1.Charsets;
using FontParser.Tables.Cmap;
using FontParser.Tables.Head;
using FontParser.Tables.Hhea;
using FontParser.Tables.Hmtx;
using FontParser.Tables.Name;
using FontParser.Tables.TtTables;
using FontParser.Tables.TtTables.Glyf;
using CffGlyphOutline = FontParser.Tables.Cff.GlyphOutline;
using Range1 = FontParser.Tables.Cff.Type1.Charsets.Range1;

namespace PdfLibrary.Fonts.Embedded;

/// <summary>
/// Provides access to embedded TrueType/OpenType font metrics
/// Parses font tables and exposes rendering-critical information
/// </summary>
public class EmbeddedFontMetrics
{
    private readonly TrueTypeParser _parser;
    private readonly HeadTable? _headTable;
    private readonly HheaTable? _hheaTable;
    private readonly MaxPTable? _maxpTable;
    private readonly HmtxTable? _hmtxTable;
    private readonly NameTable? _nameTable;
    private readonly CmapTable? _cmapTable;
    private GlyphTable? _glyphTable;
    private LocaTable? _locaTable;
    private bool _glyphTablesLoaded;

    // CFF font support
    private Type1Table? _cffTable;
    private bool _isCffFont;

    /// <summary>
    /// Units per em - critical for scaling glyphs to correct size
    /// </summary>
    public ushort UnitsPerEm { get; }

    /// <summary>
    /// Number of glyphs in the font
    /// </summary>
    public ushort NumGlyphs { get; }

    /// <summary>
    /// Font family name
    /// </summary>
    public string? FamilyName => _nameTable?.GetFamilyName();

    /// <summary>
    /// PostScript font name
    /// </summary>
    public string? PostScriptName => _nameTable?.GetPostScriptName();

    /// <summary>
    /// Font ascender in font units
    /// </summary>
    public short Ascender => _hheaTable?.Ascender ?? 0;

    /// <summary>
    /// Font descender in font units
    /// </summary>
    public short Descender => _hheaTable?.Descender ?? 0;

    /// <summary>
    /// Indicates if all required tables were successfully parsed
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Indicates if this is a CFF (OpenType/CFF) font rather than TrueType
    /// </summary>
    public bool IsCffFont => _isCffFont;

    // Diagnostic properties for cmap table debugging
    public bool HasCmapTable => _cmapTable != null;
    public int GetCmapSubtableCount() => _cmapTable?.SubTables?.Count ?? 0;
    public int GetCmapEncodingRecordCount() => _cmapTable?.EncodingRecords?.Count ?? 0;

    public string GetCmapEncodingRecordInfo(int index)
    {
        if (_cmapTable == null || _cmapTable.EncodingRecords == null || index >= _cmapTable.EncodingRecords.Count)
            return "Invalid index";

        EncodingRecord record = _cmapTable.EncodingRecords[index];
        return $"Platform={record.PlatformId}";
    }

    /// <summary>
    /// Creates embedded font metrics from raw TrueType/OpenType font data
    /// </summary>
    /// <param name="fontData">Raw font bytes (TrueType, OpenType/CFF, or raw CFF)</param>
    public EmbeddedFontMetrics(byte[] fontData)
    {
        // Check for raw CFF data (starts with version 01 00)
        if (fontData is [0x01, 0x00, ..])
        {
            // Raw CFF font data - parse directly
            try
            {
                _cffTable = new Type1Table(fontData);
                _isCffFont = true;
                UnitsPerEm = 1000; // CFF fonts typically use 1000 units per em
                NumGlyphs = (ushort)_cffTable.RawCharStrings.Count;
                IsValid = true;

                // Create a dummy parser (won't be used for CFF)
                _parser = new TrueTypeParser(fontData);
                return;
            }
            catch
            {
                // CFF parsing failed, try as TrueType below
                _isCffFont = false;
            }
        }

        _parser = new TrueTypeParser(fontData);

        // Parse head table (required)
        byte[]? headData = _parser.GetTable("head");
        if (headData != null)
        {
            try
            {
                _headTable = new HeadTable(headData);
                UnitsPerEm = _headTable.UnitsPerEm;
            }
            catch
            {
                UnitsPerEm = 1000; // Fallback default
            }
        }
        else
        {
            UnitsPerEm = 1000; // Fallback default
        }

        // Parse maxp table (required for glyph count)
        byte[]? maxpData = _parser.GetTable("maxp");
        if (maxpData != null)
        {
            try
            {
                _maxpTable = new MaxPTable(maxpData);
                NumGlyphs = _maxpTable.NumGlyphs;
            }
            catch
            {
                // MaxP table parse failed
            }
        }

        // Parse hhea table (required for horizontal metrics)
        byte[]? hheaData = _parser.GetTable("hhea");
        if (hheaData != null)
        {
            try
            {
                _hheaTable = new HheaTable(hheaData);
            }
            catch
            {
                // Hhea table parse failed
            }
        }

        // Parse hmtx table (required for glyph widths)
        byte[]? hmtxData = _parser.GetTable("hmtx");
        if (hmtxData != null && _hheaTable != null && NumGlyphs > 0)
        {
            try
            {
                _hmtxTable = new HmtxTable(hmtxData);
                _hmtxTable.Process(_hheaTable.NumberOfHMetrics, NumGlyphs);
            }
            catch
            {
                // Hmtx table parse failed
            }
        }

        // Parse name table (optional but useful)
        byte[]? nameData = _parser.GetTable("name");
        if (nameData != null)
        {
            try
            {
                _nameTable = new NameTable(nameData);
            }
            catch
            {
                // Name table parse failed
            }
        }

        // Parse cmap table (required for character->glyph mapping)
        byte[]? cmapData = _parser.GetTable("cmap");
        if (cmapData != null)
        {
            try
            {
                _cmapTable = new CmapTable(cmapData);
            }
            catch
            {
                _cmapTable = null;
            }
        }

        // Check for CFF font (OpenType with CFF outlines)
        byte[]? cffData = _parser.GetTable("CFF ");
        if (cffData != null)
        {
            try
            {
                _cffTable = new Type1Table(cffData);
                _isCffFont = true;
            }
            catch
            {
                // CFF parsing failed, treat as invalid
                _cffTable = null;
                _isCffFont = false;
            }
        }

        // Font is valid if we have the essential tables for rendering
        // cmap is optional - Type0/CID fonts use CIDToGIDMap instead
        // We need head + hmtx + either glyf (TrueType) or CFF outlines
        bool hasGlyphData = _parser.GetTable("glyf") != null || _cffTable != null;
        IsValid = _headTable != null && _hmtxTable != null && hasGlyphData;
    }

    /// <summary>
    /// Gets the glyph ID for a character code
    /// </summary>
    /// <param name="charCode">Character code (can be Unicode or font-specific encoding)</param>
    /// <returns>Glyph ID, or 0 if not found</returns>
    public ushort GetGlyphId(ushort charCode)
    {
        // For CFF fonts without cmap table, use direct character code mapping
        // This works for subset fonts where character codes map directly to glyph indices
        if (_isCffFont && _cmapTable == null)
        {
            // Character code is the glyph index for subset fonts
            if (charCode < NumGlyphs)
                return charCode;
            return 0;
        }

        return _cmapTable?.GetGlyphId(charCode) ?? 0;
    }

    /// <summary>
    /// Gets the glyph ID for a glyph name (used for CFF fonts)
    /// </summary>
    /// <param name="glyphName">Glyph name (e.g., "o", "C", "space")</param>
    /// <returns>Glyph ID, or 0 if not found</returns>
    public ushort GetGlyphIdByName(string glyphName)
    {
        if (string.IsNullOrEmpty(glyphName))
            return 0;

        // For CFF fonts, use the charset to map glyph name to glyph index
        if (_isCffFont && _cffTable != null)
        {
            // First, convert glyph name to SID
            int sid = GetSidFromGlyphName(glyphName);
            if (sid < 0)
                return 0;

            // Search the charset for the glyph index with this SID
            // Glyph 0 is always .notdef (SID 0)
            if (sid == 0)
                return 0;

            // Get the charset from the CFF table
            ICharset? charset = _cffTable.CharSet;
            if (charset == null)
                return 0;

            // Different charset formats store data differently
            if (charset is CharsetsFormat0 format0)
            {
                // Format 0: simple array where Glyphs[glyphIndex-1] = SID
                // (glyph 0 is .notdef and not in the array)
                for (var i = 0; i < format0.Glyphs.Count; i++)
                {
                    if (format0.Glyphs[i] == sid)
                        return (ushort)(i + 1); // +1 because .notdef is not in list
                }
            }
            else if (charset is CharsetsFormat1 format1)
            {
                // Format 1: ranges with SID and count
                var glyphIndex = 1; // Start at 1 (.notdef is 0)
                foreach (Range1 range in format1.Ranges)
                {
                    for (var i = 0; i <= range.NumberLeft; i++)
                    {
                        if (range.First + i == sid)
                            return (ushort)glyphIndex;
                        glyphIndex++;
                    }
                }
            }
            else if (charset is CharsetsFormat2 format2)
            {
                // Format 2: ranges with larger count field
                var glyphIndex = 1; // Start at 1 (.notdef is 0)
                foreach (Range2 range in format2.Ranges)
                {
                    for (var i = 0; i <= range.NumberLeft; i++)
                    {
                        if (range.First + i == sid)
                            return (ushort)glyphIndex;
                        glyphIndex++;
                    }
                }
            }

            return 0;
        }

        // For TrueType fonts, fall back to cmap lookup
        // This is not ideal but provides some compatibility
        return 0;
    }

    /// <summary>
    /// Convert a glyph name to its Standard String ID (SID)
    /// </summary>
    private int GetSidFromGlyphName(string glyphName)
    {
        // Check standard strings first (SID 0-390)
        for (var i = 0; i <= 390; i++)
        {
            if (StandardStrings.GetString(i) == glyphName)
                return i;
        }

        // Check custom strings in the CFF font
        if (_cffTable?.Strings != null)
        {
            for (var i = 0; i < _cffTable.Strings.Count; i++)
            {
                if (_cffTable.Strings[i] == glyphName)
                    return 391 + i; // Custom strings start at SID 391
            }
        }

        return -1; // Not found
    }

    /// <summary>
    /// Gets the advance width for a glyph in font units
    /// </summary>
    /// <param name="glyphId">Glyph ID</param>
    /// <returns>Advance width in font units</returns>
    public ushort GetAdvanceWidth(ushort glyphId)
    {
        return _hmtxTable?.GetAdvanceWidth(glyphId) ?? 0;
    }

    /// <summary>
    /// Gets the left side bearing for a glyph in font units
    /// </summary>
    /// <param name="glyphId">Glyph ID</param>
    /// <returns>Left side bearing in font units</returns>
    public short GetLeftSideBearing(ushort glyphId)
    {
        return _hmtxTable?.GetLeftSideBearing(glyphId) ?? 0;
    }

    /// <summary>
    /// Gets the advance width for a character code in font units
    /// Combines character->glyph mapping with glyph metrics
    /// </summary>
    /// <param name="charCode">Character code</param>
    /// <returns>Advance width in font units, or 0 if character not found</returns>
    public ushort GetCharacterAdvanceWidth(ushort charCode)
    {
        ushort glyphId = GetGlyphId(charCode);
        if (glyphId == 0)
            return 0;

        return GetAdvanceWidth(glyphId);
    }

    /// <summary>
    /// Scales a font unit value to user units
    /// </summary>
    /// <param name="fontUnitValue">Value in font units</param>
    /// <param name="fontSize">Font size in user units</param>
    /// <returns>Value in user units</returns>
    public double ScaleToUserUnits(double fontUnitValue, double fontSize)
    {
        return fontUnitValue * fontSize / UnitsPerEm;
    }

    /// <summary>
    /// Get the glyph outline for a specific glyph ID
    /// </summary>
    /// <param name="glyphId">Glyph ID from the font's cmap table</param>
    /// <returns>GlyphOutline with contour data, or null if glyph not found or invalid</returns>
    public GlyphOutline? GetGlyphOutline(ushort glyphId)
    {
        // Handle CFF fonts
        if (_isCffFont && _cffTable != null)
        {
            return GetCffGlyphOutline(glyphId);
        }

        // TrueType font handling
        // Ensure glyph tables are loaded
        if (!_glyphTablesLoaded)
        {
            LoadGlyphTables();
        }

        // If tables failed to load or are invalid, return null
        if (_glyphTable == null || _locaTable == null)
            return null;

        // Get glyph data
        GlyphData? glyphData = _glyphTable.GetGlyphData(glyphId);
        if (glyphData == null)
            return null;

        // Get glyph metrics
        ushort advanceWidth = GetAdvanceWidth(glyphId);
        short leftSideBearing = GetLeftSideBearing(glyphId);

        var metrics = new GlyphMetrics(
            advanceWidth,
            leftSideBearing,
            glyphData.Header.XMin,
            glyphData.Header.YMin,
            glyphData.Header.XMax,
            glyphData.Header.YMax
        );

        // Convert SimpleGlyph to GlyphOutline
        if (glyphData.GlyphSpec is SimpleGlyph simpleGlyph)
        {
            List<GlyphContour> contours = ConvertSimpleGlyphToContours(simpleGlyph);
            return new GlyphOutline(glyphId, contours, metrics, isComposite: false);
        }

        // For composite glyphs, recursively resolve components
        if (glyphData.GlyphSpec is CompositeGlyph compositeGlyph)
        {
            return ExtractCompositeGlyph(glyphId, compositeGlyph, metrics);
        }

        return null;
    }

    /// <summary>
    /// Get the raw CFF glyph outline for direct rendering with cubic Bezier curves.
    /// Returns null for TrueType fonts or if the glyph is not found.
    /// </summary>
    /// <param name="glyphId">Glyph ID from the font's cmap table</param>
    /// <returns>CFF GlyphOutline with path commands, or null</returns>
    public CffGlyphOutline? GetCffGlyphOutlineDirect(ushort glyphId)
    {
        if (!_isCffFont || _cffTable == null)
            return null;

        return _cffTable.GetGlyphOutline(glyphId);
    }

    /// <summary>
    /// Get glyph outline for a CFF font
    /// Converts CFF path commands to contour-based GlyphOutline
    /// </summary>
    private GlyphOutline? GetCffGlyphOutline(ushort glyphId)
    {
        if (_cffTable == null)
            return null;

        // Get CFF glyph outline from Type1Table
        CffGlyphOutline? cffOutline = _cffTable.GetGlyphOutline(glyphId);
        if (cffOutline == null)
            return null;

        // Get glyph metrics
        ushort advanceWidth = GetAdvanceWidth(glyphId);
        short leftSideBearing = GetLeftSideBearing(glyphId);

        // Use bounding box from CFF outline if available
        var metrics = new GlyphMetrics(
            advanceWidth,
            leftSideBearing,
            (short)cffOutline.MinX,
            (short)cffOutline.MinY,
            (short)cffOutline.MaxX,
            (short)cffOutline.MaxY
        );

        // Convert CFF path commands to contours
        List<GlyphContour> contours = ConvertCffCommandsToContours(cffOutline);

        return new GlyphOutline(glyphId, contours, metrics, isComposite: false);
    }

    /// <summary>
    /// Convert CFF path commands to contour-based representation
    /// </summary>
    private List<GlyphContour> ConvertCffCommandsToContours(CffGlyphOutline cffOutline)
    {
        var contours = new List<GlyphContour>();
        var currentContour = new List<ContourPoint>();
        float currentX = 0, currentY = 0;

        foreach (PathCommand command in cffOutline.Commands)
        {
            switch (command)
            {
                case MoveToCommand moveTo:
                    // Start a new contour
                    if (currentContour.Count > 0)
                    {
                        contours.Add(new GlyphContour(currentContour, isClosed: true));
                        currentContour = [];
                    }
                    currentX = moveTo.Point.X;
                    currentY = moveTo.Point.Y;
                    currentContour.Add(new ContourPoint(currentX, currentY, onCurve: true));
                    break;

                case LineToCommand lineTo:
                    currentX = lineTo.Point.X;
                    currentY = lineTo.Point.Y;
                    currentContour.Add(new ContourPoint(currentX, currentY, onCurve: true));
                    break;

                case CubicBezierCommand cubic:
                    // For cubic Bezier, we store control points as off-curve
                    // and the endpoint as on-curve
                    // Note: This is a simplification - proper rendering should use
                    // the cubic converter directly for better accuracy
                    currentContour.Add(new ContourPoint(cubic.Control1.X, cubic.Control1.Y, onCurve: false));
                    currentContour.Add(new ContourPoint(cubic.Control2.X, cubic.Control2.Y, onCurve: false));
                    currentX = cubic.EndPoint.X;
                    currentY = cubic.EndPoint.Y;
                    currentContour.Add(new ContourPoint(currentX, currentY, onCurve: true));
                    break;

                case ClosePathCommand:
                    if (currentContour.Count > 0)
                    {
                        contours.Add(new GlyphContour(currentContour, isClosed: true));
                        currentContour = [];
                    }
                    break;
            }
        }

        // Add any remaining contour
        if (currentContour.Count > 0)
        {
            contours.Add(new GlyphContour(currentContour, isClosed: true));
        }

        return contours;
    }

    /// <summary>
    /// Recursively extract and compose a composite glyph
    /// </summary>
    private GlyphOutline ExtractCompositeGlyph(ushort glyphId, CompositeGlyph compositeGlyph, GlyphMetrics metrics)
    {
        var allContours = new List<GlyphContour>();
        var componentIds = new List<int>();

        // Recursively extract and transform each component
        foreach (CompositeGlyphComponent component in compositeGlyph.Components)
        {
            componentIds.Add(component.GlyphIndex);

            // Recursively extract the component glyph
            GlyphOutline? componentOutline = GetGlyphOutline(component.GlyphIndex);
            if (componentOutline == null || componentOutline.IsEmpty)
                continue;

            // Transform each contour of the component using the transformation matrix
            foreach (GlyphContour contour in componentOutline.Contours)
            {
                var transformedPoints = new List<ContourPoint>();

                foreach (ContourPoint point in contour.Points)
                {
                    // Apply transformation matrix and offset
                    double x = point.X * component.A + point.Y * component.C + component.Argument1;
                    double y = point.X * component.B + point.Y * component.D + component.Argument2;

                    transformedPoints.Add(new ContourPoint(x, y, point.OnCurve));
                }

                allContours.Add(new GlyphContour(transformedPoints, contour.IsClosed));
            }
        }

        return new GlyphOutline(
            glyphId,
            allContours,
            metrics,
            isComposite: true,
            componentGlyphIds: componentIds
        );
    }

    /// <summary>
    /// Load glyph and loca tables for outline extraction
    /// </summary>
    private void LoadGlyphTables()
    {
        _glyphTablesLoaded = true;

        try
        {
            // Parse loca table (required for glyph offsets)
            byte[]? locaData = _parser.GetTable("loca");
            if (locaData == null || _headTable == null || NumGlyphs == 0)
                return;

            _locaTable = new LocaTable(locaData);
            bool isShortFormat = _headTable.IndexToLocFormat == IndexToLocFormat.Offset16;
            _locaTable.Process(NumGlyphs, isShortFormat);

            // Parse glyf table (contains glyph outlines)
            byte[]? glyfData = _parser.GetTable("glyf");
            if (glyfData == null)
                return;

            _glyphTable = new GlyphTable(glyfData);
            _glyphTable.Process(NumGlyphs, _locaTable);
        }
        catch
        {
            // If parsing fails, leave tables as null
            _glyphTable = null;
            _locaTable = null;
        }
    }

    /// <summary>
    /// Convert a SimpleGlyph to a list of GlyphContours
    /// </summary>
    private List<GlyphContour> ConvertSimpleGlyphToContours(SimpleGlyph simpleGlyph)
    {
        var contours = new List<GlyphContour>();

        if (simpleGlyph.Coordinates.Count == 0 || simpleGlyph.EndPtsOfContours.Count == 0)
            return contours;

        var startIndex = 0;

        foreach (ushort endPtIndex in simpleGlyph.EndPtsOfContours)
        {
            var contourPoints = new List<ContourPoint>();

            // Extract points for this contour
            for (int i = startIndex; i <= endPtIndex && i < simpleGlyph.Coordinates.Count; i++)
            {
                SimpleGlyphCoordinate coord = simpleGlyph.Coordinates[i];
                contourPoints.Add(new ContourPoint(coord.Point.X, coord.Point.Y, coord.OnCurve));
            }

            if (contourPoints.Count > 0)
            {
                contours.Add(new GlyphContour(contourPoints, isClosed: true));
            }

            startIndex = endPtIndex + 1;
        }

        return contours;
    }
}
