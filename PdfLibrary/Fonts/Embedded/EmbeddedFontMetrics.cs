using FontParser.Tables;
using FontParser.Tables.Cff;
using FontParser.Tables.Cff.Type1;
using FontParser.Tables.Cff.Type1.Charsets;
using FontParser.Tables.Cmap;
using FontParser.Tables.Head;
using FontParser.Tables.Hhea;
using FontParser.Tables.Hmtx;
using FontParser.Tables.Name;
using FontParser.Tables.PostScriptType1;
using FontParser.Tables.TtTables;
using FontParser.Tables.TtTables.Glyf;
using Logging;
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
    private readonly Type1Table? _cffTable;
    private readonly bool _isCffFont;

    // PostScript Type1 font support
    private readonly Type1FontParser? _type1Parser;
    private readonly bool _isType1Font;

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

    /// <summary>
    /// Indicates if this is a PostScript Type1 font
    /// </summary>
    public bool IsType1Font => _isType1Font;

    /// <summary>
    /// Gets the nominal width (default width) for CFF glyphs that don't specify explicit widths
    /// </summary>
    /// <returns>NominalWidthX in font units, or 0 if not a CFF font</returns>
    public int GetNominalWidthX()
    {
        return _cffTable?.NominalWidthX ?? 0;
    }

    // Diagnostic properties for cmap table debugging
    public bool HasCmapTable => _cmapTable is not null;
    public int GetCmapSubtableCount() => _cmapTable?.SubTables?.Count ?? 0;
    public int GetCmapEncodingRecordCount() => _cmapTable?.EncodingRecords?.Count ?? 0;

    public string GetCmapEncodingRecordInfo(int index)
    {
        if (_cmapTable?.EncodingRecords is null || index >= _cmapTable.EncodingRecords.Count)
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

                // Calculate UnitsPerEm from FontMatrix instead of hardcoding
                // FontMatrix[0] represents the scale from glyph units to text space: 1/UnitsPerEm
                List<double>? fontMatrix = _cffTable.FontMatrix;
                if (fontMatrix is not null && fontMatrix.Count >= 4 && fontMatrix[0] > 0)
                {
                    UnitsPerEm = (ushort)Math.Round(1.0 / fontMatrix[0]);
                    string matrixStr = string.Join(", ", fontMatrix);
                    PdfLogger.Log(LogCategory.Text, $"[FONTMATRIX] CFF FontMatrix: [{matrixStr}], calculated UnitsPerEm: {UnitsPerEm}, NominalWidthX: {_cffTable.NominalWidthX}");
                }
                else
                {
                    // Default: 1/0.001 = 1000
                    UnitsPerEm = 1000;
                    PdfLogger.Log(LogCategory.Text, $"[FONTMATRIX] CFF FontMatrix: null (using default [0.001, 0, 0, 0.001, 0, 0]), UnitsPerEm: 1000, NominalWidthX: {_cffTable.NominalWidthX}");
                }

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
        if (headData is not null)
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
        if (maxpData is not null)
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
        if (hheaData is not null)
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
        if (hmtxData is not null && _hheaTable is not null && NumGlyphs > 0)
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
        if (nameData is not null)
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
        if (cmapData is not null)
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
        if (cffData is not null)
        {
            try
            {
                _cffTable = new Type1Table(cffData);
                _isCffFont = true;

                // Log FontMatrix for debugging
                List<double>? fontMatrix = _cffTable.FontMatrix;
                if (fontMatrix is not null && fontMatrix.Count >= 4 && fontMatrix[0] > 0)
                {
                    string matrixStr = string.Join(", ", fontMatrix);
                    var calculatedUnitsPerEm = (ushort)Math.Round(1.0 / fontMatrix[0]);
                    PdfLogger.Log(LogCategory.Text, $"[FONTMATRIX] OpenType CFF FontMatrix: [{matrixStr}], calculated UnitsPerEm: {calculatedUnitsPerEm}, head table UnitsPerEm: {UnitsPerEm}, NominalWidthX: {_cffTable.NominalWidthX}");
                }
                else
                {
                    PdfLogger.Log(LogCategory.Text, $"[FONTMATRIX] OpenType CFF FontMatrix: null (using default [0.001, 0, 0, 0.001, 0, 0]), head table UnitsPerEm: {UnitsPerEm}, NominalWidthX: {_cffTable?.NominalWidthX}");
                }
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
        bool hasGlyphData = _parser.GetTable("glyf") is not null || _cffTable is not null;
        IsValid = _headTable is not null && _hmtxTable is not null && hasGlyphData;
    }

    /// <summary>
    /// Creates embedded font metrics from PostScript Type1 font data with Length1/Length2/Length3 parameters
    /// </summary>
    /// <param name="fontData">Raw font bytes from PDF FontFile stream</param>
    /// <param name="length1">Length of ASCII portion</param>
    /// <param name="length2">Length of binary/eexec portion</param>
    /// <param name="length3">Length of trailer portion (optional)</param>
    public EmbeddedFontMetrics(byte[] fontData, int length1, int length2, int length3 = 0)
    {
        // Initialize non-nullable parser field (won't be used for Type1)
        _parser = null!;

        PdfLogger.Log(LogCategory.Text,
            $"[TYPE1] Attempting to parse Type1 font: dataLen={fontData.Length}, Length1={length1}, Length2={length2}, Length3={length3}");

        // Log first few bytes to see the format
        if (fontData.Length > 0)
        {
            var firstBytes = BitConverter.ToString(fontData, 0, Math.Min(32, fontData.Length));
            PdfLogger.Log(LogCategory.Text, $"[TYPE1] First 32 bytes: {firstBytes}");
        }

        try
        {
            // Check for PFB format (starts with 0x80)
            if (fontData.Length > 0 && fontData[0] == 0x80)
            {
                PdfLogger.Log(LogCategory.Text, "[TYPE1] Detected PFB format");
                // PFB format - the parser handles this directly
                _type1Parser = new Type1FontParser(fontData);
            }
            else if (length1 > 0 && length2 > 0)
            {
                PdfLogger.Log(LogCategory.Text, "[TYPE1] Using Length1/Length2 format");

                // Check for eexec in the data to understand the format better
                string textPreview = System.Text.Encoding.ASCII.GetString(fontData);
                int eexecIdx = textPreview.IndexOf("eexec", StringComparison.Ordinal);
                PdfLogger.Log(LogCategory.Text, $"[TYPE1] eexec found at index: {eexecIdx}");
                if (eexecIdx > 0)
                {
                    // Show what comes after eexec
                    int previewStart = Math.Min(eexecIdx + 5, textPreview.Length);
                    int previewLen = Math.Min(100, textPreview.Length - previewStart);
                    string afterEexec = textPreview.Substring(previewStart, previewLen);
                    PdfLogger.Log(LogCategory.Text, $"[TYPE1] After eexec: '{afterEexec.Replace("\r", "\\r").Replace("\n", "\\n")}'");
                }

                // Raw PDF FontFile stream format with Length1/Length2/Length3
                _type1Parser = new Type1FontParser(fontData, length1, length2, length3);
            }
            else
            {
                PdfLogger.Log(LogCategory.Text, "[TYPE1] Trying as PFB format (fallback)");
                // Try as PFB format (fallback)
                _type1Parser = new Type1FontParser(fontData);
            }

            if (_type1Parser.IsValid)
            {
                _isType1Font = true;
                UnitsPerEm = (ushort)_type1Parser.UnitsPerEm;
                NumGlyphs = (ushort)_type1Parser.GlyphCount;
                IsValid = true;

                PdfLogger.Log(LogCategory.Text,
                    $"[TYPE1] Parsed Type1 font '{_type1Parser.FontName}' with {NumGlyphs} glyphs, UnitsPerEm={UnitsPerEm}");
            }
            else
            {
                _isType1Font = false;
                IsValid = false;
                PdfLogger.Log(LogCategory.Text, $"[TYPE1] Type1 font parsing failed - GlyphCount={_type1Parser.GlyphCount}");
            }
        }
        catch (Exception ex)
        {
            _isType1Font = false;
            IsValid = false;
            PdfLogger.Log(LogCategory.Text, $"[TYPE1] Type1 font parsing exception: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Gets the glyph ID for a character code
    /// </summary>
    /// <param name="charCode">Character code (can be Unicode or font-specific encoding)</param>
    /// <returns>Glyph ID, or 0 if not found</returns>
    public ushort GetGlyphId(ushort charCode)
    {
        // For Type1 fonts, use direct character code as glyph index
        // Type1 fonts use encoding arrays where charCode maps to glyph name
        if (_isType1Font)
        {
            return charCode < NumGlyphs
                ? charCode
                : (ushort)0;
        }

        // For CFF fonts without a cmap table, use direct character code mapping
        // This works for subset fonts where character codes map directly to glyph indices
        if (_isCffFont && _cmapTable is null)
        {
            // Character code is the glyph index for subset fonts
            return charCode < NumGlyphs
                ? charCode
                : (ushort)0;
        }

        return _cmapTable?.GetGlyphId(charCode) ?? 0;
    }

    /// <summary>
    /// Gets the glyph ID for a glyph name (used for CFF and Type1 fonts)
    /// </summary>
    /// <param name="glyphName">Glyph name (e.g., "o", "C", "space")</param>
    /// <returns>Glyph ID, or 0 if not found</returns>
    public ushort GetGlyphIdByName(string glyphName)
    {
        if (string.IsNullOrEmpty(glyphName))
            return 0;

        // For Type1 fonts, check if the glyph exists
        // We use a hash of the glyph name as a pseudo-ID (since Type1 doesn't have numeric IDs)
        if (_isType1Font && _type1Parser is not null)
        {
            // Check if the glyph exists in the font
            if (_type1Parser.HasGlyph(glyphName))
            {
                // Return a non-zero value to indicate the glyph exists
                // We use a hash to create a consistent pseudo-ID for each glyph name
                return (ushort)(Math.Abs(glyphName.GetHashCode()) % 65534 + 1);
            }
            return 0;
        }

        // For CFF fonts, use the charset to map glyph name to glyph index
        if (!_isCffFont || _cffTable is null) return 0;
        // First, convert the glyph name to SID
        int sid = GetSidFromGlyphName(glyphName);
        switch (sid)
        {
            case < 0:
            // Search the charset for the glyph index with this SID
            // Glyph 0 is always .notdef (SID 0)
            case 0:
                return 0;
        }

        // Get the charset from the CFF table
        ICharset? charset = _cffTable.CharSet;
        switch (charset)
        {
            case null:
                return 0;
            // Different charset formats store data differently
            case CharsetsFormat0 format0:
            {
                // Format 0: simple array where Glyphs[glyphIndex-1] = SID
                // (glyph 0 is .notdef and not in the array)
                for (var i = 0; i < format0.Glyphs.Count; i++)
                {
                    if (format0.Glyphs[i] == sid)
                        return (ushort)(i + 1); // +1 because .notdef is not in list
                }

                break;
            }
            case CharsetsFormat1 format1:
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

                break;
            }
            case CharsetsFormat2 format2:
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

                break;
            }
        }

        return 0;

        // For TrueType fonts, fall back to cmap lookup
        // This is not ideal but provides some compatibility
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
        if (_cffTable?.Strings is null) return -1; // Not found
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
        // TrueType fonts use hmtx table
        if (_hmtxTable is not null)
        {
            return _hmtxTable.GetAdvanceWidth(glyphId);
        }

        // Type1 fonts store width in CharStrings
        if (_isType1Font && _type1Parser is not null)
        {
            // Get glyph name for the glyphId (which is the charCode for Type1)
            string? glyphName = _type1Parser.GetGlyphName(glyphId);
            if (glyphName is not null)
            {
                CffGlyphOutline? outline = _type1Parser.GetGlyphOutline(glyphName);
                if (outline?.Width is not null)
                {
                    return (ushort)Math.Round(outline.Width.Value);
                }
            }
            // Return a default width if not found
            return 500;
        }

        // CFF fonts store width in CharStrings
        if (!_isCffFont || _cffTable is null) return 0;
        CffGlyphOutline? glyphOutline = _cffTable.GetGlyphOutline(glyphId);
        if (glyphOutline is null) return 0;
        // If Width is specified in the CharString, use it
        // Otherwise use nominalWidthX (the default width for the font)
        float width = glyphOutline.Width ?? _cffTable.NominalWidthX;
        // Width is already in font units (CFF uses 1000 units per em)
        return (ushort)Math.Round(width);

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
        return glyphId == 0
            ? (ushort)0
            : GetAdvanceWidth(glyphId);
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
        // Handle Type1 fonts
        if (_isType1Font && _type1Parser is not null)
        {
            return GetType1GlyphOutline(glyphId);
        }

        // Handle CFF fonts
        if (_isCffFont && _cffTable is not null)
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
        if (_glyphTable is null || _locaTable is null)
            return null;

        // Get glyph data
        GlyphData? glyphData = _glyphTable.GetGlyphData(glyphId);
        if (glyphData is null)
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

        // Log metrics for problematic glyphs (em dash = 1165)
        if (glyphId == 1165)
        {
            PdfLogger.Log(LogCategory.Text,
                $"[GLYPH-1165] TrueType glyph metrics: {metrics}, " +
                $"Header: XMin={glyphData.Header.XMin}, YMin={glyphData.Header.YMin}, " +
                $"XMax={glyphData.Header.XMax}, YMax={glyphData.Header.YMax}, " +
                $"NumContours={glyphData.Header.NumberOfContours}");
        }

        switch (glyphData.GlyphSpec)
        {
            // Convert SimpleGlyph to GlyphOutline
            case SimpleGlyph simpleGlyph:
            {
                List<GlyphContour> contours = ConvertSimpleGlyphToContours(simpleGlyph);

                // Log contour details for em dash
                if (glyphId == 1165 && contours.Count > 0)
                {
                    GlyphContour c = contours[0];
                    PdfLogger.Log(LogCategory.Text,
                        $"[GLYPH-1165] Contour has {c.Points.Count} points");
                    foreach (ContourPoint pt in c.Points.Take(10))
                    {
                        PdfLogger.Log(LogCategory.Text,
                            $"[GLYPH-1165]   Point: X={pt.X}, Y={pt.Y}, OnCurve={pt.OnCurve}");
                    }
                }

                return new GlyphOutline(glyphId, contours, metrics, isComposite: false);
            }
            // For composite glyphs, recursively resolve components
            case CompositeGlyph compositeGlyph:
                return ExtractCompositeGlyph(glyphId, compositeGlyph, metrics);
            default:
                return null;
        }
    }

    /// <summary>
    /// Get the raw CFF glyph outline for direct rendering with cubic Bezier curves.
    /// Returns null for TrueType fonts or if the glyph is not found.
    /// </summary>
    /// <param name="glyphId">Glyph ID from the font's cmap table</param>
    /// <returns>CFF GlyphOutline with path commands, or null</returns>
    public CffGlyphOutline? GetCffGlyphOutlineDirect(ushort glyphId)
    {
        if (!_isCffFont || _cffTable is null)
            return null;

        return _cffTable.GetGlyphOutline(glyphId);
    }

    /// <summary>
    /// Get raw CFF-style glyph outline for a Type1 font using the glyph name.
    /// Returns the outline with cubic Bezier commands for proper rendering.
    /// </summary>
    /// <param name="glyphName">The glyph name (e.g., "A", "space", "Aacute")</param>
    /// <returns>CffGlyphOutline with cubic Bezier commands, or null if glyph not found</returns>
    public CffGlyphOutline? GetType1GlyphOutlineDirect(string glyphName)
    {
        if (!_isType1Font || _type1Parser is null || string.IsNullOrEmpty(glyphName))
            return null;

        return _type1Parser.GetGlyphOutline(glyphName);
    }

    /// <summary>
    /// Get glyph outline for a CFF font
    /// Converts CFF path commands to contour-based GlyphOutline
    /// </summary>
    private GlyphOutline? GetCffGlyphOutline(ushort glyphId)
    {
        if (_cffTable is null)
            return null;

        // Get CFF glyph outline from Type1Table
        CffGlyphOutline? cffOutline = _cffTable.GetGlyphOutline(glyphId);
        if (cffOutline is null)
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
    /// Get glyph outline for a Type1 font
    /// Converts Type1 CharString path commands to contour-based GlyphOutline
    /// </summary>
    private GlyphOutline? GetType1GlyphOutline(ushort glyphId)
    {
        if (_type1Parser is null)
            return null;

        // Try to get glyph name for the character code via Type1 encoding
        string? glyphName = _type1Parser.GetGlyphName(glyphId);

        // If encoding lookup fails, try index-based lookup (for CID fonts with Type1 data)
        // In CID fonts, the GID from CIDToGIDMap may be an index into the CharStrings
        if (glyphName is null)
        {
            glyphName = _type1Parser.GetGlyphNameByIndex(glyphId);
            if (glyphName is not null)
            {
                PdfLogger.Log(LogCategory.Text,
                    $"[TYPE1-GLYPH] GetType1GlyphOutline: glyphId={glyphId} -> index lookup found '{glyphName}'");
            }
        }
        else
        {
            PdfLogger.Log(LogCategory.Text,
                $"[TYPE1-GLYPH] GetType1GlyphOutline: glyphId={glyphId} -> encoding lookup found '{glyphName}'");
        }

        if (glyphName is null)
        {
            PdfLogger.Log(LogCategory.Text,
                $"[TYPE1-GLYPH] GetType1GlyphOutline: glyphId={glyphId} -> no glyph found");
            return null;
        }

        // Get Type1 glyph outline
        CffGlyphOutline? type1Outline = _type1Parser.GetGlyphOutline(glyphName);

        PdfLogger.Log(LogCategory.Text,
            $"[TYPE1-GLYPH] GetGlyphOutline('{glyphName}'): {(type1Outline is null ? "null" : $"commands={type1Outline.Commands.Count}")}");

        if (type1Outline is null)
            return null;

        // Get glyph metrics
        ushort advanceWidth = GetAdvanceWidth(glyphId);
        short leftSideBearing = 0; // Type1 uses sidebearing from CharString

        // Use bounding box from Type1 outline
        var metrics = new GlyphMetrics(
            advanceWidth,
            leftSideBearing,
            (short)type1Outline.MinX,
            (short)type1Outline.MinY,
            (short)type1Outline.MaxX,
            (short)type1Outline.MaxY
        );

        // Convert path commands to contours (same format as CFF)
        List<GlyphContour> contours = ConvertCffCommandsToContours(type1Outline);

        return new GlyphOutline(glyphId, contours, metrics, isComposite: false);
    }

    /// <summary>
    /// Get glyph outline for a Type1 font using the glyph name directly
    /// This is the preferred method for Type1 fonts as they use name-based lookup
    /// </summary>
    /// <param name="glyphName">The glyph name (e.g., "A", "space", "Aacute")</param>
    /// <returns>GlyphOutline with contour data, or null if glyph not found</returns>
    public GlyphOutline? GetGlyphOutlineByName(string glyphName)
    {
        if (_type1Parser is null || string.IsNullOrEmpty(glyphName))
            return null;

        PdfLogger.Log(LogCategory.Text,
            $"[TYPE1-GLYPH] GetGlyphOutlineByName: glyphName='{glyphName}'");

        // Get Type1 glyph outline directly by name
        CffGlyphOutline? type1Outline = _type1Parser.GetGlyphOutline(glyphName);

        PdfLogger.Log(LogCategory.Text,
            $"[TYPE1-GLYPH] GetGlyphOutline('{glyphName}'): {(type1Outline is null ? "null" : $"commands={type1Outline.Commands.Count}")}");

        if (type1Outline is null)
            return null;

        // Get advance width from the outline (Type1 stores width in CharString)
        ushort advanceWidth = type1Outline.Width is not null
            ? (ushort)Math.Round(type1Outline.Width.Value)
            : (ushort)500; // Default width
        short leftSideBearing = 0; // Type1 uses sidebearing from CharString

        // Use bounding box from Type1 outline
        var metrics = new GlyphMetrics(
            advanceWidth,
            leftSideBearing,
            (short)type1Outline.MinX,
            (short)type1Outline.MinY,
            (short)type1Outline.MaxX,
            (short)type1Outline.MaxY
        );

        // Convert path commands to contours (same format as CFF)
        List<GlyphContour> contours = ConvertCffCommandsToContours(type1Outline);

        // Use a hash of the glyph name as the glyph ID
        var pseudoGlyphId = (ushort)(Math.Abs(glyphName.GetHashCode()) % 65534 + 1);

        return new GlyphOutline(pseudoGlyphId, contours, metrics, isComposite: false);
    }

    /// <summary>
    /// Get advance width for a Type1 glyph by name
    /// </summary>
    /// <param name="glyphName">The glyph name</param>
    /// <returns>Advance width in font units, or 500 if not found</returns>
    public ushort GetAdvanceWidthByName(string glyphName)
    {
        if (_type1Parser is null || string.IsNullOrEmpty(glyphName))
            return 500; // Default width

        CffGlyphOutline? outline = _type1Parser.GetGlyphOutline(glyphName);
        if (outline?.Width is not null)
        {
            return (ushort)Math.Round(outline.Width.Value);
        }
        return 500; // Default width
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
            if (componentOutline is null || componentOutline.IsEmpty)
                continue;

            // Transform each contour of the component using the transformation matrix
            foreach (GlyphContour contour in componentOutline.Contours)
            {
                List<ContourPoint> transformedPoints =
                    (from point in contour.Points
                        let x = point.X * component.A + point.Y * component.C + component.Argument1
                        let y = point.X * component.B + point.Y * component.D + component.Argument2
                        select new ContourPoint(x, y, point.OnCurve))
                        .ToList();

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
            if (locaData is null || _headTable is null || NumGlyphs == 0)
                return;

            _locaTable = new LocaTable(locaData);
            bool isShortFormat = _headTable.IndexToLocFormat == IndexToLocFormat.Offset16;
            _locaTable.Process(NumGlyphs, isShortFormat);

            // Parse glyf table (contains glyph outlines)
            byte[]? glyfData = _parser.GetTable("glyf");
            if (glyfData is null)
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
