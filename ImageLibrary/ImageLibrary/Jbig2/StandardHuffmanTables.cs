namespace ImageLibrary.Jbig2;

/// <summary>
/// Standard Huffman tables defined in T.88 Annex B.5.
/// Tables B.1 through B.15.
/// </summary>
internal static class StandardHuffmanTables
{
    // Lazily built tables
    private static HuffmanTable? _tableA;
    private static HuffmanTable? _tableB;
    private static HuffmanTable? _tableC;
    private static HuffmanTable? _tableD;
    private static HuffmanTable? _tableE;
    private static HuffmanTable? _tableF;
    private static HuffmanTable? _tableG;
    private static HuffmanTable? _tableH;
    private static HuffmanTable? _tableI;
    private static HuffmanTable? _tableJ;
    private static HuffmanTable? _tableK;
    private static HuffmanTable? _tableL;
    private static HuffmanTable? _tableM;
    private static HuffmanTable? _tableN;
    private static HuffmanTable? _tableO;

    /// <summary>Table B.1 - Bitmap size, run lengths (no OOB)</summary>
    public static HuffmanTable TableA => _tableA ??= HuffmanTable.Build(ParamsA);

    /// <summary>Table B.2 - Widths with OOB</summary>
    public static HuffmanTable TableB => _tableB ??= HuffmanTable.Build(ParamsB);

    /// <summary>Table B.3 - Widths, signed with OOB</summary>
    public static HuffmanTable TableC => _tableC ??= HuffmanTable.Build(ParamsC);

    /// <summary>Table B.4 - Heights (no OOB)</summary>
    public static HuffmanTable TableD => _tableD ??= HuffmanTable.Build(ParamsD);

    /// <summary>Table B.5 - Heights, signed (no OOB)</summary>
    public static HuffmanTable TableE => _tableE ??= HuffmanTable.Build(ParamsE);

    /// <summary>Table B.6 - Offsets, signed (no OOB)</summary>
    public static HuffmanTable TableF => _tableF ??= HuffmanTable.Build(ParamsF);

    /// <summary>Table B.7 - Offsets, signed (no OOB)</summary>
    public static HuffmanTable TableG => _tableG ??= HuffmanTable.Build(ParamsG);

    /// <summary>Table B.8 - Delta S with OOB</summary>
    public static HuffmanTable TableH => _tableH ??= HuffmanTable.Build(ParamsH);

    /// <summary>Table B.9 - Delta S with OOB</summary>
    public static HuffmanTable TableI => _tableI ??= HuffmanTable.Build(ParamsI);

    /// <summary>Table B.10 - Delta S with OOB</summary>
    public static HuffmanTable TableJ => _tableJ ??= HuffmanTable.Build(ParamsJ);

    /// <summary>Table B.11 - Strip delta T (no OOB)</summary>
    public static HuffmanTable TableK => _tableK ??= HuffmanTable.Build(ParamsK);

    /// <summary>Table B.12 - Strip delta T (no OOB)</summary>
    public static HuffmanTable TableL => _tableL ??= HuffmanTable.Build(ParamsL);

    /// <summary>Table B.13 - Strip delta T (no OOB)</summary>
    public static HuffmanTable TableM => _tableM ??= HuffmanTable.Build(ParamsM);

    /// <summary>Table B.14 - Small signed values (no OOB)</summary>
    public static HuffmanTable TableN => _tableN ??= HuffmanTable.Build(ParamsN);

    /// <summary>Table B.15 - Refinement delta (no OOB)</summary>
    public static HuffmanTable TableO => _tableO ??= HuffmanTable.Build(ParamsO);

    // Table B.1
    private static readonly HuffmanParams ParamsA = new(false, [
        new(1, 4, 0),
        new(2, 8, 16),
        new(3, 16, 272),
        new(0, 32, -1),       // low
        new(3, 32, 65808)     // high
    ]);

    // Table B.2
    private static readonly HuffmanParams ParamsB = new(true, [
        new(1, 0, 0),
        new(2, 0, 1),
        new(3, 0, 2),
        new(4, 3, 3),
        new(5, 6, 11),
        new(0, 32, -1),       // low
        new(6, 32, 75),       // high
        new(6, 0, 0)          // OOB
    ]);

    // Table B.3
    private static readonly HuffmanParams ParamsC = new(true, [
        new(8, 8, -256),
        new(1, 0, 0),
        new(2, 0, 1),
        new(3, 0, 2),
        new(4, 3, 3),
        new(5, 6, 11),
        new(8, 32, -257),     // low
        new(7, 32, 75),       // high
        new(6, 0, 0)          // OOB
    ]);

    // Table B.4
    private static readonly HuffmanParams ParamsD = new(false, [
        new(1, 0, 1),
        new(2, 0, 2),
        new(3, 0, 3),
        new(4, 3, 4),
        new(5, 6, 12),
        new(0, 32, -1),       // low
        new(5, 32, 76)        // high
    ]);

    // Table B.5
    private static readonly HuffmanParams ParamsE = new(false, [
        new(7, 8, -255),
        new(1, 0, 1),
        new(2, 0, 2),
        new(3, 0, 3),
        new(4, 3, 4),
        new(5, 6, 12),
        new(7, 32, -256),     // low
        new(6, 32, 76)        // high
    ]);

    // Table B.6
    private static readonly HuffmanParams ParamsF = new(false, [
        new(5, 10, -2048),
        new(4, 9, -1024),
        new(4, 8, -512),
        new(4, 7, -256),
        new(5, 6, -128),
        new(5, 5, -64),
        new(4, 5, -32),
        new(2, 7, 0),
        new(3, 7, 128),
        new(3, 8, 256),
        new(4, 9, 512),
        new(4, 10, 1024),
        new(6, 32, -2049),    // low
        new(6, 32, 2048)      // high
    ]);

    // Table B.7
    private static readonly HuffmanParams ParamsG = new(false, [
        new(4, 9, -1024),
        new(3, 8, -512),
        new(4, 7, -256),
        new(5, 6, -128),
        new(5, 5, -64),
        new(4, 5, -32),
        new(4, 5, 0),
        new(5, 5, 32),
        new(5, 6, 64),
        new(4, 7, 128),
        new(3, 8, 256),
        new(3, 9, 512),
        new(3, 10, 1024),
        new(5, 32, -1025),    // low
        new(5, 32, 2048)      // high
    ]);

    // Table B.8
    private static readonly HuffmanParams ParamsH = new(true, [
        new(8, 3, -15),
        new(9, 1, -7),
        new(8, 1, -5),
        new(9, 0, -3),
        new(7, 0, -2),
        new(4, 0, -1),
        new(2, 1, 0),
        new(5, 0, 2),
        new(6, 0, 3),
        new(3, 4, 4),
        new(6, 1, 20),
        new(4, 4, 22),
        new(4, 5, 38),
        new(5, 6, 70),
        new(5, 7, 134),
        new(6, 7, 262),
        new(7, 8, 390),
        new(6, 10, 646),
        new(9, 32, -16),      // low
        new(9, 32, 1670),     // high
        new(2, 0, 0)          // OOB
    ]);

    // Table B.9
    private static readonly HuffmanParams ParamsI = new(true, [
        new(8, 4, -31),
        new(9, 2, -15),
        new(8, 2, -11),
        new(9, 1, -7),
        new(7, 1, -5),
        new(4, 1, -3),
        new(3, 1, -1),
        new(3, 1, 1),
        new(5, 1, 3),
        new(6, 1, 5),
        new(3, 5, 7),
        new(6, 2, 39),
        new(4, 5, 43),
        new(4, 6, 75),
        new(5, 7, 139),
        new(5, 8, 267),
        new(6, 8, 523),
        new(7, 9, 779),
        new(6, 11, 1291),
        new(9, 32, -32),      // low
        new(9, 32, 3339),     // high
        new(2, 0, 0)          // OOB
    ]);

    // Table B.10
    private static readonly HuffmanParams ParamsJ = new(true, [
        new(7, 4, -21),
        new(8, 0, -5),
        new(7, 0, -4),
        new(5, 0, -3),
        new(2, 2, -2),
        new(5, 0, 2),
        new(6, 0, 3),
        new(7, 0, 4),
        new(8, 0, 5),
        new(2, 6, 6),
        new(5, 5, 70),
        new(6, 5, 102),
        new(6, 6, 134),
        new(6, 7, 198),
        new(6, 8, 326),
        new(6, 9, 582),
        new(6, 10, 1094),
        new(7, 11, 2118),
        new(8, 32, -22),      // low
        new(8, 32, 4166),     // high
        new(2, 0, 0)          // OOB
    ]);

    // Table B.11
    private static readonly HuffmanParams ParamsK = new(false, [
        new(1, 0, 1),
        new(2, 1, 2),
        new(4, 0, 4),
        new(4, 1, 5),
        new(5, 1, 7),
        new(5, 2, 9),
        new(6, 2, 13),
        new(7, 2, 17),
        new(7, 3, 21),
        new(7, 4, 29),
        new(7, 5, 45),
        new(7, 6, 77),
        new(0, 32, -1),       // low
        new(7, 32, 141)       // high
    ]);

    // Table B.12
    private static readonly HuffmanParams ParamsL = new(false, [
        new(1, 0, 1),
        new(2, 0, 2),
        new(3, 1, 3),
        new(5, 0, 5),
        new(5, 1, 6),
        new(6, 1, 8),
        new(7, 0, 10),
        new(7, 1, 11),
        new(7, 2, 13),
        new(7, 3, 17),
        new(7, 4, 25),
        new(8, 5, 41),
        new(8, 32, 73),
        new(0, 32, -1),       // low
        new(0, 32, 0)         // high (special - uses PREFLEN=0)
    ]);

    // Table B.13
    private static readonly HuffmanParams ParamsM = new(false, [
        new(1, 0, 1),
        new(3, 0, 2),
        new(4, 0, 3),
        new(5, 0, 4),
        new(4, 1, 5),
        new(3, 3, 7),
        new(6, 1, 15),
        new(6, 2, 17),
        new(6, 3, 21),
        new(6, 4, 29),
        new(6, 5, 45),
        new(7, 6, 77),
        new(0, 32, -1),       // low
        new(7, 32, 141)       // high
    ]);

    // Table B.14
    private static readonly HuffmanParams ParamsN = new(false, [
        new(3, 0, -2),
        new(3, 0, -1),
        new(1, 0, 0),
        new(3, 0, 1),
        new(3, 0, 2),
        new(0, 32, -1),       // low (special - PREFLEN=0 means this is handled separately)
        new(0, 32, 3)         // high
    ]);

    // Table B.15
    private static readonly HuffmanParams ParamsO = new(false, [
        new(7, 4, -24),
        new(6, 2, -8),
        new(5, 1, -4),
        new(4, 0, -2),
        new(3, 0, -1),
        new(1, 0, 0),
        new(3, 0, 1),
        new(4, 0, 2),
        new(5, 1, 3),
        new(6, 2, 5),
        new(7, 4, 9),
        new(7, 32, -25),      // low
        new(7, 32, 25)        // high
    ]);
}
