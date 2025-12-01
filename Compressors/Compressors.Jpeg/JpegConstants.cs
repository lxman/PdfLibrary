namespace Compressors.Jpeg;

/// <summary>
/// JPEG standard constants including markers, Huffman tables, quantization matrices, and zigzag order.
/// Values from ITU-T T.81 Annex K.
/// </summary>
public static class JpegConstants
{
    #region JPEG Markers

    public const byte MarkerPrefix = 0xFF;

    // Start/End markers
    public const byte SOI = 0xD8;  // Start of Image
    public const byte EOI = 0xD9;  // End of Image

    // Frame markers (Start of Frame)
    public const byte SOF0 = 0xC0; // Baseline DCT
    public const byte SOF1 = 0xC1; // Extended Sequential DCT
    public const byte SOF2 = 0xC2; // Progressive DCT
    public const byte SOF3 = 0xC3; // Lossless (sequential)

    // Huffman table markers
    public const byte DHT = 0xC4;  // Define Huffman Table

    // Arithmetic coding markers
    public const byte DAC = 0xCC;  // Define Arithmetic Coding

    // Restart markers
    public const byte RST0 = 0xD0;
    public const byte RST1 = 0xD1;
    public const byte RST2 = 0xD2;
    public const byte RST3 = 0xD3;
    public const byte RST4 = 0xD4;
    public const byte RST5 = 0xD5;
    public const byte RST6 = 0xD6;
    public const byte RST7 = 0xD7;

    // Other markers
    public const byte DQT = 0xDB;  // Define Quantization Table
    public const byte DNL = 0xDC;  // Define Number of Lines
    public const byte DRI = 0xDD;  // Define Restart Interval
    public const byte DHP = 0xDE;  // Define Hierarchical Progression
    public const byte EXP = 0xDF;  // Expand Reference Component

    // Application markers
    public const byte APP0 = 0xE0; // JFIF
    public const byte APP1 = 0xE1; // EXIF
    public const byte APP2 = 0xE2;
    // ... APP3-APP15 follow

    // Comment
    public const byte COM = 0xFE;

    // Scan marker
    public const byte SOS = 0xDA;  // Start of Scan

    #endregion

    #region Zigzag Scan Order

    /// <summary>
    /// Zigzag scan order for 8x8 block - maps linear index to (row, col) position.
    /// Used after DCT to order coefficients from low to high frequency.
    /// </summary>
    public static readonly byte[] ZigzagOrder = new byte[]
    {
         0,  1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63
    };

    /// <summary>
    /// Inverse zigzag - maps (row * 8 + col) to linear zigzag position.
    /// </summary>
    public static readonly byte[] ZigzagInverse = new byte[]
    {
         0,  1,  5,  6, 14, 15, 27, 28,
         2,  4,  7, 13, 16, 26, 29, 42,
         3,  8, 12, 17, 25, 30, 41, 43,
         9, 11, 18, 24, 31, 40, 44, 53,
        10, 19, 23, 32, 39, 45, 52, 54,
        20, 22, 33, 38, 46, 51, 55, 60,
        21, 34, 37, 47, 50, 56, 59, 61,
        35, 36, 48, 49, 57, 58, 62, 63
    };

    #endregion

    #region Standard Quantization Tables (ITU-T T.81 Annex K)

    /// <summary>
    /// Standard luminance (Y) quantization table at quality 50.
    /// </summary>
    public static readonly byte[] LuminanceQuantTable = new byte[]
    {
        16, 11, 10, 16,  24,  40,  51,  61,
        12, 12, 14, 19,  26,  58,  60,  55,
        14, 13, 16, 24,  40,  57,  69,  56,
        14, 17, 22, 29,  51,  87,  80,  62,
        18, 22, 37, 56,  68, 109, 103,  77,
        24, 35, 55, 64,  81, 104, 113,  92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103,  99
    };

    /// <summary>
    /// Standard chrominance (Cb/Cr) quantization table at quality 50.
    /// </summary>
    public static readonly byte[] ChrominanceQuantTable = new byte[]
    {
        17, 18, 24, 47, 99, 99, 99, 99,
        18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99,
        47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99
    };

    #endregion

    #region Standard Huffman Tables (ITU-T T.81 Annex K)

    // DC Luminance Huffman Table
    // BITS: number of codes of each length 1-16
    public static readonly byte[] DcLuminanceBits = new byte[]
    {
        0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0
    };

    // HUFFVAL: symbol values
    public static readonly byte[] DcLuminanceValues = new byte[]
    {
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11
    };

    // DC Chrominance Huffman Table
    public static readonly byte[] DcChrominanceBits = new byte[]
    {
        0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0
    };

    public static readonly byte[] DcChrominanceValues = new byte[]
    {
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11
    };

    // AC Luminance Huffman Table
    public static readonly byte[] AcLuminanceBits = new byte[]
    {
        0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 125
    };

    public static readonly byte[] AcLuminanceValues = new byte[]
    {
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12,
        0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08,
        0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16,
        0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
        0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
        0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
        0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
        0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
        0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6,
        0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
        0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4,
        0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
        0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA,
        0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA
    };

    // AC Chrominance Huffman Table
    public static readonly byte[] AcChrominanceBits = new byte[]
    {
        0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 119
    };

    public static readonly byte[] AcChrominanceValues = new byte[]
    {
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21,
        0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
        0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91,
        0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0,
        0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34,
        0xE1, 0x25, 0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26,
        0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38,
        0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
        0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
        0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
        0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96,
        0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5,
        0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4,
        0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
        0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2,
        0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA,
        0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9,
        0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA
    };

    #endregion

    #region DCT Constants

    /// <summary>
    /// Block size for JPEG DCT (8x8).
    /// </summary>
    public const int BlockSize = 8;

    /// <summary>
    /// Total coefficients in a block.
    /// </summary>
    public const int BlockSize2 = BlockSize * BlockSize; // 64

    /// <summary>
    /// Level shift value (subtract before DCT, add after IDCT).
    /// </summary>
    public const int LevelShift = 128;

    #endregion

    #region Huffman Special Codes

    /// <summary>
    /// End of Block (EOB) - indicates all remaining AC coefficients are zero.
    /// Encoded as (0, 0) = run=0, size=0.
    /// </summary>
    public const byte EOB = 0x00;

    /// <summary>
    /// Zero Run Length (ZRL) - indicates 16 consecutive zeros.
    /// Encoded as (15, 0) = run=15, size=0.
    /// </summary>
    public const byte ZRL = 0xF0;

    #endregion

    #region JFIF Constants

    public static readonly byte[] JfifSignature = new byte[] { 0x4A, 0x46, 0x49, 0x46, 0x00 }; // "JFIF\0"

    public const byte JfifMajorVersion = 1;
    public const byte JfifMinorVersion = 1;

    #endregion
}
