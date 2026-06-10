using Logging;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a Type 1 (PostScript) font (ISO 32000-1:2008 section 9.6.2)
/// </summary>
internal partial class Type1Font : PdfFont
{
    private double[]? _widths;
    private double _defaultWidth;
    private EmbeddedFontMetrics? _embeddedMetrics;
    private bool _metricsLoaded;

    public Type1Font(PdfDictionary dictionary, PdfDocument? document = null)
        : base(dictionary, document)
    {
        LoadEncoding();
        LoadToUnicodeCMap();
        LoadWidths();
    }

    internal override PdfFontType FontType => PdfFontType.Type1;

    public override double GetCharacterWidth(int charCode)
    {
        // Try to use embedded font metrics first for more accurate widths
        // This matches TrueTypeFont behavior and fixes garbled text issues
        // when the PDF /Widths array has incorrect values
        EmbeddedFontMetrics? embeddedMetrics = GetEmbeddedMetrics();
        PdfLogger.Log(LogCategory.Text, $"  [WIDTH-DEBUG] Type1 charCode={charCode}: embeddedMetrics={(embeddedMetrics != null ? "exists" : "NULL")}, IsValid={embeddedMetrics?.IsValid}, Encoding={(Encoding != null ? "exists" : "NULL")}");

        if (embeddedMetrics is { IsValid: true })
        {
            // For Type1 fonts with custom encodings, we must map through the encoding first
            // Character codes (e.g., 18-31) -> Glyph names (e.g., "T", "e", "c") -> Widths
            string? glyphName = Encoding?.GetGlyphName(charCode);
            PdfLogger.Log(LogCategory.Text, $"  [WIDTH-DEBUG] Type1 charCode={charCode}: glyphName='{glyphName}'");

            if (!string.IsNullOrEmpty(glyphName))
            {
                // For Type1 fonts, use name-based width lookup directly
                // This avoids the glyphId mismatch issue where GetGlyphIdByName returns a hash
                // but GetAdvanceWidth expects a character code
                if (embeddedMetrics.IsType1Font)
                {
                    ushort glyphWidth = embeddedMetrics.GetAdvanceWidthByName(glyphName);
                    PdfLogger.Log(LogCategory.Text, $"  [WIDTH-DEBUG] Type1 charCode={charCode}: glyphWidth={glyphWidth} (by name)");

                    if (glyphWidth > 0)
                    {
                        double scaledWidth = glyphWidth * 1000.0 / embeddedMetrics.UnitsPerEm;
                        PdfLogger.Log(LogCategory.Text, $"  [WIDTH] Type1 charCode={charCode} glyphName='{glyphName}' -> embedded (by name): rawWidth={glyphWidth}, unitsPerEm={embeddedMetrics.UnitsPerEm}, scaled={scaledWidth:F1}");
                        return scaledWidth;
                    }
                }
                else
                {
                    // CFF and other fonts: use glyphId-based lookup
                    ushort glyphId = embeddedMetrics.GetGlyphIdByName(glyphName);
                    PdfLogger.Log(LogCategory.Text, $"  [WIDTH-DEBUG] Type1 charCode={charCode}: glyphId={glyphId}");

                    if (glyphId > 0)
                    {
                        ushort glyphWidth = embeddedMetrics.GetAdvanceWidth(glyphId);
                        PdfLogger.Log(LogCategory.Text, $"  [WIDTH-DEBUG] Type1 charCode={charCode}: glyphWidth={glyphWidth}");

                        if (glyphWidth > 0)
                        {
                            // For CFF fonts: if glyph width equals nominalWidthX, the glyph doesn't specify
                            // its own width and is using the default. In this case, prefer PDF /Widths array.
                            if (embeddedMetrics.IsCffFont)
                            {
                                int nominalWidthX = embeddedMetrics.GetNominalWidthX();
                                if (glyphWidth == nominalWidthX)
                                {
                                    PdfLogger.Log(LogCategory.Text, $"  [WIDTH-DEBUG] Type1 charCode={charCode}: CFF glyph width equals nominalWidthX ({nominalWidthX}), trying PDF widths array");
                                    // Fall through to PDF widths array check below
                                }
                                else
                                {
                                    // Glyph has explicit width, use it
                                    double scaledWidth = glyphWidth * 1000.0 / embeddedMetrics.UnitsPerEm;
                                    PdfLogger.Log(LogCategory.Text, $"  [WIDTH] Type1 charCode={charCode} glyphName='{glyphName}' glyphId={glyphId} -> embedded: rawWidth={glyphWidth}, unitsPerEm={embeddedMetrics.UnitsPerEm}, scaled={scaledWidth:F1}");
                                    return scaledWidth;
                                }
                            }
                            else
                            {
                                // Non-CFF fonts: always use embedded width
                                double scaledWidth = glyphWidth * 1000.0 / embeddedMetrics.UnitsPerEm;
                                PdfLogger.Log(LogCategory.Text, $"  [WIDTH] Type1 charCode={charCode} glyphName='{glyphName}' glyphId={glyphId} -> embedded: rawWidth={glyphWidth}, unitsPerEm={embeddedMetrics.UnitsPerEm}, scaled={scaledWidth:F1}");
                                return scaledWidth;
                            }
                        }
                    }
                }
            }
        }

        // Fallback to widths array from PDF
        PdfLogger.Log(LogCategory.Text, $"  [WIDTH-PATH] Checking PDF widths array: _widths={(_widths != null ? "exists" : "NULL")}, FirstChar={FirstChar}, LastChar={LastChar}, charCode={charCode}");
        if (_widths is not null && charCode >= FirstChar && charCode <= LastChar)
        {
            int index = charCode - FirstChar;
            if (index >= 0 && index < _widths.Length)
            {
                PdfLogger.Log(LogCategory.Text, $"  [WIDTH] Type1 charCode={charCode} -> PDF widths array[{index}]: {_widths[index]:F1}");
                return _widths[index];
            }

            PdfLogger.Log(LogCategory.Text, $"  [WIDTH-PATH] PDF widths array index out of bounds: index={index}, _widths.Length={_widths.Length}");
        }

        // For standard 14 fonts, use built-in metrics
        // Map through encoding first (similar to embedded metrics path above)
        // This fixes custom encodings where charCode 2 might be 'E', not standard ASCII 69
        string? glyphNameForStd14 = Encoding?.GetGlyphName(charCode);
        PdfLogger.Log(LogCategory.Text, $"  [WIDTH-PATH] Standard 14 fonts: glyphName='{glyphNameForStd14}'");

        double? standardWidth = null;
        if (!string.IsNullOrEmpty(glyphNameForStd14))
        {
            standardWidth = GetStandardFontWidthByName(glyphNameForStd14);
        }

        // If no encoding or glyph name not found, try direct charCode lookup as fallback
        if (!standardWidth.HasValue)
        {
            PdfLogger.Log(LogCategory.Text, "  [WIDTH-PATH] Standard 14 fonts: trying fallback charCode lookup");
            standardWidth = GetStandardFontWidth(charCode);
        }

        PdfLogger.Log(LogCategory.Text, $"  [WIDTH-PATH] Standard 14 fonts width: {(standardWidth.HasValue ? standardWidth.Value.ToString("F1") : "NULL")}");
        if (standardWidth.HasValue)
        {
            PdfLogger.Log(LogCategory.Text, $"  [WIDTH] Type1 charCode={charCode} glyphName='{glyphNameForStd14}' -> Standard 14 font: {standardWidth.Value:F1}");
            return standardWidth.Value;
        }

        // Try to get from the font descriptor
        PdfFontDescriptor? descriptor = GetDescriptor();
        PdfLogger.Log(LogCategory.Text, $"  [WIDTH-PATH] Font descriptor: {(descriptor != null ? $"MissingWidth={descriptor.MissingWidth}, AvgWidth={descriptor.AvgWidth}" : "NULL")}");
        if (descriptor is null)
        {
            double fallback = _defaultWidth > 0 ? _defaultWidth : 250;
            PdfLogger.Log(LogCategory.Text, $"  [WIDTH] Type1 charCode={charCode} -> Default (no descriptor): {fallback:F1}");
            return fallback;
        }
        if (descriptor.MissingWidth > 0)
        {
            PdfLogger.Log(LogCategory.Text, $"  [WIDTH] Type1 charCode={charCode} -> Descriptor MissingWidth: {descriptor.MissingWidth:F1}");
            return descriptor.MissingWidth;
        }
        if (descriptor.AvgWidth > 0)
        {
            PdfLogger.Log(LogCategory.Text, $"  [WIDTH] Type1 charCode={charCode} -> Descriptor AvgWidth: {descriptor.AvgWidth:F1}");
            return descriptor.AvgWidth;
        }

        // Last resort: use default width
        double finalDefault = _defaultWidth > 0 ? _defaultWidth : 250;
        PdfLogger.Log(LogCategory.Text, $"  [WIDTH] Type1 charCode={charCode} -> Final default: {finalDefault:F1}");
        return finalDefault;
    }

    /// <summary>
    /// Gets the width for a character in a standard 14 font by glyph name.
    /// This is the preferred method for custom encodings where charCode != standard ASCII.
    /// </summary>
    private double? GetStandardFontWidthByName(string glyphName)
    {
        if (string.IsNullOrEmpty(BaseFont) || string.IsNullOrEmpty(glyphName))
            return null;

        return BaseFont switch
        {
            // Each font variant has different widths - must use correct AFM data
            "Helvetica" or "Helvetica-Oblique" => GetHelveticaWidthByName(glyphName),
            "Helvetica-Bold" or "Helvetica-BoldOblique" => GetHelveticaBoldWidthByName(glyphName),
            "Times-Roman" => GetTimesRomanWidthByName(glyphName),
            "Times-Bold" => GetTimesBoldWidthByName(glyphName),
            "Times-Italic" => GetTimesItalicWidthByName(glyphName),
            "Times-BoldItalic" => GetTimesBoldItalicWidthByName(glyphName),
            "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => 600,
            _ => null
        };
    }

    /// <summary>
    /// Gets the width for a character in a standard 14 font by charCode (fallback).
    /// Only used when no encoding is available or glyph name lookup fails.
    /// </summary>
    private double? GetStandardFontWidth(int charCode)
    {
        if (string.IsNullOrEmpty(BaseFont))
            return null;

        return BaseFont switch
        {
            // Standard 14 fonts have built-in metrics from Adobe AFM files
            // Helvetica and Helvetica-Oblique share the same widths (oblique is just slanted)
            "Helvetica" or "Helvetica-Oblique" => GetHelveticaWidth(charCode),
            // Helvetica-Bold and Helvetica-BoldOblique share the same widths
            "Helvetica-Bold" or "Helvetica-BoldOblique" => GetHelveticaBoldWidth(charCode),
            "Times-Roman" or "Times-Bold" or "Times-Italic" or "Times-BoldItalic" => GetTimesRomanWidth(charCode),
            "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => 600,
            _ => null
        };
    }

    internal override EmbeddedFontMetrics? GetEmbeddedMetrics()
    {
        if (_metricsLoaded)
            return _embeddedMetrics;

        _metricsLoaded = true;
        PdfLogger.Log(LogCategory.Text, $"[TYPE1FONT] GetEmbeddedMetrics called for font '{BaseFont}'");

        try
        {
            // Get font descriptor
            PdfFontDescriptor? descriptor = GetDescriptor();
            if (descriptor is null)
            {
                PdfLogger.Log(LogCategory.Text, $"[TYPE1FONT] No descriptor for font '{BaseFont}'");
                return null;
            }

            // Try to get embedded CFF data (FontFile3) - preferred for Type1C
            byte[]? fontData = descriptor.GetFontFile3();

            if (fontData is not null)
            {
                PdfLogger.Log(LogCategory.Text, $"[TYPE1FONT] Found FontFile3 (CFF) for font '{BaseFont}', {fontData.Length} bytes");
                // CFF/Type1C data - use the TrueType/CFF constructor
                _embeddedMetrics = new EmbeddedFontMetrics(fontData);
                return _embeddedMetrics;
            }

            // Try classic Type1 (FontFile) with length parameters
            // This uses PFA/PFB format which requires Length1/Length2/Length3 for proper parsing
            (byte[] data, int length1, int length2, int length3)? type1Data = descriptor.GetFontFileWithLengths();
            if (type1Data is not null)
            {
                (byte[] data, int length1, int length2, int length3) = type1Data.Value;
                PdfLogger.Log(LogCategory.Text, $"[TYPE1FONT] Found FontFile (Type1) for font '{BaseFont}', {data.Length} bytes, L1={length1}, L2={length2}, L3={length3}");
                // Use the Type1-specific constructor with length parameters
                _embeddedMetrics = new EmbeddedFontMetrics(data, length1, length2, length3);
                PdfLogger.Log(LogCategory.Text, $"[TYPE1FONT] EmbeddedFontMetrics created: IsValid={_embeddedMetrics.IsValid}, IsType1={_embeddedMetrics.IsType1Font}");
                return _embeddedMetrics;
            }

            PdfLogger.Log(LogCategory.Text, $"[TYPE1FONT] No embedded font data found for '{BaseFont}'");
            return null;
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Text, $"[TYPE1FONT] Exception for font '{BaseFont}': {ex.Message}");
            // If parsing fails, return null and fall back to PDF widths
            return null;
        }
    }

    private void LoadEncoding()
    {
        if (!_dictionary.TryGetValue(new PdfName("Encoding"), out PdfObject? obj))
        {
            // Use standard encoding based on the font name
            Encoding = GetStandardEncoding(BaseFont);
            PdfLogger.Log(LogCategory.Text, $"[TYPE1-ENCODING] No Encoding in dict, using standard for '{BaseFont}', Encoding={Encoding is not null}");
            return;
        }

        // Resolve indirect reference
        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        Encoding = obj switch
        {
            // Named encoding
            PdfName encodingName => PdfFontEncoding.GetStandardEncoding(encodingName.Value),
            // Custom encoding dictionary
            PdfDictionary encodingDict => PdfFontEncoding.FromDictionary(encodingDict, GetStandardEncoding(BaseFont)),
            _ => Encoding
        };
    }

    private void LoadWidths()
    {
        // Get widths array
        if (_dictionary.TryGetValue(new PdfName("Widths"), out PdfObject? obj))
        {
            if (obj is PdfIndirectReference reference && _document is not null)
                obj = _document.ResolveReference(reference);

            if (obj is PdfArray widthsArray)
            {
                _widths = new double[widthsArray.Count];
                for (var i = 0; i < widthsArray.Count; i++)
                {
                    _widths[i] = widthsArray[i].ToDouble();
                }
            }
        }

        // Get default width
        PdfFontDescriptor? descriptor = GetDescriptor();
        if (descriptor is not null)
        {
            _defaultWidth = descriptor.MissingWidth > 0 ? descriptor.MissingWidth : descriptor.AvgWidth;
        }

        // If still no width info, use standard metrics
        if (_widths is null && _defaultWidth == 0)
        {
            _defaultWidth = GetStandardWidth(BaseFont);
        }
    }

    private static PdfFontEncoding GetStandardEncoding(string baseFontName)
    {
        if (baseFontName.Contains("Symbol") || baseFontName.Contains("ZapfDingbats"))
            return PdfFontEncoding.GetStandardEncoding("SymbolEncoding");
        return PdfFontEncoding.GetStandardEncoding("StandardEncoding");
    }

    private static double GetStandardWidth(string baseFontName)
    {
        // Approximate widths for standard 14 fonts
        return baseFontName switch
        {
            _ when baseFontName.Contains("Courier") => 600, // Courier is monospace
            _ when baseFontName.Contains("Helvetica") => 556,
            _ when baseFontName.Contains("Times") => 500,
            _ when baseFontName.Contains("Symbol") => 600,
            _ when baseFontName.Contains("ZapfDingbats") => 600,
            _ => 500
        };
    }

    private static bool IsStandard14Font(string baseFontName)
    {
        string[] standard14 =
        [
            "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
            "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
            "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
            "Symbol", "ZapfDingbats"
        ];

        return Array.Exists(standard14, font => baseFontName.Contains(font));
    }

}
