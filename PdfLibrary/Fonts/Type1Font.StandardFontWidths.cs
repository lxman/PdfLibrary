namespace PdfLibrary.Fonts;

/// <summary>
/// Adobe AFM character widths for the standard-14 fonts (Helvetica/Times families).
/// Partial-class continuation of Type1Font, extracted from Type1Font.cs so the
/// width-lookup dispatchers there stay readable. Pure data; no behavior change.
/// </summary>
internal partial class Type1Font
{

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
    /// Helvetica character widths by glyph name (for custom encodings)
    /// Source: Adobe Font Metrics (AFM) files
    /// </summary>
    private static double GetHelveticaWidthByName(string glyphName)
    {
        return glyphName switch
        {
            "space" => 278,
            "exclam" => 278,
            "quotedbl" => 355,
            "numbersign" => 556,
            "dollar" => 556,
            "percent" => 889,
            "ampersand" => 667,
            "quotesingle" or "quoteright" => 191,
            "parenleft" => 333,
            "parenright" => 333,
            "asterisk" => 389,
            "plus" => 584,
            "comma" => 278,
            "hyphen" or "minus" => 333,
            "period" => 278,
            "slash" => 278,
            "zero" => 556,
            "one" => 556,
            "two" => 556,
            "three" => 556,
            "four" => 556,
            "five" => 556,
            "six" => 556,
            "seven" => 556,
            "eight" => 556,
            "nine" => 556,
            "colon" => 278,
            "semicolon" => 278,
            "less" => 584,
            "equal" => 584,
            "greater" => 584,
            "question" => 556,
            "at" => 1015,
            "A" => 667,
            "B" => 667,
            "C" => 722,
            "D" => 722,
            "E" => 667,
            "F" => 611,
            "G" => 778,
            "H" => 722,
            "I" => 278,
            "J" => 500,
            "K" => 667,
            "L" => 556,
            "M" => 833,
            "N" => 722,
            "O" => 778,
            "P" => 667,
            "Q" => 778,
            "R" => 722,
            "S" => 667,
            "T" => 611,
            "U" => 722,
            "V" => 667,
            "W" => 944,
            "X" => 667,
            "Y" => 667,
            "Z" => 611,
            "bracketleft" => 278,
            "backslash" => 278,
            "bracketright" => 278,
            "asciicircum" => 469,
            "underscore" => 556,
            "grave" or "quoteleft" => 333,
            "a" => 556,
            "b" => 556,
            "c" => 500,
            "d" => 556,
            "e" => 556,
            "f" => 278,
            "g" => 556,
            "h" => 556,
            "i" => 222,
            "j" => 222,
            "k" => 500,
            "l" => 222,
            "m" => 833,
            "n" => 556,
            "o" => 556,
            "p" => 556,
            "q" => 556,
            "r" => 333,
            "s" => 500,
            "t" => 278,
            "u" => 556,
            "v" => 500,
            "w" => 722,
            "x" => 500,
            "y" => 500,
            "z" => 500,
            "braceleft" => 334,
            "bar" => 260,
            "braceright" => 334,
            "asciitilde" => 584,
            _ => 556  // default
        };
    }

    /// <summary>
    /// Helvetica-Bold character widths by glyph name (for custom encodings)
    /// Source: Adobe Font Metrics (AFM) files - Helvetica-Bold.afm
    /// </summary>
    private static double GetHelveticaBoldWidthByName(string glyphName)
    {
        return glyphName switch
        {
            "space" => 278,
            "exclam" => 333,
            "quotedbl" => 474,
            "numbersign" => 556,
            "dollar" => 556,
            "percent" => 889,
            "ampersand" => 722,
            "quotesingle" or "quoteright" => 238,
            "parenleft" => 333,
            "parenright" => 333,
            "asterisk" => 389,
            "plus" => 584,
            "comma" => 278,
            "hyphen" or "minus" => 333,
            "period" => 278,
            "slash" => 278,
            "zero" => 556,
            "one" => 556,
            "two" => 556,
            "three" => 556,
            "four" => 556,
            "five" => 556,
            "six" => 556,
            "seven" => 556,
            "eight" => 556,
            "nine" => 556,
            "colon" => 333,
            "semicolon" => 333,
            "less" => 584,
            "equal" => 584,
            "greater" => 584,
            "question" => 611,
            "at" => 975,
            "A" => 722,
            "B" => 722,
            "C" => 722,
            "D" => 722,
            "E" => 667,
            "F" => 611,
            "G" => 778,
            "H" => 722,
            "I" => 278,
            "J" => 556,
            "K" => 722,
            "L" => 611,
            "M" => 833,
            "N" => 722,
            "O" => 778,
            "P" => 667,
            "Q" => 778,
            "R" => 722,
            "S" => 667,
            "T" => 611,
            "U" => 722,
            "V" => 667,
            "W" => 944,
            "X" => 667,
            "Y" => 667,
            "Z" => 611,
            "bracketleft" => 333,
            "backslash" => 278,
            "bracketright" => 333,
            "asciicircum" => 584,
            "underscore" => 556,
            "grave" or "quoteleft" => 333,
            "a" => 556,
            "b" => 611,
            "c" => 556,
            "d" => 611,
            "e" => 556,
            "f" => 333,
            "g" => 611,
            "h" => 611,
            "i" => 278,
            "j" => 278,
            "k" => 556,
            "l" => 278,
            "m" => 889,
            "n" => 611,
            "o" => 611,
            "p" => 611,
            "q" => 611,
            "r" => 389,
            "s" => 556,
            "t" => 333,
            "u" => 611,
            "v" => 556,
            "w" => 778,
            "x" => 556,
            "y" => 556,
            "z" => 500,
            "braceleft" => 389,
            "bar" => 280,
            "braceright" => 389,
            "asciitilde" => 584,
            _ => 556  // default
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

    /// <summary>
    /// Times-Roman character widths by glyph name (for custom encodings)
    /// Source: Adobe Font Metrics (AFM) files - Times-Roman.afm
    /// </summary>
    private static double GetTimesRomanWidthByName(string glyphName)
    {
        return glyphName switch
        {
            "space" => 250,
            "exclam" => 333,
            "quotedbl" => 408,
            "numbersign" => 500,
            "dollar" => 500,
            "percent" => 833,
            "ampersand" => 778,
            "quotesingle" or "quoteright" => 333,
            "parenleft" => 333,
            "parenright" => 333,
            "asterisk" => 500,
            "plus" => 564,
            "comma" => 250,
            "hyphen" or "minus" => 333,
            "period" => 250,
            "slash" => 278,
            "zero" => 500,
            "one" => 500,
            "two" => 500,
            "three" => 500,
            "four" => 500,
            "five" => 500,
            "six" => 500,
            "seven" => 500,
            "eight" => 500,
            "nine" => 500,
            "colon" => 278,
            "semicolon" => 278,
            "less" => 564,
            "equal" => 564,
            "greater" => 564,
            "question" => 444,
            "at" => 921,
            "A" => 722,
            "B" => 667,
            "C" => 667,
            "D" => 722,
            "E" => 611,
            "F" => 556,
            "G" => 722,
            "H" => 722,
            "I" => 333,
            "J" => 389,
            "K" => 722,
            "L" => 611,
            "M" => 889,
            "N" => 722,
            "O" => 722,
            "P" => 556,
            "Q" => 722,
            "R" => 667,
            "S" => 556,
            "T" => 611,
            "U" => 722,
            "V" => 722,
            "W" => 944,
            "X" => 722,
            "Y" => 722,
            "Z" => 611,
            "bracketleft" => 333,
            "backslash" => 278,
            "bracketright" => 333,
            "asciicircum" => 469,
            "underscore" => 500,
            "grave" or "quoteleft" => 333,
            "a" => 444,
            "b" => 500,
            "c" => 444,
            "d" => 500,
            "e" => 444,
            "f" => 333,
            "g" => 500,
            "h" => 500,
            "i" => 278,
            "j" => 278,
            "k" => 500,
            "l" => 278,
            "m" => 778,
            "n" => 500,
            "o" => 500,
            "p" => 500,
            "q" => 500,
            "r" => 333,
            "s" => 389,
            "t" => 278,
            "u" => 500,
            "v" => 500,
            "w" => 722,
            "x" => 500,
            "y" => 500,
            "z" => 444,
            "braceleft" => 480,
            "bar" => 200,
            "braceright" => 480,
            "asciitilde" => 541,
            _ => 500  // default
        };
    }

    /// <summary>
    /// Times-Bold character widths by glyph name (for custom encodings)
    /// Source: Adobe Font Metrics (AFM) files - Times-Bold.afm
    /// NOTE: Times-Bold has DIFFERENT widths than Times-Roman
    /// </summary>
    private static double GetTimesBoldWidthByName(string glyphName)
    {
        return glyphName switch
        {
            "space" => 250,
            "exclam" => 333,
            "quotedbl" => 555,
            "numbersign" => 500,
            "dollar" => 500,
            "percent" => 1000,
            "ampersand" => 833,
            "quotesingle" or "quoteright" => 333,
            "parenleft" => 333,
            "parenright" => 333,
            "asterisk" => 500,
            "plus" => 570,
            "comma" => 250,
            "hyphen" or "minus" => 333,
            "period" => 250,
            "slash" => 278,
            "zero" => 500,
            "one" => 500,
            "two" => 500,
            "three" => 500,
            "four" => 500,
            "five" => 500,
            "six" => 500,
            "seven" => 500,
            "eight" => 500,
            "nine" => 500,
            "colon" => 333,
            "semicolon" => 333,
            "less" => 570,
            "equal" => 570,
            "greater" => 570,
            "question" => 500,
            "at" => 930,
            "A" => 722,
            "B" => 667,
            "C" => 722,
            "D" => 722,
            "E" => 667,
            "F" => 611,
            "G" => 778,
            "H" => 778,
            "I" => 389,
            "J" => 500,
            "K" => 778,
            "L" => 667,
            "M" => 944,
            "N" => 722,
            "O" => 778,
            "P" => 611,
            "Q" => 778,
            "R" => 722,
            "S" => 556,
            "T" => 667,
            "U" => 722,
            "V" => 722,
            "W" => 1000,
            "X" => 722,
            "Y" => 722,
            "Z" => 667,
            "bracketleft" => 333,
            "backslash" => 278,
            "bracketright" => 333,
            "asciicircum" => 581,
            "underscore" => 500,
            "grave" or "quoteleft" => 333,
            "a" => 500,
            "b" => 556,
            "c" => 444,
            "d" => 556,
            "e" => 444,
            "f" => 333,
            "g" => 500,
            "h" => 556,
            "i" => 278,
            "j" => 333,
            "k" => 556,
            "l" => 278,
            "m" => 833,
            "n" => 556,
            "o" => 500,
            "p" => 556,
            "q" => 556,
            "r" => 444,
            "s" => 389,
            "t" => 333,
            "u" => 556,
            "v" => 500,
            "w" => 722,
            "x" => 500,
            "y" => 500,
            "z" => 444,
            "braceleft" => 394,
            "bar" => 220,
            "braceright" => 394,
            "asciitilde" => 520,
            _ => 500  // default
        };
    }

    /// <summary>
    /// Times-Italic character widths by glyph name (for custom encodings)
    /// Source: Adobe Font Metrics (AFM) files - Times-Italic.afm
    /// </summary>
    private static double GetTimesItalicWidthByName(string glyphName)
    {
        return glyphName switch
        {
            "space" => 250,
            "exclam" => 333,
            "quotedbl" => 420,
            "numbersign" => 500,
            "dollar" => 500,
            "percent" => 833,
            "ampersand" => 778,
            "quotesingle" or "quoteright" => 333,
            "parenleft" => 333,
            "parenright" => 333,
            "asterisk" => 500,
            "plus" => 675,
            "comma" => 250,
            "hyphen" or "minus" => 333,
            "period" => 250,
            "slash" => 278,
            "zero" => 500,
            "one" => 500,
            "two" => 500,
            "three" => 500,
            "four" => 500,
            "five" => 500,
            "six" => 500,
            "seven" => 500,
            "eight" => 500,
            "nine" => 500,
            "colon" => 333,
            "semicolon" => 333,
            "less" => 675,
            "equal" => 675,
            "greater" => 675,
            "question" => 500,
            "at" => 920,
            "A" => 611,
            "B" => 611,
            "C" => 667,
            "D" => 722,
            "E" => 611,
            "F" => 611,
            "G" => 722,
            "H" => 722,
            "I" => 333,
            "J" => 444,
            "K" => 667,
            "L" => 556,
            "M" => 833,
            "N" => 667,
            "O" => 722,
            "P" => 611,
            "Q" => 722,
            "R" => 611,
            "S" => 500,
            "T" => 556,
            "U" => 722,
            "V" => 611,
            "W" => 833,
            "X" => 611,
            "Y" => 556,
            "Z" => 556,
            "bracketleft" => 389,
            "backslash" => 278,
            "bracketright" => 389,
            "asciicircum" => 422,
            "underscore" => 500,
            "grave" or "quoteleft" => 333,
            "a" => 500,
            "b" => 500,
            "c" => 444,
            "d" => 500,
            "e" => 444,
            "f" => 278,
            "g" => 500,
            "h" => 500,
            "i" => 278,
            "j" => 278,
            "k" => 444,
            "l" => 278,
            "m" => 722,
            "n" => 500,
            "o" => 500,
            "p" => 500,
            "q" => 500,
            "r" => 389,
            "s" => 389,
            "t" => 278,
            "u" => 500,
            "v" => 444,
            "w" => 667,
            "x" => 444,
            "y" => 444,
            "z" => 389,
            "braceleft" => 400,
            "bar" => 275,
            "braceright" => 400,
            "asciitilde" => 541,
            _ => 500  // default
        };
    }

    /// <summary>
    /// Times-BoldItalic character widths by glyph name (for custom encodings)
    /// Source: Adobe Font Metrics (AFM) files - Times-BoldItalic.afm
    /// </summary>
    private static double GetTimesBoldItalicWidthByName(string glyphName)
    {
        return glyphName switch
        {
            "space" => 250,
            "exclam" => 389,
            "quotedbl" => 555,
            "numbersign" => 500,
            "dollar" => 500,
            "percent" => 833,
            "ampersand" => 778,
            "quotesingle" or "quoteright" => 333,
            "parenleft" => 333,
            "parenright" => 333,
            "asterisk" => 500,
            "plus" => 570,
            "comma" => 250,
            "hyphen" or "minus" => 333,
            "period" => 250,
            "slash" => 278,
            "zero" => 500,
            "one" => 500,
            "two" => 500,
            "three" => 500,
            "four" => 500,
            "five" => 500,
            "six" => 500,
            "seven" => 500,
            "eight" => 500,
            "nine" => 500,
            "colon" => 333,
            "semicolon" => 333,
            "less" => 570,
            "equal" => 570,
            "greater" => 570,
            "question" => 500,
            "at" => 832,
            "A" => 667,
            "B" => 667,
            "C" => 667,
            "D" => 722,
            "E" => 667,
            "F" => 667,
            "G" => 722,
            "H" => 778,
            "I" => 389,
            "J" => 500,
            "K" => 667,
            "L" => 611,
            "M" => 889,
            "N" => 722,
            "O" => 722,
            "P" => 611,
            "Q" => 722,
            "R" => 667,
            "S" => 556,
            "T" => 611,
            "U" => 722,
            "V" => 667,
            "W" => 889,
            "X" => 667,
            "Y" => 611,
            "Z" => 611,
            "bracketleft" => 333,
            "backslash" => 278,
            "bracketright" => 333,
            "asciicircum" => 570,
            "underscore" => 500,
            "grave" or "quoteleft" => 333,
            "a" => 500,
            "b" => 500,
            "c" => 444,
            "d" => 500,
            "e" => 444,
            "f" => 333,
            "g" => 500,
            "h" => 556,
            "i" => 278,
            "j" => 278,
            "k" => 500,
            "l" => 278,
            "m" => 778,
            "n" => 556,
            "o" => 500,
            "p" => 500,
            "q" => 500,
            "r" => 389,
            "s" => 389,
            "t" => 278,
            "u" => 556,
            "v" => 444,
            "w" => 667,
            "x" => 500,
            "y" => 444,
            "z" => 389,
            "braceleft" => 348,
            "bar" => 220,
            "braceright" => 348,
            "asciitilde" => 570,
            _ => 500  // default
        };
    }
}
