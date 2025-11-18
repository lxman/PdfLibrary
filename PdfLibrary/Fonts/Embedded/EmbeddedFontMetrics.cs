using FontParser.Tables;
using FontParser.Tables.Cmap;
using FontParser.Tables.Head;
using FontParser.Tables.Hhea;
using FontParser.Tables.Hmtx;
using FontParser.Tables.Name;
using FontParser.Tables.TtTables;
using FontParser.Tables.TtTables.Glyf;

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
    /// Creates embedded font metrics from raw TrueType/OpenType font data
    /// </summary>
    /// <param name="fontData">Raw font bytes (TrueType or OpenType/CFF)</param>
    public EmbeddedFontMetrics(byte[] fontData)
    {
        _parser = new TrueTypeParser(fontData);

        // Parse head table (required)
        byte[]? headData = _parser.GetTable("head");
        if (headData != null)
        {
            _headTable = new HeadTable(headData);
            UnitsPerEm = _headTable.UnitsPerEm;
        }
        else
        {
            UnitsPerEm = 1000; // Fallback default
        }

        // Parse maxp table (required for glyph count)
        byte[]? maxpData = _parser.GetTable("maxp");
        if (maxpData != null)
        {
            _maxpTable = new MaxPTable(maxpData);
            NumGlyphs = _maxpTable.NumGlyphs;
        }

        // Parse hhea table (required for horizontal metrics)
        byte[]? hheaData = _parser.GetTable("hhea");
        if (hheaData != null)
        {
            _hheaTable = new HheaTable(hheaData);
        }

        // Parse hmtx table (required for glyph widths)
        byte[]? hmtxData = _parser.GetTable("hmtx");
        if (hmtxData != null && _hheaTable != null && NumGlyphs > 0)
        {
            _hmtxTable = new HmtxTable(hmtxData);
            _hmtxTable.Process(_hheaTable.NumberOfHMetrics, NumGlyphs);
        }

        // Parse name table (optional but useful)
        byte[]? nameData = _parser.GetTable("name");
        if (nameData != null)
        {
            _nameTable = new NameTable(nameData);
        }

        // Parse cmap table (required for character->glyph mapping)
        byte[]? cmapData = _parser.GetTable("cmap");
        if (cmapData != null)
        {
            _cmapTable = new CmapTable(cmapData);
        }

        // Font is valid if we have the essential tables
        IsValid = _headTable != null && _hmtxTable != null && _cmapTable != null;
    }

    /// <summary>
    /// Gets the glyph ID for a character code
    /// </summary>
    /// <param name="charCode">Character code (can be Unicode or font-specific encoding)</param>
    /// <returns>Glyph ID, or 0 if not found</returns>
    public ushort GetGlyphId(ushort charCode)
    {
        return _cmapTable?.GetGlyphId(charCode) ?? 0;
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
            var contours = ConvertSimpleGlyphToContours(simpleGlyph);
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
            foreach (var contour in componentOutline.Contours)
            {
                var transformedPoints = new List<ContourPoint>();

                foreach (var point in contour.Points)
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

        int startIndex = 0;

        foreach (ushort endPtIndex in simpleGlyph.EndPtsOfContours)
        {
            var contourPoints = new List<ContourPoint>();

            // Extract points for this contour
            for (int i = startIndex; i <= endPtIndex && i < simpleGlyph.Coordinates.Count; i++)
            {
                var coord = simpleGlyph.Coordinates[i];
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
