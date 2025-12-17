namespace ImageLibrary.Ccitt;

/// <summary>
/// CCITT Huffman code tables for white and black run lengths.
/// Based on ITU-T T.4 and T.6 specifications.
/// </summary>
public static class HuffmanTables
{
    /// <summary>
    /// Represents a Huffman code entry with its bit pattern and length.
    /// </summary>
    public readonly struct HuffmanCode
    {
        public readonly ushort Code;
        public readonly byte BitLength;

        public HuffmanCode(ushort code, byte bitLength)
        {
            Code = code;
            BitLength = bitLength;
        }
    }

    // White terminating codes (run lengths 0-63)
    // Format: code bits read MSB first
    public static readonly HuffmanCode[] WhiteTerminatingCodes =
    [
        new HuffmanCode(0b00110101, 8),   // 0
        new HuffmanCode(0b000111, 6),     // 1
        new HuffmanCode(0b0111, 4),       // 2
        new HuffmanCode(0b1000, 4),       // 3
        new HuffmanCode(0b1011, 4),       // 4
        new HuffmanCode(0b1100, 4),       // 5
        new HuffmanCode(0b1110, 4),       // 6
        new HuffmanCode(0b1111, 4),       // 7
        new HuffmanCode(0b10011, 5),      // 8
        new HuffmanCode(0b10100, 5),      // 9
        new HuffmanCode(0b00111, 5),      // 10
        new HuffmanCode(0b01000, 5),      // 11
        new HuffmanCode(0b001000, 6),     // 12
        new HuffmanCode(0b000011, 6),     // 13
        new HuffmanCode(0b110100, 6),     // 14
        new HuffmanCode(0b110101, 6),     // 15
        new HuffmanCode(0b101010, 6),     // 16
        new HuffmanCode(0b101011, 6),     // 17
        new HuffmanCode(0b0100111, 7),    // 18
        new HuffmanCode(0b0001100, 7),    // 19
        new HuffmanCode(0b0001000, 7),    // 20
        new HuffmanCode(0b0010111, 7),    // 21
        new HuffmanCode(0b0000011, 7),    // 22
        new HuffmanCode(0b0000100, 7),    // 23
        new HuffmanCode(0b0101000, 7),    // 24
        new HuffmanCode(0b0101011, 7),    // 25
        new HuffmanCode(0b0010011, 7),    // 26
        new HuffmanCode(0b0100100, 7),    // 27
        new HuffmanCode(0b0011000, 7),    // 28
        new HuffmanCode(0b00000010, 8),   // 29
        new HuffmanCode(0b00000011, 8),   // 30
        new HuffmanCode(0b00011010, 8),   // 31
        new HuffmanCode(0b00011011, 8),   // 32
        new HuffmanCode(0b00010010, 8),   // 33
        new HuffmanCode(0b00010011, 8),   // 34
        new HuffmanCode(0b00010100, 8),   // 35
        new HuffmanCode(0b00010101, 8),   // 36
        new HuffmanCode(0b00010110, 8),   // 37
        new HuffmanCode(0b00010111, 8),   // 38
        new HuffmanCode(0b00101000, 8),   // 39
        new HuffmanCode(0b00101001, 8),   // 40
        new HuffmanCode(0b00101010, 8),   // 41
        new HuffmanCode(0b00101011, 8),   // 42
        new HuffmanCode(0b00101100, 8),   // 43
        new HuffmanCode(0b00101101, 8),   // 44
        new HuffmanCode(0b00000100, 8),   // 45
        new HuffmanCode(0b00000101, 8),   // 46
        new HuffmanCode(0b00001010, 8),   // 47
        new HuffmanCode(0b00001011, 8),   // 48
        new HuffmanCode(0b01010010, 8),   // 49
        new HuffmanCode(0b01010011, 8),   // 50
        new HuffmanCode(0b01010100, 8),   // 51
        new HuffmanCode(0b01010101, 8),   // 52
        new HuffmanCode(0b00100100, 8),   // 53
        new HuffmanCode(0b00100101, 8),   // 54
        new HuffmanCode(0b01011000, 8),   // 55
        new HuffmanCode(0b01011001, 8),   // 56
        new HuffmanCode(0b01011010, 8),   // 57
        new HuffmanCode(0b01011011, 8),   // 58
        new HuffmanCode(0b01001010, 8),   // 59
        new HuffmanCode(0b01001011, 8),   // 60
        new HuffmanCode(0b00110010, 8),   // 61
        new HuffmanCode(0b00110011, 8),   // 62
        new HuffmanCode(0b00110100, 8) // 63
    ];

    // Black terminating codes (run lengths 0-63)
    public static readonly HuffmanCode[] BlackTerminatingCodes =
    [
        new HuffmanCode(0b0000110111, 10),  // 0
        new HuffmanCode(0b010, 3),          // 1
        new HuffmanCode(0b11, 2),           // 2
        new HuffmanCode(0b10, 2),           // 3
        new HuffmanCode(0b011, 3),          // 4
        new HuffmanCode(0b0011, 4),         // 5
        new HuffmanCode(0b0010, 4),         // 6
        new HuffmanCode(0b00011, 5),        // 7
        new HuffmanCode(0b000101, 6),       // 8
        new HuffmanCode(0b000100, 6),       // 9
        new HuffmanCode(0b0000100, 7),      // 10
        new HuffmanCode(0b0000101, 7),      // 11
        new HuffmanCode(0b0000111, 7),      // 12
        new HuffmanCode(0b00000100, 8),     // 13
        new HuffmanCode(0b00000111, 8),     // 14
        new HuffmanCode(0b000011000, 9),    // 15
        new HuffmanCode(0b0000010111, 10),  // 16
        new HuffmanCode(0b0000011000, 10),  // 17
        new HuffmanCode(0b0000001000, 10),  // 18
        new HuffmanCode(0b00001100111, 11), // 19
        new HuffmanCode(0b00001101000, 11), // 20
        new HuffmanCode(0b00001101100, 11), // 21
        new HuffmanCode(0b00000110111, 11), // 22
        new HuffmanCode(0b00000101000, 11), // 23
        new HuffmanCode(0b00000010111, 11), // 24
        new HuffmanCode(0b00000011000, 11), // 25
        new HuffmanCode(0b000011001010, 12), // 26
        new HuffmanCode(0b000011001011, 12), // 27
        new HuffmanCode(0b000011001100, 12), // 28
        new HuffmanCode(0b000011001101, 12), // 29
        new HuffmanCode(0b000001101000, 12), // 30
        new HuffmanCode(0b000001101001, 12), // 31
        new HuffmanCode(0b000001101010, 12), // 32
        new HuffmanCode(0b000001101011, 12), // 33
        new HuffmanCode(0b000011010010, 12), // 34
        new HuffmanCode(0b000011010011, 12), // 35
        new HuffmanCode(0b000011010100, 12), // 36
        new HuffmanCode(0b000011010101, 12), // 37
        new HuffmanCode(0b000011010110, 12), // 38
        new HuffmanCode(0b000011010111, 12), // 39
        new HuffmanCode(0b000001101100, 12), // 40
        new HuffmanCode(0b000001101101, 12), // 41
        new HuffmanCode(0b000011011010, 12), // 42
        new HuffmanCode(0b000011011011, 12), // 43
        new HuffmanCode(0b000001010100, 12), // 44
        new HuffmanCode(0b000001010101, 12), // 45
        new HuffmanCode(0b000001010110, 12), // 46
        new HuffmanCode(0b000001010111, 12), // 47
        new HuffmanCode(0b000001100100, 12), // 48
        new HuffmanCode(0b000001100101, 12), // 49
        new HuffmanCode(0b000001010010, 12), // 50
        new HuffmanCode(0b000001010011, 12), // 51
        new HuffmanCode(0b000000100100, 12), // 52
        new HuffmanCode(0b000000110111, 12), // 53
        new HuffmanCode(0b000000111000, 12), // 54
        new HuffmanCode(0b000000100111, 12), // 55
        new HuffmanCode(0b000000101000, 12), // 56
        new HuffmanCode(0b000001011000, 12), // 57
        new HuffmanCode(0b000001011001, 12), // 58
        new HuffmanCode(0b000000101011, 12), // 59
        new HuffmanCode(0b000000101100, 12), // 60
        new HuffmanCode(0b000001011010, 12), // 61
        new HuffmanCode(0b000001100110, 12), // 62
        new HuffmanCode(0b000001100111, 12) // 63
    ];

    // White makeup codes (run lengths 64, 128, 192, ... 1728)
    // Index 0 = 64, index 1 = 128, etc.
    public static readonly HuffmanCode[] WhiteMakeupCodes =
    [
        new HuffmanCode(0b11011, 5),       // 64
        new HuffmanCode(0b10010, 5),       // 128
        new HuffmanCode(0b010111, 6),      // 192
        new HuffmanCode(0b0110111, 7),     // 256
        new HuffmanCode(0b00110110, 8),    // 320
        new HuffmanCode(0b00110111, 8),    // 384
        new HuffmanCode(0b01100100, 8),    // 448
        new HuffmanCode(0b01100101, 8),    // 512
        new HuffmanCode(0b01101000, 8),    // 576
        new HuffmanCode(0b01100111, 8),    // 640
        new HuffmanCode(0b011001100, 9),   // 704
        new HuffmanCode(0b011001101, 9),   // 768
        new HuffmanCode(0b011010010, 9),   // 832
        new HuffmanCode(0b011010011, 9),   // 896
        new HuffmanCode(0b011010100, 9),   // 960
        new HuffmanCode(0b011010101, 9),   // 1024
        new HuffmanCode(0b011010110, 9),   // 1088
        new HuffmanCode(0b011010111, 9),   // 1152
        new HuffmanCode(0b011011000, 9),   // 1216
        new HuffmanCode(0b011011001, 9),   // 1280
        new HuffmanCode(0b011011010, 9),   // 1344
        new HuffmanCode(0b011011011, 9),   // 1408
        new HuffmanCode(0b010011000, 9),   // 1472
        new HuffmanCode(0b010011001, 9),   // 1536
        new HuffmanCode(0b010011010, 9),   // 1600
        new HuffmanCode(0b011000, 6),      // 1664
        new HuffmanCode(0b010011011, 9) // 1728
    ];

    // Black makeup codes (run lengths 64, 128, 192, ... 1728)
    public static readonly HuffmanCode[] BlackMakeupCodes =
    [
        new HuffmanCode(0b0000001111, 10),    // 64
        new HuffmanCode(0b000011001000, 12),  // 128
        new HuffmanCode(0b000011001001, 12),  // 192
        new HuffmanCode(0b000001011011, 12),  // 256
        new HuffmanCode(0b000000110011, 12),  // 320
        new HuffmanCode(0b000000110100, 12),  // 384
        new HuffmanCode(0b000000110101, 12),  // 448
        new HuffmanCode(0b0000001101100, 13), // 512
        new HuffmanCode(0b0000001101101, 13), // 576
        new HuffmanCode(0b0000001001010, 13), // 640
        new HuffmanCode(0b0000001001011, 13), // 704
        new HuffmanCode(0b0000001001100, 13), // 768
        new HuffmanCode(0b0000001001101, 13), // 832
        new HuffmanCode(0b0000001110010, 13), // 896
        new HuffmanCode(0b0000001110011, 13), // 960
        new HuffmanCode(0b0000001110100, 13), // 1024
        new HuffmanCode(0b0000001110101, 13), // 1088
        new HuffmanCode(0b0000001110110, 13), // 1152
        new HuffmanCode(0b0000001110111, 13), // 1216
        new HuffmanCode(0b0000001010010, 13), // 1280
        new HuffmanCode(0b0000001010011, 13), // 1344
        new HuffmanCode(0b0000001010100, 13), // 1408
        new HuffmanCode(0b0000001010101, 13), // 1472
        new HuffmanCode(0b0000001011010, 13), // 1536
        new HuffmanCode(0b0000001011011, 13), // 1600
        new HuffmanCode(0b0000001100100, 13), // 1664
        new HuffmanCode(0b0000001100101, 13) // 1728
    ];

    // Extended makeup codes (run lengths 1792-2560, shared for both white and black)
    // Index 0 = 1792, index 1 = 1856, etc.
    public static readonly HuffmanCode[] ExtendedMakeupCodes =
    [
        new HuffmanCode(0b00000001000, 11),   // 1792
        new HuffmanCode(0b00000001100, 11),   // 1856
        new HuffmanCode(0b00000001101, 11),   // 1920
        new HuffmanCode(0b000000010010, 12),  // 1984
        new HuffmanCode(0b000000010011, 12),  // 2048
        new HuffmanCode(0b000000010100, 12),  // 2112
        new HuffmanCode(0b000000010101, 12),  // 2176
        new HuffmanCode(0b000000010110, 12),  // 2240
        new HuffmanCode(0b000000010111, 12),  // 2304
        new HuffmanCode(0b000000011100, 12),  // 2368
        new HuffmanCode(0b000000011101, 12),  // 2432
        new HuffmanCode(0b000000011110, 12),  // 2496
        new HuffmanCode(0b000000011111, 12) // 2560
    ];

    /// <summary>
    /// Gets the Huffman code for a given run length.
    /// </summary>
    /// <param name="runLength">The run length to encode.</param>
    /// <param name="isWhite">True for white runs, false for black runs.</param>
    /// <param name="makeupCode">Output: the makeup code (if needed), or default if not.</param>
    /// <param name="terminatingCode">Output: the terminating code.</param>
    public static void GetRunLengthCodes(int runLength, bool isWhite,
        out HuffmanCode makeupCode, out HuffmanCode terminatingCode)
    {
        makeupCode = default;

        if (runLength <= CcittConstants.MaxTerminatingRunLength)
        {
            // Simple case: just a terminating code
            terminatingCode = isWhite
                ? WhiteTerminatingCodes[runLength]
                : BlackTerminatingCodes[runLength];
            return;
        }

        // Need makeup code(s) + terminating code
        int remaining = runLength;

        // Handle extended makeup codes (1792-2560)
        if (remaining >= 1792)
        {
            int extendedIndex = (remaining - 1792) / 64;
            if (extendedIndex < ExtendedMakeupCodes.Length)
            {
                makeupCode = ExtendedMakeupCodes[extendedIndex];
                remaining -= 1792 + (extendedIndex * 64);
            }
        }

        // Handle standard makeup codes (64-1728)
        if (remaining >= 64 && remaining <= CcittConstants.MaxStandardMakeupRunLength)
        {
            int makeupIndex = (remaining / 64) - 1;
            HuffmanCode[] makeupCodes = isWhite ? WhiteMakeupCodes : BlackMakeupCodes;
            if (makeupIndex < makeupCodes.Length)
            {
                makeupCode = makeupCodes[makeupIndex];
                remaining = remaining % 64;
            }
        }

        // Terminating code for the remainder
        terminatingCode = isWhite
            ? WhiteTerminatingCodes[remaining]
            : BlackTerminatingCodes[remaining];
    }
}