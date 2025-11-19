using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using System.Text;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a PDF font encoding (ISO 32000-1:2008 section 9.6.6)
/// Maps character codes to character names or Unicode
/// </summary>
public class PdfFontEncoding
{
    private readonly Dictionary<int, string> _codeToName = new();
    private readonly Dictionary<int, string> _codeToUnicode = new();
    private readonly string _baseEncodingName;

    public PdfFontEncoding(string baseEncodingName = "StandardEncoding")
    {
        _baseEncodingName = baseEncodingName;
        InitializeBaseEncoding(baseEncodingName);
    }

    /// <summary>
    /// Decodes a character code to Unicode string
    /// </summary>
    public string DecodeCharacter(int charCode)
    {
        // Direct Unicode mapping
        if (_codeToUnicode.TryGetValue(charCode, out string? unicode))
            return unicode;

        // Try character name lookup
        if (_codeToName.TryGetValue(charCode, out string? charName))
        {
            string? unicodeFromName = GlyphList.GetUnicode(charName);
            if (unicodeFromName != null)
                return unicodeFromName;
        }

        // Fall back to character code as-is (Latin-1)
        if (charCode is >= 0 and <= 255)
            return Encoding.Latin1.GetString([( byte)charCode]);

        return "?";
    }

    /// <summary>
    /// Sets a character code mapping to a character name
    /// </summary>
    public void SetCharacterName(int charCode, string charName)
    {
        _codeToName[charCode] = charName;

        // Also set Unicode if we can resolve it
        string? unicode = GlyphList.GetUnicode(charName);
        if (unicode != null)
            _codeToUnicode[charCode] = unicode;
    }

    /// <summary>
    /// Sets a character code mapping to Unicode
    /// </summary>
    public void SetUnicode(int charCode, string unicode)
    {
        _codeToUnicode[charCode] = unicode;
    }

    /// <summary>
    /// Gets the glyph name for a character code
    /// </summary>
    public string? GetGlyphName(int charCode)
    {
        if (_codeToName.TryGetValue(charCode, out string? name))
            return name;

        // For standard ASCII, use the character itself as the glyph name
        if (charCode >= 32 && charCode <= 126)
        {
            char c = (char)charCode;
            // Standard glyph names for common characters
            return c switch
            {
                >= 'A' and <= 'Z' => c.ToString(),
                >= 'a' and <= 'z' => c.ToString(),
                >= '0' and <= '9' => c switch
                {
                    '0' => "zero",
                    '1' => "one",
                    '2' => "two",
                    '3' => "three",
                    '4' => "four",
                    '5' => "five",
                    '6' => "six",
                    '7' => "seven",
                    '8' => "eight",
                    '9' => "nine",
                    _ => null
                },
                ' ' => "space",
                '!' => "exclam",
                '"' => "quotedbl",
                '#' => "numbersign",
                '$' => "dollar",
                '%' => "percent",
                '&' => "ampersand",
                '\'' => "quotesingle",
                '(' => "parenleft",
                ')' => "parenright",
                '*' => "asterisk",
                '+' => "plus",
                ',' => "comma",
                '-' => "hyphen",
                '.' => "period",
                '/' => "slash",
                ':' => "colon",
                ';' => "semicolon",
                '<' => "less",
                '=' => "equal",
                '>' => "greater",
                '?' => "question",
                '@' => "at",
                '[' => "bracketleft",
                '\\' => "backslash",
                ']' => "bracketright",
                '^' => "asciicircum",
                '_' => "underscore",
                '`' => "grave",
                '{' => "braceleft",
                '|' => "bar",
                '}' => "braceright",
                '~' => "asciitilde",
                _ => null
            };
        }

        return null;
    }

    /// <summary>
    /// Gets a standard PDF encoding
    /// </summary>
    public static PdfFontEncoding GetStandardEncoding(string name)
    {
        return name switch
        {
            "StandardEncoding" => CreateStandardEncoding(),
            "WinAnsiEncoding" => CreateWinAnsiEncoding(),
            "MacRomanEncoding" => CreateMacRomanEncoding(),
            "MacExpertEncoding" => CreateMacExpertEncoding(),
            "SymbolEncoding" => CreateSymbolEncoding(),
            "ZapfDingbatsEncoding" => CreateZapfDingbatsEncoding(),
            _ => CreateStandardEncoding()
        };
    }

    /// <summary>
    /// Creates encoding from a dictionary with optional base encoding
    /// </summary>
    public static PdfFontEncoding FromDictionary(PdfDictionary dict, PdfFontEncoding? baseEncoding = null)
    {
        // Get base encoding
        var baseName = "StandardEncoding";
        if (dict.TryGetValue(new PdfName("BaseEncoding"), out PdfObject baseObj) && baseObj is PdfName basePdfName)
        {
            baseName = basePdfName.Value;
        }

        PdfFontEncoding encoding = baseEncoding ?? GetStandardEncoding(baseName);

        // Apply differences
        if (dict.TryGetValue(new PdfName("Differences"), out PdfObject diffObj) && diffObj is PdfArray differences)
        {
            ApplyDifferences(encoding, differences);
        }

        return encoding;
    }

    private static void ApplyDifferences(PdfFontEncoding encoding, PdfArray differences)
    {
        var currentCode = 0;

        foreach (PdfObject item in differences)
        {
            if (item is PdfInteger code)
            {
                currentCode = code.Value;
            }
            else if (item is PdfName name)
            {
                encoding.SetCharacterName(currentCode, name.Value);
                currentCode++;
            }
        }
    }

    private void InitializeBaseEncoding(string encodingName)
    {
        // The actual encoding tables are initialized by the specific factory methods
        // This is just a placeholder for custom encodings
    }

    // Standard Encoding (PDF Reference Appendix D.1)
    private static PdfFontEncoding CreateStandardEncoding()
    {
        var encoding = new PdfFontEncoding("StandardEncoding");

        // ASCII printable characters (32-126) map to themselves
        for (var i = 32; i <= 126; i++)
        {
            encoding.SetUnicode(i, char.ConvertFromUtf32(i));
        }

        // Common additional mappings
        encoding.SetCharacterName(161, "exclamdown");
        encoding.SetCharacterName(162, "cent");
        encoding.SetCharacterName(163, "sterling");
        encoding.SetCharacterName(164, "fraction");
        encoding.SetCharacterName(165, "yen");
        encoding.SetCharacterName(166, "florin");
        encoding.SetCharacterName(167, "section");
        encoding.SetCharacterName(168, "currency");
        encoding.SetCharacterName(169, "quotesingle");
        encoding.SetCharacterName(170, "quotedblleft");
        encoding.SetCharacterName(171, "guillemotleft");
        encoding.SetCharacterName(172, "guilsinglleft");
        encoding.SetCharacterName(173, "guilsinglright");
        encoding.SetCharacterName(174, "fi");
        encoding.SetCharacterName(175, "fl");
        encoding.SetCharacterName(177, "endash");
        encoding.SetCharacterName(178, "dagger");
        encoding.SetCharacterName(179, "daggerdbl");
        encoding.SetCharacterName(180, "periodcentered");
        encoding.SetCharacterName(182, "paragraph");
        encoding.SetCharacterName(183, "bullet");
        encoding.SetCharacterName(184, "quotesinglbase");
        encoding.SetCharacterName(185, "quotedblbase");
        encoding.SetCharacterName(186, "quotedblright");
        encoding.SetCharacterName(187, "guillemotright");
        encoding.SetCharacterName(188, "ellipsis");
        encoding.SetCharacterName(189, "perthousand");
        encoding.SetCharacterName(191, "questiondown");

        return encoding;
    }

    // WinAnsiEncoding (Windows Code Page 1252 - PDF Reference Appendix D.2)
    private static PdfFontEncoding CreateWinAnsiEncoding()
    {
        var encoding = new PdfFontEncoding("WinAnsiEncoding");

        // ASCII printable characters (32-126) map directly
        for (var i = 32; i <= 126; i++)
        {
            encoding.SetUnicode(i, char.ConvertFromUtf32(i));
        }

        // Windows-1252 specific mappings (128-159)
        encoding.SetUnicode(128, "\u20AC"); // Euro sign
        encoding.SetUnicode(130, "\u201A"); // Single low-9 quotation mark
        encoding.SetUnicode(131, "\u0192"); // Latin small letter f with hook
        encoding.SetUnicode(132, "\u201E"); // Double low-9 quotation mark
        encoding.SetUnicode(133, "\u2026"); // Horizontal ellipsis
        encoding.SetUnicode(134, "\u2020"); // Dagger
        encoding.SetUnicode(135, "\u2021"); // Double dagger
        encoding.SetUnicode(136, "\u02C6"); // Modifier letter circumflex accent
        encoding.SetUnicode(137, "\u2030"); // Per mille sign
        encoding.SetUnicode(138, "\u0160"); // Latin capital letter S with caron
        encoding.SetUnicode(139, "\u2039"); // Single left-pointing angle quotation mark
        encoding.SetUnicode(140, "\u0152"); // Latin capital ligature OE
        encoding.SetUnicode(142, "\u017D"); // Latin capital letter Z with caron
        encoding.SetUnicode(145, "\u2018"); // Left single quotation mark
        encoding.SetUnicode(146, "\u2019"); // Right single quotation mark
        encoding.SetUnicode(147, "\u201C"); // Left double quotation mark
        encoding.SetUnicode(148, "\u201D"); // Right double quotation mark
        encoding.SetUnicode(149, "\u2022"); // Bullet
        encoding.SetUnicode(150, "\u2013"); // En dash
        encoding.SetUnicode(151, "\u2014"); // Em dash
        encoding.SetUnicode(152, "\u02DC"); // Small tilde
        encoding.SetUnicode(153, "\u2122"); // Trade mark sign
        encoding.SetUnicode(154, "\u0161"); // Latin small letter s with caron
        encoding.SetUnicode(155, "\u203A"); // Single right-pointing angle quotation mark
        encoding.SetUnicode(156, "\u0153"); // Latin small ligature oe
        encoding.SetUnicode(158, "\u017E"); // Latin small letter z with caron
        encoding.SetUnicode(159, "\u0178"); // Latin capital letter Y with diaeresis

        // Latin-1 Supplement (160-255) - map directly to Unicode
        for (var i = 160; i <= 255; i++)
        {
            encoding.SetUnicode(i, char.ConvertFromUtf32(i));
        }

        return encoding;
    }

    // MacRomanEncoding (Macintosh standard Roman character set)
    private static PdfFontEncoding CreateMacRomanEncoding()
    {
        var encoding = new PdfFontEncoding("MacRomanEncoding");

        // ASCII portion (32-126)
        for (var i = 32; i <= 126; i++)
        {
            encoding.SetUnicode(i, char.ConvertFromUtf32(i));
        }

        // MacRoman-specific mappings (128-255)
        // Simplified version - full implementation would have all MacRoman mappings
        for (var i = 128; i <= 255; i++)
        {
            try
            {
                encoding.SetUnicode(i, Encoding.GetEncoding("macintosh").GetString([(byte)i]));
            }
            catch
            {
                // Fall back to Latin-1 if MacRoman encoding not available
                encoding.SetUnicode(i, Encoding.Latin1.GetString([(byte)i]));
            }
        }

        return encoding;
    }

    // MacExpertEncoding (for expert fonts)
    private static PdfFontEncoding CreateMacExpertEncoding()
    {
        // MacExpertEncoding is similar to MacRomanEncoding but for expert fonts
        return CreateMacRomanEncoding();
    }

    // SymbolEncoding (Symbol font encoding)
    private static PdfFontEncoding CreateSymbolEncoding()
    {
        var encoding = new PdfFontEncoding("SymbolEncoding");

        // Symbol font has its own character set (Greek letters, math symbols, etc.)
        // Common mappings
        encoding.SetCharacterName(32, "space");
        encoding.SetCharacterName(33, "exclam");
        encoding.SetCharacterName(34, "universal");
        encoding.SetCharacterName(35, "numbersign");
        encoding.SetCharacterName(36, "existential");
        encoding.SetCharacterName(37, "percent");
        encoding.SetCharacterName(38, "ampersand");
        encoding.SetCharacterName(39, "suchthat");
        encoding.SetCharacterName(40, "parenleft");
        encoding.SetCharacterName(41, "parenright");
        encoding.SetCharacterName(42, "asteriskmath");
        encoding.SetCharacterName(43, "plus");
        encoding.SetCharacterName(44, "comma");
        encoding.SetCharacterName(45, "minus");
        encoding.SetCharacterName(46, "period");
        encoding.SetCharacterName(47, "slash");

        // Greek letters start at 65 (Alpha)
        encoding.SetCharacterName(65, "Alpha");
        encoding.SetCharacterName(66, "Beta");
        encoding.SetCharacterName(67, "Chi");
        encoding.SetCharacterName(68, "Delta");
        encoding.SetCharacterName(69, "Epsilon");
        encoding.SetCharacterName(70, "Phi");
        encoding.SetCharacterName(71, "Gamma");
        encoding.SetCharacterName(72, "Eta");

        return encoding;
    }

    // ZapfDingbatsEncoding (Dingbats font encoding)
    private static PdfFontEncoding CreateZapfDingbatsEncoding()
    {
        var encoding = new PdfFontEncoding("ZapfDingbatsEncoding");

        // ZapfDingbats contains ornamental characters and symbols
        encoding.SetCharacterName(32, "space");
        encoding.SetCharacterName(33, "a1"); // Scissors
        encoding.SetCharacterName(34, "a2"); // Scissors
        encoding.SetCharacterName(35, "a202"); // Checkmark
        encoding.SetCharacterName(36, "a3"); // Cross mark

        return encoding;
    }
}
