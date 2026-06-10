using System.Collections.Generic;

namespace FontParser.Tables.Cff
{
    /// <summary>
    /// Adobe StandardEncoding (code → glyph name), per ISO 32000 Annex D / the PostScript
    /// Language Reference. Used to resolve the bchar/achar operands of a seac-style endchar,
    /// which reference the base and accent glyphs by their StandardEncoding code.
    /// </summary>
    public static class StandardEncoding
    {
        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();

        static StandardEncoding()
        {
            for (var c = 'A'; c <= 'Z'; c++) Names[c] = c.ToString();
            for (var c = 'a'; c <= 'z'; c++) Names[c] = c.ToString();

            var defined = new (int Code, string Name)[]
            {
                (32, "space"), (33, "exclam"), (34, "quotedbl"), (35, "numbersign"), (36, "dollar"),
                (37, "percent"), (38, "ampersand"), (39, "quoteright"), (40, "parenleft"), (41, "parenright"),
                (42, "asterisk"), (43, "plus"), (44, "comma"), (45, "hyphen"), (46, "period"), (47, "slash"),
                (48, "zero"), (49, "one"), (50, "two"), (51, "three"), (52, "four"), (53, "five"), (54, "six"),
                (55, "seven"), (56, "eight"), (57, "nine"), (58, "colon"), (59, "semicolon"), (60, "less"),
                (61, "equal"), (62, "greater"), (63, "question"), (64, "at"),
                (91, "bracketleft"), (92, "backslash"), (93, "bracketright"), (94, "asciicircum"),
                (95, "underscore"), (96, "quoteleft"), (123, "braceleft"), (124, "bar"), (125, "braceright"),
                (126, "asciitilde"),
                (161, "exclamdown"), (162, "cent"), (163, "sterling"), (164, "fraction"), (165, "yen"),
                (166, "florin"), (167, "section"), (168, "currency"), (169, "quotesingle"), (170, "quotedblleft"),
                (171, "guillemotleft"), (172, "guilsinglleft"), (173, "guilsinglright"), (174, "fi"), (175, "fl"),
                (177, "endash"), (178, "dagger"), (179, "daggerdbl"), (180, "periodcentered"), (182, "paragraph"),
                (183, "bullet"), (184, "quotesinglbase"), (185, "quotedblbase"), (186, "quotedblright"),
                (187, "guillemotright"), (188, "ellipsis"), (189, "perthousand"), (191, "questiondown"),
                (193, "grave"), (194, "acute"), (195, "circumflex"), (196, "tilde"), (197, "macron"),
                (198, "breve"), (199, "dotaccent"), (200, "dieresis"), (202, "ring"), (203, "cedilla"),
                (205, "hungarumlaut"), (206, "ogonek"), (207, "caron"), (208, "emdash"),
                (225, "AE"), (227, "ordfeminine"), (232, "Lslash"), (233, "Oslash"), (234, "OE"),
                (235, "ordmasculine"), (241, "ae"), (245, "dotlessi"), (248, "lslash"), (249, "oslash"),
                (250, "oe"), (251, "germandbls")
            };
            foreach ((int code, string name) in defined) Names[code] = name;
        }

        /// <summary>Glyph name for a StandardEncoding code, or null if the code is undefined.</summary>
        public static string? GetName(int code) => Names.TryGetValue(code, out string? n) ? n : null;
    }
}
