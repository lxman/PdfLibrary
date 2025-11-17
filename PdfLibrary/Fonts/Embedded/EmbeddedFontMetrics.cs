using PdfLibrary.Fonts.Embedded.Tables;

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
    private readonly MaxpTable? _maxpTable;
    private readonly HmtxTable? _hmtxTable;
    private readonly NameTable? _nameTable;
    private readonly CmapTable? _cmapTable;

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
            _maxpTable = new MaxpTable(maxpData);
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
}
