namespace PdfLibrary.Fonts
{
    /// <summary>
    /// Adobe Glyph List - maps PostScript glyph names to Unicode values
    /// Based on Adobe Glyph List Specification (AGL)
    /// https://github.com/adobe-type-tools/agl-specification
    ///
    /// This is a focused subset including:
    /// - Ligatures (fi, fl, ff, ffi, ffl)
    /// - Common accented characters
    /// - Common symbols
    /// - Basic Latin characters
    /// </summary>
    public static class AdobeGlyphList
    {
        private static readonly Dictionary<string, string> _glyphToUnicode = new Dictionary<string, string>
        {
            // Ligatures - MOST IMPORTANT for our use case
            ["fi"] = "\uFB01",      // Latin Small Ligature FI
            ["fl"] = "\uFB02",      // Latin Small Ligature FL
            ["ff"] = "\uFB00",      // Latin Small Ligature FF
            ["ffi"] = "\uFB03",     // Latin Small Ligature FFI
            ["ffl"] = "\uFB04",     // Latin Small Ligature FFL

            // Basic Latin
            ["A"] = "A", ["B"] = "B", ["C"] = "C", ["D"] = "D", ["E"] = "E",
            ["F"] = "F", ["G"] = "G", ["H"] = "H", ["I"] = "I", ["J"] = "J",
            ["K"] = "K", ["L"] = "L", ["M"] = "M", ["N"] = "N", ["O"] = "O",
            ["P"] = "P", ["Q"] = "Q", ["R"] = "R", ["S"] = "S", ["T"] = "T",
            ["U"] = "U", ["V"] = "V", ["W"] = "W", ["X"] = "X", ["Y"] = "Y", ["Z"] = "Z",
            ["a"] = "a", ["b"] = "b", ["c"] = "c", ["d"] = "d", ["e"] = "e",
            ["f"] = "f", ["g"] = "g", ["h"] = "h", ["i"] = "i", ["j"] = "j",
            ["k"] = "k", ["l"] = "l", ["m"] = "m", ["n"] = "n", ["o"] = "o",
            ["p"] = "p", ["q"] = "q", ["r"] = "r", ["s"] = "s", ["t"] = "t",
            ["u"] = "u", ["v"] = "v", ["w"] = "w", ["x"] = "x", ["y"] = "y", ["z"] = "z",

            // Numbers
            ["zero"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4",
            ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9",

            // Common punctuation
            ["space"] = " ",
            ["exclam"] = "!",
            ["quotedbl"] = "\"",
            ["numbersign"] = "#",
            ["dollar"] = "$",
            ["percent"] = "%",
            ["ampersand"] = "&",
            ["quotesingle"] = "'",
            ["parenleft"] = "(",
            ["parenright"] = ")",
            ["asterisk"] = "*",
            ["plus"] = "+",
            ["comma"] = ",",
            ["hyphen"] = "-",
            ["period"] = ".",
            ["slash"] = "/",
            ["colon"] = ":",
            ["semicolon"] = ";",
            ["less"] = "<",
            ["equal"] = "=",
            ["greater"] = ">",
            ["question"] = "?",
            ["at"] = "@",
            ["bracketleft"] = "[",
            ["backslash"] = "\\",
            ["bracketright"] = "]",
            ["asciicircum"] = "^",
            ["underscore"] = "_",
            ["grave"] = "`",
            ["braceleft"] = "{",
            ["bar"] = "|",
            ["braceright"] = "}",
            ["asciitilde"] = "~",

            // Latin-1 Supplement - Accented characters
            ["Agrave"] = "\u00C0", ["Aacute"] = "\u00C1", ["Acircumflex"] = "\u00C2",
            ["Atilde"] = "\u00C3", ["Adieresis"] = "\u00C4", ["Aring"] = "\u00C5",
            ["AE"] = "\u00C6", ["Ccedilla"] = "\u00C7",
            ["Egrave"] = "\u00C8", ["Eacute"] = "\u00C9", ["Ecircumflex"] = "\u00CA",
            ["Edieresis"] = "\u00CB",
            ["Igrave"] = "\u00CC", ["Iacute"] = "\u00CD", ["Icircumflex"] = "\u00CE",
            ["Idieresis"] = "\u00CF",
            ["Eth"] = "\u00D0", ["Ntilde"] = "\u00D1",
            ["Ograve"] = "\u00D2", ["Oacute"] = "\u00D3", ["Ocircumflex"] = "\u00D4",
            ["Otilde"] = "\u00D5", ["Odieresis"] = "\u00D6",
            ["Oslash"] = "\u00D8", ["Ugrave"] = "\u00D9", ["Uacute"] = "\u00DA",
            ["Ucircumflex"] = "\u00DB", ["Udieresis"] = "\u00DC",
            ["Yacute"] = "\u00DD", ["Thorn"] = "\u00DE",
            ["germandbls"] = "\u00DF",
            ["agrave"] = "\u00E0", ["aacute"] = "\u00E1", ["acircumflex"] = "\u00E2",
            ["atilde"] = "\u00E3", ["adieresis"] = "\u00E4", ["aring"] = "\u00E5",
            ["ae"] = "\u00E6", ["ccedilla"] = "\u00E7",
            ["egrave"] = "\u00E8", ["eacute"] = "\u00E9", ["ecircumflex"] = "\u00EA",
            ["edieresis"] = "\u00EB",
            ["igrave"] = "\u00EC", ["iacute"] = "\u00ED", ["icircumflex"] = "\u00EE",
            ["idieresis"] = "\u00EF",
            ["eth"] = "\u00F0", ["ntilde"] = "\u00F1",
            ["ograve"] = "\u00F2", ["oacute"] = "\u00F3", ["ocircumflex"] = "\u00F4",
            ["otilde"] = "\u00F5", ["odieresis"] = "\u00F6",
            ["oslash"] = "\u00F8", ["ugrave"] = "\u00F9", ["uacute"] = "\u00FA",
            ["ucircumflex"] = "\u00FB", ["udieresis"] = "\u00FC",
            ["yacute"] = "\u00FD", ["thorn"] = "\u00FE", ["ydieresis"] = "\u00FF",

            // Common symbols
            ["bullet"] = "\u2022",
            ["endash"] = "\u2013",
            ["emdash"] = "\u2014",
            ["quoteleft"] = "\u2018",
            ["quoteright"] = "\u2019",
            ["quotesinglbase"] = "\u201A",
            ["quotedblleft"] = "\u201C",
            ["quotedblright"] = "\u201D",
            ["quotedblbase"] = "\u201E",
            ["dagger"] = "\u2020",
            ["daggerdbl"] = "\u2021",
            ["ellipsis"] = "\u2026",
            ["perthousand"] = "\u2030",
            ["guilsinglleft"] = "\u2039",
            ["guilsinglright"] = "\u203A",
            ["trademark"] = "\u2122",

            // Currency
            ["cent"] = "\u00A2",
            ["sterling"] = "\u00A3",
            ["yen"] = "\u00A5",
            ["florin"] = "\u0192",
            ["currency"] = "\u00A4",

            // Math
            ["minus"] = "\u2212",
            ["multiply"] = "\u00D7",
            ["divide"] = "\u00F7",
            ["plusminus"] = "\u00B1",

            // Special
            ["dotlessi"] = "\u0131",
            ["OE"] = "\u0152",
            ["oe"] = "\u0153",
            ["Scaron"] = "\u0160",
            ["scaron"] = "\u0161",
            ["Ydieresis"] = "\u0178",
            ["Zcaron"] = "\u017D",
            ["zcaron"] = "\u017E",
            ["Lslash"] = "\u0141",
            ["lslash"] = "\u0142",

            // Fractions
            ["onehalf"] = "\u00BD",
            ["onequarter"] = "\u00BC",
            ["threequarters"] = "\u00BE",
            ["oneeighth"] = "\u215B",
            ["threeeighths"] = "\u215C",
            ["fiveeighths"] = "\u215D",
            ["seveneighths"] = "\u215E",
            ["onethird"] = "\u2153",
            ["twothirds"] = "\u2154",

            // Special characters
            [".notdef"] = "\uFFFD",  // Replacement character
            ["space"] = " ",
            ["nonbreakingspace"] = "\u00A0",
            ["section"] = "\u00A7",
            ["paragraph"] = "\u00B6",
            ["copyright"] = "\u00A9",
            ["registered"] = "\u00AE",
            ["degree"] = "\u00B0",
            ["mu"] = "\u00B5",
            ["ordfeminine"] = "\u00AA",
            ["ordmasculine"] = "\u00BA",
        };

        /// <summary>
        /// Map glyph name to Unicode string
        /// </summary>
        /// <param name="glyphName">PostScript glyph name (e.g., "fi", "Aacute")</param>
        /// <returns>Unicode string, or null if not found</returns>
        public static string? GetUnicode(string glyphName)
        {
            if (string.IsNullOrEmpty(glyphName))
                return null;

            return _glyphToUnicode.TryGetValue(glyphName, out var unicode) ? unicode : null;
        }

        /// <summary>
        /// Check if a glyph name has a known Unicode mapping
        /// </summary>
        public static bool HasMapping(string glyphName)
        {
            return _glyphToUnicode.ContainsKey(glyphName);
        }

        /// <summary>
        /// Get all glyph name to Unicode mappings
        /// </summary>
        public static IReadOnlyDictionary<string, string> GetAllMappings()
        {
            return _glyphToUnicode;
        }

        // Reverse mapping: Unicode to glyph name (lazily initialized)
        private static Dictionary<string, string>? _unicodeToGlyph;
        private static readonly object _initLock = new();

        /// <summary>
        /// Get glyph name from Unicode character
        /// Used for Type0 fonts with embedded Type1 data where we need to map
        /// Unicode (from ToUnicode CMap) back to PostScript glyph names
        /// </summary>
        /// <param name="unicode">Unicode string (single character or ligature)</param>
        /// <returns>PostScript glyph name, or null if not found</returns>
        public static string? GetGlyphName(string unicode)
        {
            if (string.IsNullOrEmpty(unicode))
                return null;

            // Initialize reverse mapping on first use
            if (_unicodeToGlyph is null)
            {
                lock (_initLock)
                {
                    if (_unicodeToGlyph is null)
                    {
                        _unicodeToGlyph = new Dictionary<string, string>();
                        foreach (var kvp in _glyphToUnicode)
                        {
                            // Only add if not already present (first glyph name wins)
                            if (!_unicodeToGlyph.ContainsKey(kvp.Value))
                            {
                                _unicodeToGlyph[kvp.Value] = kvp.Key;
                            }
                        }
                    }
                }
            }

            return _unicodeToGlyph.TryGetValue(unicode, out var glyphName) ? glyphName : null;
        }

        /// <summary>
        /// Get glyph name from Unicode code point
        /// </summary>
        /// <param name="codePoint">Unicode code point</param>
        /// <returns>PostScript glyph name, or null if not found</returns>
        public static string? GetGlyphName(int codePoint)
        {
            var unicode = char.ConvertFromUtf32(codePoint);
            return GetGlyphName(unicode);
        }
    }
}
