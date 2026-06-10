namespace FontParser.Tables
{
    /// <summary>
    /// Standard Macintosh glyph ordering used by the 'post' table format 2.0.
    /// Name indices 0–257 reference these standard names; 258+ index into the
    /// font's own list of Pascal-string names.
    /// </summary>
    public static class MacintoshGlyphNames
    {
        public const int StandardNameCount = 258;

        private static readonly string[] StandardNames = new string[]
        {
            ".notdef", ".null", "nonmarkingreturn", "space", "exclam", "quotedbl", "numbersign",
            "dollar", "percent", "ampersand", "quotesingle", "parenleft", "parenright", "asterisk",
            "plus", "comma", "hyphen", "period", "slash", "zero", "one", "two", "three", "four",
            "five", "six", "seven", "eight", "nine", "colon", "semicolon", "less", "equal",
            "greater", "question", "at", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K",
            "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "bracketleft", "backslash", "bracketright", "asciicircum", "underscore", "grave",
            "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q",
            "r", "s", "t", "u", "v", "w", "x", "y", "z", "braceleft", "bar", "braceright",
            "asciitilde", "Adieresis", "Aring", "Ccedilla", "Eacute", "Ntilde", "Odieresis",
            "Udieresis", "aacute", "agrave", "acircumflex", "adieresis", "atilde", "aring",
            "ccedilla", "eacute", "egrave", "ecircumflex", "edieresis", "iacute", "igrave",
            "icircumflex", "idieresis", "ntilde", "oacute", "ograve", "ocircumflex", "odieresis",
            "otilde", "uacute", "ugrave", "ucircumflex", "udieresis", "dagger", "degree", "cent",
            "sterling", "section", "bullet", "paragraph", "germandbls", "registered", "copyright",
            "trademark", "acute", "dieresis", "notequal", "AE", "Oslash", "infinity", "plusminus",
            "lessequal", "greaterequal", "yen", "mu", "partialdiff", "summation", "product", "pi",
            "integral", "ordfeminine", "ordmasculine", "Omega", "ae", "oslash", "questiondown",
            "exclamdown", "logicalnot", "radical", "florin", "approxequal", "Delta", "guillemotleft",
            "guillemotright", "ellipsis", "nonbreakingspace", "Agrave", "Atilde", "Otilde", "OE",
            "oe", "endash", "emdash", "quotedblleft", "quotedblright", "quoteleft", "quoteright",
            "divide", "lozenge", "ydieresis", "Ydieresis", "fraction", "currency", "guilsinglleft",
            "guilsinglright", "fi", "fl", "daggerdbl", "periodcentered", "quotesinglbase",
            "quotedblbase", "perthousand", "Acircumflex", "Ecircumflex", "Aacute", "Edieresis",
            "Egrave", "Iacute", "Icircumflex", "Idieresis", "Igrave", "Oacute", "Ocircumflex",
            "apple", "Ograve", "Uacute", "Ucircumflex", "Ugrave", "dotlessi", "circumflex",
            "tilde", "macron", "breve", "dotaccent", "ring", "cedilla", "hungarumlaut", "ogonek",
            "caron", "Lslash", "lslash", "Scaron", "scaron", "Zcaron", "zcaron", "brokenbar",
            "Eth", "eth", "Yacute", "yacute", "Thorn", "thorn", "minus", "multiply",
            "onesuperior", "twosuperior", "threesuperior", "onehalf", "onequarter", "threequarters",
            "franc", "Gbreve", "gbreve", "Idotaccent", "Scedilla", "scedilla", "Cacute", "cacute",
            "Ccaron", "ccaron", "dcroat"
        };

        public static string? GetStandardName(int index)
        {
            if (index < 0 || index >= StandardNameCount)
                return null;
            return StandardNames[index];
        }
    }
}
