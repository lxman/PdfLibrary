using Logging;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a Type 1 (PostScript) font (ISO 32000-1:2008 section 9.6.2)
/// </summary>
public class Type1Font : PdfFont
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

    public override PdfFontType FontType => PdfFontType.Type1;

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
            // Character codes (e.g., 18-31) -> Glyph names (e.g., "T", "e", "c") -> Glyph IDs -> Widths
            string? glyphName = Encoding?.GetGlyphName(charCode);
            PdfLogger.Log(LogCategory.Text, $"  [WIDTH-DEBUG] Type1 charCode={charCode}: glyphName='{glyphName}'");

            if (!string.IsNullOrEmpty(glyphName))
            {
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

        // Fallback to widths array from PDF
        if (_widths is not null && charCode >= FirstChar && charCode <= LastChar)
        {
            int index = charCode - FirstChar;
            if (index >= 0 && index < _widths.Length)
            {
                PdfLogger.Log(LogCategory.Text, $"  [WIDTH] Type1 charCode={charCode} -> PDF widths array: {_widths[index]:F1}");
                return _widths[index];
            }
        }

        // For standard 14 fonts, use built-in metrics
        double? standardWidth = GetStandardFontWidth(charCode);
        if (standardWidth.HasValue)
            return standardWidth.Value;

        // Try to get from the font descriptor
        PdfFontDescriptor? descriptor = GetDescriptor();
        if (descriptor is null) return _defaultWidth > 0
            ? _defaultWidth
            : 250; // 250 is PDF default
        if (descriptor.MissingWidth > 0)
            return descriptor.MissingWidth;
        if (descriptor.AvgWidth > 0)
            return descriptor.AvgWidth;

        // Last resort: use default width
        return _defaultWidth > 0
            ? _defaultWidth
            : 250; // 250 is PDF default
    }

    /// <summary>
    /// Gets the width for a character in a standard 14 font
    /// </summary>
    private double? GetStandardFontWidth(int charCode)
    {
        if (string.IsNullOrEmpty(BaseFont))
            return null;

        return BaseFont switch
        {
            // Standard 14 fonts have built-in metrics
            // Helvetica widths for WinAnsi encoding (most common)
            "Helvetica" or "Helvetica-Bold" or "Helvetica-Oblique" or "Helvetica-BoldOblique" => GetHelveticaWidth(
                charCode),
            "Times-Roman" or "Times-Bold" or "Times-Italic" or "Times-BoldItalic" => GetTimesRomanWidth(charCode),
            "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => 600,
            _ => null
        };
    }

    /// <summary>
    /// Helvetica character widths (WinAnsi encoding)
    /// Source: Adobe Font Metrics (AFM) files
    /// </summary>
    private static double GetHelveticaWidth(int charCode)
    {
        // Helvetica widths for character codes 32-255 (WinAnsi)
        return charCode switch
        {
            32 => 278,   // space
            33 => 278,   // exclam
            34 => 355,   // quotedbl
            35 => 556,   // numbersign
            36 => 556,   // dollar
            37 => 889,   // percent
            38 => 667,   // ampersand
            39 => 191,   // quotesingle
            40 => 333,   // parenleft
            41 => 333,   // parenright
            42 => 389,   // asterisk
            43 => 584,   // plus
            44 => 278,   // comma
            45 => 333,   // hyphen
            46 => 278,   // period
            47 => 278,   // slash
            48 => 556,   // zero
            49 => 556,   // one
            50 => 556,   // two
            51 => 556,   // three
            52 => 556,   // four
            53 => 556,   // five
            54 => 556,   // six
            55 => 556,   // seven
            56 => 556,   // eight
            57 => 556,   // nine
            58 => 278,   // colon
            59 => 278,   // semicolon
            60 => 584,   // less
            61 => 584,   // equal
            62 => 584,   // greater
            63 => 556,   // question
            64 => 1015,  // at
            65 => 667,   // A
            66 => 667,   // B
            67 => 722,   // C
            68 => 722,   // D
            69 => 667,   // E
            70 => 611,   // F
            71 => 778,   // G
            72 => 722,   // H
            73 => 278,   // I
            74 => 500,   // J
            75 => 667,   // K
            76 => 556,   // L
            77 => 833,   // M
            78 => 722,   // N
            79 => 778,   // O
            80 => 667,   // P
            81 => 778,   // Q
            82 => 722,   // R
            83 => 667,   // S
            84 => 611,   // T
            85 => 722,   // U
            86 => 667,   // V
            87 => 944,   // W
            88 => 667,   // X
            89 => 667,   // Y
            90 => 611,   // Z
            91 => 278,   // bracketleft
            92 => 278,   // backslash
            93 => 278,   // bracketright
            94 => 469,   // asciicircum
            95 => 556,   // underscore
            96 => 333,   // grave
            97 => 556,   // a
            98 => 556,   // b
            99 => 500,   // c
            100 => 556,  // d
            101 => 556,  // e
            102 => 278,  // f
            103 => 556,  // g
            104 => 556,  // h
            105 => 222,  // i
            106 => 222,  // j
            107 => 500,  // k
            108 => 222,  // l
            109 => 833,  // m
            110 => 556,  // n
            111 => 556,  // o
            112 => 556,  // p
            113 => 556,  // q
            114 => 333,  // r
            115 => 500,  // s
            116 => 278,  // t
            117 => 556,  // u
            118 => 500,  // v
            119 => 722,  // w
            120 => 500,  // x
            121 => 500,  // y
            122 => 500,  // z
            123 => 334,  // braceleft
            124 => 260,  // bar
            125 => 334,  // braceright
            126 => 584,  // asciitilde
            _ => 556     // default for extended characters
        };
    }

    /// <summary>
    /// Times Roman character widths (WinAnsi encoding)
    /// </summary>
    private static double GetTimesRomanWidth(int charCode)
    {
        return charCode switch
        {
            32 => 250,   // space
            33 => 333,   // exclam
            34 => 408,   // quotedbl
            35 => 500,   // numbersign
            36 => 500,   // dollar
            37 => 833,   // percent
            38 => 778,   // ampersand
            39 => 180,   // quotesingle
            40 => 333,   // parenleft
            41 => 333,   // parenright
            42 => 500,   // asterisk
            43 => 564,   // plus
            44 => 250,   // comma
            45 => 333,   // hyphen
            46 => 250,   // period
            47 => 278,   // slash
            48 => 500,   // zero
            49 => 500,   // one
            50 => 500,   // two
            51 => 500,   // three
            52 => 500,   // four
            53 => 500,   // five
            54 => 500,   // six
            55 => 500,   // seven
            56 => 500,   // eight
            57 => 500,   // nine
            58 => 278,   // colon
            59 => 278,   // semicolon
            60 => 564,   // less
            61 => 564,   // equal
            62 => 564,   // greater
            63 => 444,   // question
            64 => 921,   // at
            65 => 722,   // A
            66 => 667,   // B
            67 => 667,   // C
            68 => 722,   // D
            69 => 611,   // E
            70 => 556,   // F
            71 => 722,   // G
            72 => 722,   // H
            73 => 333,   // I
            74 => 389,   // J
            75 => 722,   // K
            76 => 611,   // L
            77 => 889,   // M
            78 => 722,   // N
            79 => 722,   // O
            80 => 556,   // P
            81 => 722,   // Q
            82 => 667,   // R
            83 => 556,   // S
            84 => 611,   // T
            85 => 722,   // U
            86 => 722,   // V
            87 => 944,   // W
            88 => 722,   // X
            89 => 722,   // Y
            90 => 611,   // Z
            91 => 333,   // bracketleft
            92 => 278,   // backslash
            93 => 333,   // bracketright
            94 => 469,   // asciicircum
            95 => 500,   // underscore
            96 => 333,   // grave
            97 => 444,   // a
            98 => 500,   // b
            99 => 444,   // c
            100 => 500,  // d
            101 => 444,  // e
            102 => 333,  // f
            103 => 500,  // g
            104 => 500,  // h
            105 => 278,  // i
            106 => 278,  // j
            107 => 500,  // k
            108 => 278,  // l
            109 => 778,  // m
            110 => 500,  // n
            111 => 500,  // o
            112 => 500,  // p
            113 => 500,  // q
            114 => 333,  // r
            115 => 389,  // s
            116 => 278,  // t
            117 => 500,  // u
            118 => 500,  // v
            119 => 722,  // w
            120 => 500,  // x
            121 => 500,  // y
            122 => 444,  // z
            123 => 480,  // braceleft
            124 => 200,  // bar
            125 => 480,  // braceright
            126 => 541,  // asciitilde
            _ => 500     // default
        };
    }

    public override EmbeddedFontMetrics? GetEmbeddedMetrics()
    {
        if (_metricsLoaded)
            return _embeddedMetrics;

        _metricsLoaded = true;

        try
        {
            // Get font descriptor
            PdfFontDescriptor? descriptor = GetDescriptor();
            if (descriptor is null)
                return null;

            // Try to get embedded CFF data (FontFile3) - preferred for Type1C
            byte[]? fontData = descriptor.GetFontFile3();
            var fontFileType = "FontFile3";

            // If no FontFile3, try classic Type1 (FontFile)
            // Note: FontFile uses PFB format which may need different parsing
            if (fontData is null)
            {
                fontData = descriptor.GetFontFile();
                fontFileType = "FontFile";
            }

            if (fontData is null)
                return null;

            // Parse embedded font metrics
            _embeddedMetrics = new EmbeddedFontMetrics(fontData);
            return _embeddedMetrics;
        }
        catch
        {
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
                    _widths[i] = GetNumber(widthsArray[i]);
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
        // Standard 14 fonts use specific encodings
        if (!IsStandard14Font(baseFontName)) return PdfFontEncoding.GetStandardEncoding("StandardEncoding");
        if (baseFontName.Contains("Symbol") || baseFontName.Contains("ZapfDingbats"))
            return PdfFontEncoding.GetStandardEncoding("SymbolEncoding");
        return PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
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

    private static double GetNumber(PdfObject obj)
    {
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => 0
        };
    }
}
