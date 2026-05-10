namespace Jbig2Decoder.Mq
{
    /// <summary>
    /// MQ probability-estimation state machine (ITU-T T.88 Table E.1).
    ///
    /// Indexed 0..46. Each row describes one Qe state:
    ///  - <see cref="Qe"/>      — LPS sub-interval size (15-bit fixed-point)
    ///  - <see cref="NMPS"/>    — next index after MPS renormalization
    ///  - <see cref="NLPS"/>    — next index after LPS renormalization
    ///  - <see cref="Switch"/>  — if true, MPS sense flips on LPS renormalization
    /// </summary>
    internal static class QeTable
    {
        public const int Length = 47;

        // Qe values are 15-bit fixed-point: divide by 0x8000 × (4/3) for decimal probability.
        public static readonly ushort[] Qe =
        {
            /*  0 */ 0x5601, /*  1 */ 0x3401, /*  2 */ 0x1801, /*  3 */ 0x0AC1,
            /*  4 */ 0x0521, /*  5 */ 0x0221, /*  6 */ 0x5601, /*  7 */ 0x5401,
            /*  8 */ 0x4801, /*  9 */ 0x3801, /* 10 */ 0x3001, /* 11 */ 0x2401,
            /* 12 */ 0x1C01, /* 13 */ 0x1601, /* 14 */ 0x5601, /* 15 */ 0x5401,
            /* 16 */ 0x5101, /* 17 */ 0x4801, /* 18 */ 0x3801, /* 19 */ 0x3401,
            /* 20 */ 0x3001, /* 21 */ 0x2801, /* 22 */ 0x2401, /* 23 */ 0x2201,
            /* 24 */ 0x1C01, /* 25 */ 0x1801, /* 26 */ 0x1601, /* 27 */ 0x1401,
            /* 28 */ 0x1201, /* 29 */ 0x1101, /* 30 */ 0x0AC1, /* 31 */ 0x09C1,
            /* 32 */ 0x08A1, /* 33 */ 0x0521, /* 34 */ 0x0441, /* 35 */ 0x02A1,
            /* 36 */ 0x0221, /* 37 */ 0x0141, /* 38 */ 0x0111, /* 39 */ 0x0085,
            /* 40 */ 0x0049, /* 41 */ 0x0025, /* 42 */ 0x0015, /* 43 */ 0x0009,
            /* 44 */ 0x0005, /* 45 */ 0x0001, /* 46 */ 0x5601,
        };

        public static readonly byte[] NMPS =
        {
            /*  0 */  1, /*  1 */  2, /*  2 */  3, /*  3 */  4,
            /*  4 */  5, /*  5 */ 38, /*  6 */  7, /*  7 */  8,
            /*  8 */  9, /*  9 */ 10, /* 10 */ 11, /* 11 */ 12,
            /* 12 */ 13, /* 13 */ 29, /* 14 */ 15, /* 15 */ 16,
            /* 16 */ 17, /* 17 */ 18, /* 18 */ 19, /* 19 */ 20,
            /* 20 */ 21, /* 21 */ 22, /* 22 */ 23, /* 23 */ 24,
            /* 24 */ 25, /* 25 */ 26, /* 26 */ 27, /* 27 */ 28,
            /* 28 */ 29, /* 29 */ 30, /* 30 */ 31, /* 31 */ 32,
            /* 32 */ 33, /* 33 */ 34, /* 34 */ 35, /* 35 */ 36,
            /* 36 */ 37, /* 37 */ 38, /* 38 */ 39, /* 39 */ 40,
            /* 40 */ 41, /* 41 */ 42, /* 42 */ 43, /* 43 */ 44,
            /* 44 */ 45, /* 45 */ 45, /* 46 */ 46,
        };

        public static readonly byte[] NLPS =
        {
            /*  0 */  1, /*  1 */  6, /*  2 */  9, /*  3 */ 12,
            /*  4 */ 29, /*  5 */ 33, /*  6 */  6, /*  7 */ 14,
            /*  8 */ 14, /*  9 */ 14, /* 10 */ 17, /* 11 */ 18,
            /* 12 */ 20, /* 13 */ 21, /* 14 */ 14, /* 15 */ 14,
            /* 16 */ 15, /* 17 */ 16, /* 18 */ 17, /* 19 */ 18,
            /* 20 */ 19, /* 21 */ 19, /* 22 */ 20, /* 23 */ 21,
            /* 24 */ 22, /* 25 */ 23, /* 26 */ 24, /* 27 */ 25,
            /* 28 */ 26, /* 29 */ 27, /* 30 */ 28, /* 31 */ 29,
            /* 32 */ 30, /* 33 */ 31, /* 34 */ 32, /* 35 */ 33,
            /* 36 */ 34, /* 37 */ 35, /* 38 */ 36, /* 39 */ 37,
            /* 40 */ 38, /* 41 */ 39, /* 42 */ 40, /* 43 */ 41,
            /* 44 */ 42, /* 45 */ 43, /* 46 */ 46,
        };

        public static readonly bool[] Switch =
        {
            /*  0 */ true,  /*  1 */ false, /*  2 */ false, /*  3 */ false,
            /*  4 */ false, /*  5 */ false, /*  6 */ true,  /*  7 */ false,
            /*  8 */ false, /*  9 */ false, /* 10 */ false, /* 11 */ false,
            /* 12 */ false, /* 13 */ false, /* 14 */ true,  /* 15 */ false,
            /* 16 */ false, /* 17 */ false, /* 18 */ false, /* 19 */ false,
            /* 20 */ false, /* 21 */ false, /* 22 */ false, /* 23 */ false,
            /* 24 */ false, /* 25 */ false, /* 26 */ false, /* 27 */ false,
            /* 28 */ false, /* 29 */ false, /* 30 */ false, /* 31 */ false,
            /* 32 */ false, /* 33 */ false, /* 34 */ false, /* 35 */ false,
            /* 36 */ false, /* 37 */ false, /* 38 */ false, /* 39 */ false,
            /* 40 */ false, /* 41 */ false, /* 42 */ false, /* 43 */ false,
            /* 44 */ false, /* 45 */ false, /* 46 */ false,
        };
    }
}
