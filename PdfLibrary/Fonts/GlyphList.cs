using System.Globalization;
using System.Text;

namespace PdfLibrary.Fonts;

/// <summary>
/// Maps Adobe glyph names to Unicode code points
/// Based on Adobe Glyph List (AGL) specification
/// </summary>
public static class GlyphList
{
    private static readonly Dictionary<string, string> _glyphToUnicode = InitializeGlyphList();

    /// <summary>
    /// Gets the Unicode character for a glyph name
    /// </summary>
    public static string? GetUnicode(string glyphName)
    {
        if (_glyphToUnicode.TryGetValue(glyphName, out string? unicode))
            return unicode;

        // Handle uniXXXX format (direct Unicode encoding)
        if (!glyphName.StartsWith("uni") || glyphName.Length != 7) return null;
        return int.TryParse(glyphName[3..], NumberStyles.HexNumber, null, out int codePoint)
            ? char.ConvertFromUtf32(codePoint)
            : null;
    }

    // Reverse mapping (Unicode -> glyph name), lazily built; first name in table order wins.
    private static volatile Dictionary<string, string>? _unicodeToGlyph;
    private static readonly object _reverseLock = new();

    /// <summary>
    /// Gets a PostScript glyph name for a Unicode string (reverse of <see cref="GetUnicode"/>).
    /// Used when mapping ToUnicode values back to glyph names for name-keyed fonts.
    /// </summary>
    public static string? GetGlyphName(string unicode)
    {
        if (string.IsNullOrEmpty(unicode)) return null;

        if (_unicodeToGlyph is null)
        {
            lock (_reverseLock)
            {
                if (_unicodeToGlyph is null)
                {
                    var reverse = new Dictionary<string, string>();
                    foreach (KeyValuePair<string, string> kvp in _glyphToUnicode)
                        if (!reverse.ContainsKey(kvp.Value)) reverse[kvp.Value] = kvp.Key;
                    _unicodeToGlyph = reverse;
                }
            }
        }

        return _unicodeToGlyph.GetValueOrDefault(unicode);
    }

    /// <summary>Gets a PostScript glyph name for a Unicode code point, or null.</summary>
    public static string? GetGlyphName(int codePoint) => GetGlyphName(char.ConvertFromUtf32(codePoint));

    private static Dictionary<string, string> InitializeGlyphList()
    {
        Dictionary<string, string> hand = BuildHandTable();
        AddAglSupplement(hand);
        return hand;
    }

    /// <summary>
    /// Adds every name from the vendored canonical Adobe Glyph List (Resources/Agl/glyphlist.txt;
    /// see Resources/Agl/LICENSE.md for source, pinned commit, and SHA256) that is NOT already a
    /// key in <paramref name="dict"/>. Runs AFTER the hand table is fully populated, so every
    /// existing forward entry and every existing first-name-wins reverse-map choice is preserved —
    /// this only ADDS names the hand table lacks (afii*, angle, aleph, universal, ...). A
    /// multi-codepoint AGL value (space-separated hex) joins to its full UTF-16 string.
    ///
    /// <para><b>Four deliberate hand-vs-AGL overrides</b> (found while auditing every hand-table name
    /// against its AGL value — verified via <c>Resources/Agl/glyphlist.txt</c>, not memory): the hand
    /// table's own value for these four names DIFFERS from what the canonical AGL says for the same
    /// name, and — because this method only adds ABSENT names — the hand table's value keeps
    /// winning, unchanged by Task 10:
    /// <list type="bullet">
    /// <item><c>Delta</c>: hand U+0394 (Greek capital delta, Δ) vs AGL U+2206 (∆ INCREMENT). AGL's
    /// own name for Δ is <c>Deltagreek</c>.</item>
    /// <item><c>Omega</c>: hand U+03A9 (Greek capital omega, Ω) vs AGL U+2126 (Ω OHM SIGN, a distinct
    /// codepoint). AGL's own name for the Greek letter is <c>Omegagreek</c>.</item>
    /// <item><c>mu</c>: hand U+03BC (Greek small mu, μ — the hand table's Basic-Latin/Greek sections
    /// assign this key twice; the later, Greek-section assignment wins) vs AGL U+00B5 (µ MICRO SIGN,
    /// under both <c>mu</c> and <c>mu1</c>).</item>
    /// <item><c>rupiah</c>: hand U+20A8 (₨ RUPEE SIGN) vs AGL U+F6DD (a PUA/expert-set codepoint).</item>
    /// </list>
    /// </para>
    /// </summary>
    private static void AddAglSupplement(Dictionary<string, string> dict)
    {
        using Stream? raw = typeof(GlyphList).Assembly
            .GetManifestResourceStream("PdfLibrary.Resources.Agl.glyphlist.txt");
        if (raw is null) return; // defensive: resource is always embedded; never expected to miss

        using var reader = new StreamReader(raw);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;

            int semi = line.IndexOf(';');
            if (semi < 0) continue;

            string name = line[..semi];
            if (dict.ContainsKey(name)) continue; // hand table wins; only add absent names

            string[] hexCodes = line[(semi + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (hexCodes.Length == 0) continue;

            var value = new StringBuilder();
            var ok = true;
            foreach (string hex in hexCodes)
            {
                if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int codePoint))
                {
                    ok = false;
                    break;
                }
                value.Append(char.ConvertFromUtf32(codePoint));
            }
            if (ok) dict[name] = value.ToString();
        }
    }

    private static Dictionary<string, string> BuildHandTable()
    {
        // Adobe Glyph List - common glyph name to Unicode mappings
        // Hand-built subset; completed at static init by AddAglSupplement from the vendored
        // canonical AGL (names absent here only).
        return new Dictionary<string, string>
        {
            // Basic Latin
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

            // Numbers
            ["zero"] = "0",
            ["one"] = "1",
            ["two"] = "2",
            ["three"] = "3",
            ["four"] = "4",
            ["five"] = "5",
            ["six"] = "6",
            ["seven"] = "7",
            ["eight"] = "8",
            ["nine"] = "9",

            // More punctuation
            ["colon"] = ":",
            ["semicolon"] = ";",
            ["less"] = "<",
            ["equal"] = "=",
            ["greater"] = ">",
            ["question"] = "?",
            ["at"] = "@",

            // Uppercase Latin
            ["A"] = "A",
            ["B"] = "B",
            ["C"] = "C",
            ["D"] = "D",
            ["E"] = "E",
            ["F"] = "F",
            ["G"] = "G",
            ["H"] = "H",
            ["I"] = "I",
            ["J"] = "J",
            ["K"] = "K",
            ["L"] = "L",
            ["M"] = "M",
            ["N"] = "N",
            ["O"] = "O",
            ["P"] = "P",
            ["Q"] = "Q",
            ["R"] = "R",
            ["S"] = "S",
            ["T"] = "T",
            ["U"] = "U",
            ["V"] = "V",
            ["W"] = "W",
            ["X"] = "X",
            ["Y"] = "Y",
            ["Z"] = "Z",

            // Brackets
            ["bracketleft"] = "[",
            ["backslash"] = "\\",
            ["bracketright"] = "]",
            ["asciicircum"] = "^",
            ["underscore"] = "_",
            ["grave"] = "`",

            // Lowercase Latin
            ["a"] = "a",
            ["b"] = "b",
            ["c"] = "c",
            ["d"] = "d",
            ["e"] = "e",
            ["f"] = "f",
            ["g"] = "g",
            ["h"] = "h",
            ["i"] = "i",
            ["j"] = "j",
            ["k"] = "k",
            ["l"] = "l",
            ["m"] = "m",
            ["n"] = "n",
            ["o"] = "o",
            ["p"] = "p",
            ["q"] = "q",
            ["r"] = "r",
            ["s"] = "s",
            ["t"] = "t",
            ["u"] = "u",
            ["v"] = "v",
            ["w"] = "w",
            ["x"] = "x",
            ["y"] = "y",
            ["z"] = "z",

            // More punctuation
            ["braceleft"] = "{",
            ["bar"] = "|",
            ["braceright"] = "}",
            ["asciitilde"] = "~",

            // Latin-1 Supplement
            ["exclamdown"] = "¡",
            ["cent"] = "¢",
            ["sterling"] = "£",
            ["currency"] = "¤",
            ["yen"] = "¥",
            ["brokenbar"] = "¦",
            ["section"] = "§",
            ["dieresis"] = "¨",
            ["copyright"] = "©",
            ["ordfeminine"] = "ª",
            ["guillemotleft"] = "«",
            ["logicalnot"] = "¬",
            ["registered"] = "®",
            ["macron"] = "¯",
            ["degree"] = "°",
            ["plusminus"] = "±",
            ["twosuperior"] = "²",
            ["threesuperior"] = "³",
            ["acute"] = "´",
            ["mu"] = "µ",
            ["paragraph"] = "¶",
            ["periodcentered"] = "·",
            ["cedilla"] = "¸",
            ["onesuperior"] = "¹",
            ["ordmasculine"] = "º",
            ["guillemotright"] = "»",
            ["onequarter"] = "¼",
            ["onehalf"] = "½",
            ["threequarters"] = "¾",
            ["questiondown"] = "¿",

            // Latin Extended-A (accented characters)
            ["Agrave"] = "À",
            ["Aacute"] = "Á",
            ["Acircumflex"] = "Â",
            ["Atilde"] = "Ã",
            ["Adieresis"] = "Ä",
            ["Aring"] = "Å",
            ["AE"] = "Æ",
            ["Ccedilla"] = "Ç",
            ["Egrave"] = "È",
            ["Eacute"] = "É",
            ["Ecircumflex"] = "Ê",
            ["Edieresis"] = "Ë",
            ["Igrave"] = "Ì",
            ["Iacute"] = "Í",
            ["Icircumflex"] = "Î",
            ["Idieresis"] = "Ï",
            ["Eth"] = "Ð",
            ["Ntilde"] = "Ñ",
            ["Ograve"] = "Ò",
            ["Oacute"] = "Ó",
            ["Ocircumflex"] = "Ô",
            ["Otilde"] = "Õ",
            ["Odieresis"] = "Ö",
            ["multiply"] = "×",
            ["Oslash"] = "Ø",
            ["Ugrave"] = "Ù",
            ["Uacute"] = "Ú",
            ["Ucircumflex"] = "Û",
            ["Udieresis"] = "Ü",
            ["Yacute"] = "Ý",
            ["Thorn"] = "Þ",
            ["germandbls"] = "ß",
            ["agrave"] = "à",
            ["aacute"] = "á",
            ["acircumflex"] = "â",
            ["atilde"] = "ã",
            ["adieresis"] = "ä",
            ["aring"] = "å",
            ["ae"] = "æ",
            ["ccedilla"] = "ç",
            ["egrave"] = "è",
            ["eacute"] = "é",
            ["ecircumflex"] = "ê",
            ["edieresis"] = "ë",
            ["igrave"] = "ì",
            ["iacute"] = "í",
            ["icircumflex"] = "î",
            ["idieresis"] = "ï",
            ["eth"] = "ð",
            ["ntilde"] = "ñ",
            ["ograve"] = "ò",
            ["oacute"] = "ó",
            ["ocircumflex"] = "ô",
            ["otilde"] = "õ",
            ["odieresis"] = "ö",
            ["divide"] = "÷",
            ["oslash"] = "ø",
            ["ugrave"] = "ù",
            ["uacute"] = "ú",
            ["ucircumflex"] = "û",
            ["udieresis"] = "ü",
            ["yacute"] = "ý",
            ["thorn"] = "þ",
            ["ydieresis"] = "ÿ",

            // Ligatures
            ["fi"] = "\ufb01",
            ["fl"] = "\ufb02",
            ["ff"] = "\ufb00",
            ["ffi"] = "\ufb03",
            ["ffl"] = "\ufb04",

            // Punctuation
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
            ["bullet"] = "\u2022",
            ["ellipsis"] = "\u2026",
            ["perthousand"] = "\u2030",
            ["guilsinglleft"] = "\u2039",
            ["guilsinglright"] = "\u203A",
            ["fraction"] = "\u2044",

            // Currency
            ["Euro"] = "\u20AC",
            ["franc"] = "\u20A3",
            ["lira"] = "\u20A4",
            ["peseta"] = "\u20A7",
            ["dong"] = "\u20AB",
            ["rupiah"] = "\u20A8",

            // Greek (uppercase)
            ["Alpha"] = "\u0391",
            ["Beta"] = "\u0392",
            ["Gamma"] = "\u0393",
            ["Delta"] = "\u0394",
            ["Epsilon"] = "\u0395",
            ["Zeta"] = "\u0396",
            ["Eta"] = "\u0397",
            ["Theta"] = "\u0398",
            ["Iota"] = "\u0399",
            ["Kappa"] = "\u039A",
            ["Lambda"] = "\u039B",
            ["Mu"] = "\u039C",
            ["Nu"] = "\u039D",
            ["Xi"] = "\u039E",
            ["Omicron"] = "\u039F",
            ["Pi"] = "\u03A0",
            ["Rho"] = "\u03A1",
            ["Sigma"] = "\u03A3",
            ["Tau"] = "\u03A4",
            ["Upsilon"] = "\u03A5",
            ["Phi"] = "\u03A6",
            ["Chi"] = "\u03A7",
            ["Psi"] = "\u03A8",
            ["Omega"] = "\u03A9",

            // Greek (lowercase)
            ["alpha"] = "\u03B1",
            ["beta"] = "\u03B2",
            ["gamma"] = "\u03B3",
            ["delta"] = "\u03B4",
            ["epsilon"] = "\u03B5",
            ["zeta"] = "\u03B6",
            ["eta"] = "\u03B7",
            ["theta"] = "\u03B8",
            ["iota"] = "\u03B9",
            ["kappa"] = "\u03BA",
            ["lambda"] = "\u03BB",
            ["mu"] = "\u03BC",
            ["nu"] = "\u03BD",
            ["xi"] = "\u03BE",
            ["omicron"] = "\u03BF",
            ["pi"] = "\u03C0",
            ["rho"] = "\u03C1",
            ["sigma"] = "\u03C3",
            ["tau"] = "\u03C4",
            ["upsilon"] = "\u03C5",
            ["phi"] = "\u03C6",
            ["chi"] = "\u03C7",
            ["psi"] = "\u03C8",
            ["omega"] = "\u03C9",

            // Math symbols
            ["summation"] = "\u2211",
            ["product"] = "\u220F",
            ["integral"] = "\u222B",
            ["radical"] = "\u221A",
            ["infinity"] = "\u221E",
            ["partialdiff"] = "\u2202",
            ["increment"] = "\u2206",
            ["notequal"] = "\u2260",
            ["lessequal"] = "\u2264",
            ["greaterequal"] = "\u2265",
            ["approxequal"] = "\u2248",

            // Arrows
            ["arrowleft"] = "\u2190",
            ["arrowup"] = "\u2191",
            ["arrowright"] = "\u2192",
            ["arrowdown"] = "\u2193",

            // Merged from the former AdobeGlyphList (names not already mapped above)
            ["trademark"] = "\u2122",
            ["florin"] = "\u0192",
            ["nonbreakingspace"] = "\u00a0",
            ["minus"] = "\u2212",
            ["dotlessi"] = "\u0131",
            ["OE"] = "\u0152",
            ["oe"] = "\u0153",
            ["Scaron"] = "\u0160",
            ["scaron"] = "\u0161",
            // Issue 28: AGL spacing-modifier accents
            ["circumflex"] = "\u02c6",
            ["tilde"] = "\u02dc",
            ["breve"] = "\u02d8",
            ["dotaccent"] = "\u02d9",
            ["ring"] = "\u02da",
            ["hungarumlaut"] = "\u02dd",
            ["ogonek"] = "\u02db",
            ["caron"] = "\u02c7",
            ["Ydieresis"] = "\u0178",
            ["Zcaron"] = "\u017d",
            ["zcaron"] = "\u017e",
            ["Lslash"] = "\u0141",
            ["lslash"] = "\u0142",
            ["onethird"] = "\u2153",
            ["twothirds"] = "\u2154",
            ["oneeighth"] = "\u215b",
            ["threeeighths"] = "\u215c",
            ["fiveeighths"] = "\u215d",
            ["seveneighths"] = "\u215e",
            [".notdef"] = "\ufffd",
        };
    }
}
