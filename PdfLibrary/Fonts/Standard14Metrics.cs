namespace PdfLibrary.Fonts;

/// <summary>
/// Adobe AFM advance-width metrics for the Standard-14 fonts (Helvetica/Times/Courier families),
/// plus /BaseFont-name normalisation (ISO 32000-1 §9.6.2.1 / §9.10.1).
///
/// A non-embedded simple font (Type1 or TrueType) may legally omit its /Widths array; the viewer
/// must then lay the text out with the AFM metrics of the Standard-14 font the /BaseFont maps to.
/// The mapping covers the Windows aliases producers emit — TimesNewRoman→Times-Roman,
/// Arial,Bold→Helvetica-Bold, CourierNew→Courier — as well as the canonical PostScript names.
/// Shared by <see cref="TrueTypeFont"/> and <see cref="Type1Font"/>.
/// </summary>
internal static class Standard14Metrics
{
    /// <summary>
    /// Canonical Standard-14 PostScript name for <paramref name="baseFont"/> (e.g. "Times-Bold"), or
    /// null when the name is not a recognised Standard-14 family. Strips a subset tag ("ABCDEF+"),
    /// splits "Family-Style"/"Family,Style", and folds bold/italic(oblique) style flags.
    /// </summary>
    public static string? CanonicalName(string? baseFont)
    {
        if (string.IsNullOrWhiteSpace(baseFont))
            return null;

        string name = baseFont;
        int plus = name.IndexOf('+');
        if (plus == 6) name = name[(plus + 1)..];          // strip "ABCDEF+" subset tag

        string core = name, style = string.Empty;
        int sep = name.IndexOfAny(['-', ',']);
        if (sep >= 0)
        {
            core = name[..sep];
            style = name[(sep + 1)..];
        }

        string c = core.Replace(" ", string.Empty).ToLowerInvariant();
        string s = style.Replace(" ", string.Empty).ToLowerInvariant();
        bool bold = s.Contains("bold");
        bool italic = s.Contains("italic") || s.Contains("oblique");

        return c switch
        {
            "times" or "timesroman" or "timesnewroman" or "timesnewromanps" or "timesnewromanpsmt" =>
                (bold, italic) switch
                {
                    (true, true) => "Times-BoldItalic",
                    (true, false) => "Times-Bold",
                    (false, true) => "Times-Italic",
                    _ => "Times-Roman"
                },
            "helvetica" or "arial" or "arialmt" or "helv" =>
                (bold, italic) switch
                {
                    (true, true) => "Helvetica-BoldOblique",
                    (true, false) => "Helvetica-Bold",
                    (false, true) => "Helvetica-Oblique",
                    _ => "Helvetica"
                },
            "courier" or "couriernew" or "couriernewps" or "couriernewpsmt" =>
                (bold, italic) switch
                {
                    (true, true) => "Courier-BoldOblique",
                    (true, false) => "Courier-Bold",
                    (false, true) => "Courier-Oblique",
                    _ => "Courier"
                },
            "symbol" => "Symbol",
            "zapfdingbats" or "dingbats" => "ZapfDingbats",
            _ => null
        };
    }

    /// <summary>
    /// AFM advance width (1000-unit em) for the glyph named <paramref name="glyphName"/> in the
    /// Standard-14 font <paramref name="baseFont"/> maps to. Null when no glyph name is supplied,
    /// the base font is not a recognised Standard-14 family, or the glyph name is not present in
    /// that face's vendored AFM (see <see cref="AfmMetrics.ForFace"/>).
    /// </summary>
    public static double? WidthByName(string? baseFont, string? glyphName)
    {
        if (string.IsNullOrEmpty(glyphName))
            return null;
        string? canonical = CanonicalName(baseFont);
        if (canonical is null)
            return null;

        // Courier is monospaced — all 315 glyphs are 600 — so it needs no vendored AFM.
        if (canonical.StartsWith("Courier", StringComparison.Ordinal))
            return 600;

        // The Oblique faces are width-identical to their upright counterparts, verified against the
        // AFMs, so they share a table rather than duplicating one.
        string face = canonical switch
        {
            "Helvetica-Oblique" => "Helvetica",
            "Helvetica-BoldOblique" => "Helvetica-Bold",
            _ => canonical,
        };

        return AfmMetrics.ForFace(face) is { } widths && widths.TryGetValue(glyphName, out double w)
            ? w
            : null;
    }

    /// <summary>
    /// AFM advance width by raw character code, used only when no glyph name is available (no
    /// /Encoding). Returns null when the base font is not a recognised Standard-14 family.
    /// </summary>
    public static double? WidthByCode(string? baseFont, int charCode)
    {
        string? canonical = CanonicalName(baseFont);
        if (canonical is null)
            return null;

        return canonical switch
        {
            "Helvetica" or "Helvetica-Oblique" => GetHelveticaWidth(charCode),
            "Helvetica-Bold" or "Helvetica-BoldOblique" => GetHelveticaBoldWidth(charCode),
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
    /// Helvetica-Bold character widths (WinAnsi encoding)
    /// Source: Adobe Font Metrics (AFM) files - Helvetica-Bold.afm
    /// Bold variants have different widths than regular Helvetica
    /// </summary>
    private static double GetHelveticaBoldWidth(int charCode)
    {
        return charCode switch
        {
            32 => 278,   // space
            33 => 333,   // exclam
            34 => 474,   // quotedbl
            35 => 556,   // numbersign
            36 => 556,   // dollar
            37 => 889,   // percent
            38 => 722,   // ampersand
            39 => 238,   // quotesingle
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
            58 => 333,   // colon
            59 => 333,   // semicolon
            60 => 584,   // less
            61 => 584,   // equal
            62 => 584,   // greater
            63 => 611,   // question
            64 => 975,   // at
            65 => 722,   // A
            66 => 722,   // B
            67 => 722,   // C
            68 => 722,   // D
            69 => 667,   // E
            70 => 611,   // F
            71 => 778,   // G
            72 => 722,   // H
            73 => 278,   // I
            74 => 556,   // J
            75 => 722,   // K
            76 => 611,   // L
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
            91 => 333,   // bracketleft
            92 => 278,   // backslash
            93 => 333,   // bracketright
            94 => 584,   // asciicircum
            95 => 556,   // underscore
            96 => 333,   // grave
            97 => 556,   // a
            98 => 611,   // b
            99 => 556,   // c
            100 => 611,  // d
            101 => 556,  // e
            102 => 333,  // f
            103 => 611,  // g
            104 => 611,  // h
            105 => 278,  // i
            106 => 278,  // j
            107 => 556,  // k
            108 => 278,  // l
            109 => 889,  // m
            110 => 611,  // n
            111 => 611,  // o
            112 => 611,  // p
            113 => 611,  // q
            114 => 389,  // r
            115 => 556,  // s
            116 => 333,  // t
            117 => 611,  // u
            118 => 556,  // v
            119 => 778,  // w
            120 => 556,  // x
            121 => 556,  // y
            122 => 500,  // z
            123 => 389,  // braceleft
            124 => 280,  // bar
            125 => 389,  // braceright
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
}
