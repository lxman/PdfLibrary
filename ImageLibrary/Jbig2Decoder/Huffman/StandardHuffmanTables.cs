namespace Jbig2Decoder.Huffman
{
    /// <summary>
    /// Standard Huffman tables defined in T.88 Annex B (Tables B.1 through B.15).
    /// Identifiers match jbig2dec for cross-reference: <see cref="A"/> = Table B.1, etc.
    /// </summary>
    internal static class StandardHuffmanTables
    {
        // Table B.1
        public static readonly HuffmanParams A = new HuffmanParams
        {
            HtOob = false,
            Lines = new[]
            {
                new HuffmanLine(1,  4, 0),
                new HuffmanLine(2,  8, 16),
                new HuffmanLine(3, 16, 272),
                new HuffmanLine(0, 32, -1),       // low
                new HuffmanLine(3, 32, 65808),    // high
            },
        };

        // Table B.2
        public static readonly HuffmanParams B = new HuffmanParams
        {
            HtOob = true,
            Lines = new[]
            {
                new HuffmanLine(1, 0, 0),
                new HuffmanLine(2, 0, 1),
                new HuffmanLine(3, 0, 2),
                new HuffmanLine(4, 3, 3),
                new HuffmanLine(5, 6, 11),
                new HuffmanLine(0, 32, -1),       // low
                new HuffmanLine(6, 32, 75),       // high
                new HuffmanLine(6, 0, 0),         // OOB
            },
        };

        // Table B.3
        public static readonly HuffmanParams C = new HuffmanParams
        {
            HtOob = true,
            Lines = new[]
            {
                new HuffmanLine(8, 8, -256),
                new HuffmanLine(1, 0, 0),
                new HuffmanLine(2, 0, 1),
                new HuffmanLine(3, 0, 2),
                new HuffmanLine(4, 3, 3),
                new HuffmanLine(5, 6, 11),
                new HuffmanLine(8, 32, -257),     // low
                new HuffmanLine(7, 32, 75),       // high
                new HuffmanLine(6, 0, 0),         // OOB
            },
        };

        // Table B.4
        public static readonly HuffmanParams D = new HuffmanParams
        {
            HtOob = false,
            Lines = new[]
            {
                new HuffmanLine(1, 0, 1),
                new HuffmanLine(2, 0, 2),
                new HuffmanLine(3, 0, 3),
                new HuffmanLine(4, 3, 4),
                new HuffmanLine(5, 6, 12),
                new HuffmanLine(0, 32, -1),       // low
                new HuffmanLine(5, 32, 76),       // high
            },
        };

        // Table B.5
        public static readonly HuffmanParams E = new HuffmanParams
        {
            HtOob = false,
            Lines = new[]
            {
                new HuffmanLine(7, 8, -255),
                new HuffmanLine(1, 0, 1),
                new HuffmanLine(2, 0, 2),
                new HuffmanLine(3, 0, 3),
                new HuffmanLine(4, 3, 4),
                new HuffmanLine(5, 6, 12),
                new HuffmanLine(7, 32, -256),     // low
                new HuffmanLine(6, 32, 76),       // high
            },
        };

        // Table B.6
        public static readonly HuffmanParams F = new HuffmanParams
        {
            HtOob = false,
            Lines = new[]
            {
                new HuffmanLine(5, 10, -2048),
                new HuffmanLine(4,  9, -1024),
                new HuffmanLine(4,  8, -512),
                new HuffmanLine(4,  7, -256),
                new HuffmanLine(5,  6, -128),
                new HuffmanLine(5,  5, -64),
                new HuffmanLine(4,  5, -32),
                new HuffmanLine(2,  7, 0),
                new HuffmanLine(3,  7, 128),
                new HuffmanLine(3,  8, 256),
                new HuffmanLine(4,  9, 512),
                new HuffmanLine(4, 10, 1024),
                new HuffmanLine(6, 32, -2049),    // low
                new HuffmanLine(6, 32, 2048),     // high
            },
        };

        // Table B.7
        public static readonly HuffmanParams G = new HuffmanParams
        {
            HtOob = false,
            Lines = new[]
            {
                new HuffmanLine(4,  9, -1024),
                new HuffmanLine(3,  8, -512),
                new HuffmanLine(4,  7, -256),
                new HuffmanLine(5,  6, -128),
                new HuffmanLine(5,  5, -64),
                new HuffmanLine(4,  5, -32),
                new HuffmanLine(4,  5, 0),
                new HuffmanLine(5,  5, 32),
                new HuffmanLine(5,  6, 64),
                new HuffmanLine(4,  7, 128),
                new HuffmanLine(3,  8, 256),
                new HuffmanLine(3,  9, 512),
                new HuffmanLine(3, 10, 1024),
                new HuffmanLine(5, 32, -1025),    // low
                new HuffmanLine(5, 32, 2048),     // high
            },
        };

        // Table B.8
        public static readonly HuffmanParams H = new HuffmanParams
        {
            HtOob = true,
            Lines = new[]
            {
                new HuffmanLine(8,  3, -15),
                new HuffmanLine(9,  1, -7),
                new HuffmanLine(8,  1, -5),
                new HuffmanLine(9,  0, -3),
                new HuffmanLine(7,  0, -2),
                new HuffmanLine(4,  0, -1),
                new HuffmanLine(2,  1, 0),
                new HuffmanLine(5,  0, 2),
                new HuffmanLine(6,  0, 3),
                new HuffmanLine(3,  4, 4),
                new HuffmanLine(6,  1, 20),
                new HuffmanLine(4,  4, 22),
                new HuffmanLine(4,  5, 38),
                new HuffmanLine(5,  6, 70),
                new HuffmanLine(5,  7, 134),
                new HuffmanLine(6,  7, 262),
                new HuffmanLine(7,  8, 390),
                new HuffmanLine(6, 10, 646),
                new HuffmanLine(9, 32, -16),      // low
                new HuffmanLine(9, 32, 1670),     // high
                new HuffmanLine(2,  0, 0),        // OOB
            },
        };

        // Table B.9
        public static readonly HuffmanParams I = new HuffmanParams
        {
            HtOob = true,
            Lines = new[]
            {
                new HuffmanLine(8,  4, -31),
                new HuffmanLine(9,  2, -15),
                new HuffmanLine(8,  2, -11),
                new HuffmanLine(9,  1, -7),
                new HuffmanLine(7,  1, -5),
                new HuffmanLine(4,  1, -3),
                new HuffmanLine(3,  1, -1),
                new HuffmanLine(3,  1, 1),
                new HuffmanLine(5,  1, 3),
                new HuffmanLine(6,  1, 5),
                new HuffmanLine(3,  5, 7),
                new HuffmanLine(6,  2, 39),
                new HuffmanLine(4,  5, 43),
                new HuffmanLine(4,  6, 75),
                new HuffmanLine(5,  7, 139),
                new HuffmanLine(5,  8, 267),
                new HuffmanLine(6,  8, 523),
                new HuffmanLine(7,  9, 779),
                new HuffmanLine(6, 11, 1291),
                new HuffmanLine(9, 32, -32),      // low
                new HuffmanLine(9, 32, 3339),     // high
                new HuffmanLine(2,  0, 0),        // OOB
            },
        };

        // Table B.10
        public static readonly HuffmanParams J = new HuffmanParams
        {
            HtOob = true,
            Lines = new[]
            {
                new HuffmanLine(7,  4, -21),
                new HuffmanLine(8,  0, -5),
                new HuffmanLine(7,  0, -4),
                new HuffmanLine(5,  0, -3),
                new HuffmanLine(2,  2, -2),
                new HuffmanLine(5,  0, 2),
                new HuffmanLine(6,  0, 3),
                new HuffmanLine(7,  0, 4),
                new HuffmanLine(8,  0, 5),
                new HuffmanLine(2,  6, 6),
                new HuffmanLine(5,  5, 70),
                new HuffmanLine(6,  5, 102),
                new HuffmanLine(6,  6, 134),
                new HuffmanLine(6,  7, 198),
                new HuffmanLine(6,  8, 326),
                new HuffmanLine(6,  9, 582),
                new HuffmanLine(6, 10, 1094),
                new HuffmanLine(7, 11, 2118),
                new HuffmanLine(8, 32, -22),      // low
                new HuffmanLine(8, 32, 4166),     // high
                new HuffmanLine(2,  0, 0),        // OOB
            },
        };

        // Table B.11
        public static readonly HuffmanParams K = new HuffmanParams
        {
            HtOob = false,
            Lines = new[]
            {
                new HuffmanLine(1, 0, 1),
                new HuffmanLine(2, 1, 2),
                new HuffmanLine(4, 0, 4),
                new HuffmanLine(4, 1, 5),
                new HuffmanLine(5, 1, 7),
                new HuffmanLine(5, 2, 9),
                new HuffmanLine(6, 2, 13),
                new HuffmanLine(7, 2, 17),
                new HuffmanLine(7, 3, 21),
                new HuffmanLine(7, 4, 29),
                new HuffmanLine(7, 5, 45),
                new HuffmanLine(7, 6, 77),
                new HuffmanLine(0, 32, -1),       // low
                new HuffmanLine(7, 32, 141),      // high
            },
        };

        // Table B.12
        public static readonly HuffmanParams L = new HuffmanParams
        {
            HtOob = false,
            Lines = new[]
            {
                new HuffmanLine(1, 0, 1),
                new HuffmanLine(2, 0, 2),
                new HuffmanLine(3, 1, 3),
                new HuffmanLine(5, 0, 5),
                new HuffmanLine(5, 1, 6),
                new HuffmanLine(6, 1, 8),
                new HuffmanLine(7, 0, 10),
                new HuffmanLine(7, 1, 11),
                new HuffmanLine(7, 2, 13),
                new HuffmanLine(7, 3, 17),
                new HuffmanLine(7, 4, 25),
                new HuffmanLine(8, 5, 41),
                new HuffmanLine(8, 32, 73),
                new HuffmanLine(0, 32, -1),       // low
                new HuffmanLine(0, 32, 0),        // high
            },
        };

        // Table B.13
        public static readonly HuffmanParams M = new HuffmanParams
        {
            HtOob = false,
            Lines = new[]
            {
                new HuffmanLine(1, 0, 1),
                new HuffmanLine(3, 0, 2),
                new HuffmanLine(4, 0, 3),
                new HuffmanLine(5, 0, 4),
                new HuffmanLine(4, 1, 5),
                new HuffmanLine(3, 3, 7),
                new HuffmanLine(6, 1, 15),
                new HuffmanLine(6, 2, 17),
                new HuffmanLine(6, 3, 21),
                new HuffmanLine(6, 4, 29),
                new HuffmanLine(6, 5, 45),
                new HuffmanLine(7, 6, 77),
                new HuffmanLine(0, 32, -1),       // low
                new HuffmanLine(7, 32, 141),      // high
            },
        };

        // Table B.14
        public static readonly HuffmanParams N = new HuffmanParams
        {
            HtOob = false,
            Lines = new[]
            {
                new HuffmanLine(3, 0, -2),
                new HuffmanLine(3, 0, -1),
                new HuffmanLine(1, 0, 0),
                new HuffmanLine(3, 0, 1),
                new HuffmanLine(3, 0, 2),
                new HuffmanLine(0, 32, -1),       // low
                new HuffmanLine(0, 32, 3),        // high
            },
        };

        // Table B.15
        public static readonly HuffmanParams O = new HuffmanParams
        {
            HtOob = false,
            Lines = new[]
            {
                new HuffmanLine(7, 4, -24),
                new HuffmanLine(6, 2, -8),
                new HuffmanLine(5, 1, -4),
                new HuffmanLine(4, 0, -2),
                new HuffmanLine(3, 0, -1),
                new HuffmanLine(1, 0, 0),
                new HuffmanLine(3, 0, 1),
                new HuffmanLine(4, 0, 2),
                new HuffmanLine(5, 1, 3),
                new HuffmanLine(6, 2, 5),
                new HuffmanLine(7, 4, 9),
                new HuffmanLine(7, 32, -25),      // low
                new HuffmanLine(7, 32, 25),       // high
            },
        };
    }
}
