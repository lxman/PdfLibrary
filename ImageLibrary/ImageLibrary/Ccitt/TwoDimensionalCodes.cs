namespace ImageLibrary.Ccitt;

/// <summary>
/// Two-dimensional mode codes for CCITT Group 3 2D (MR) and Group 4 (MMR).
/// Based on ITU-T T.4 Table 2 and T.6 Table 1.
/// </summary>
public static class TwoDimensionalCodes
{
    /// <summary>
    /// Pass mode code: 0001 (4 bits).
    /// Used when b2 lies to the left of a1.
    /// </summary>
    public const int PassCode = 0b0001;
    public const int PassCodeBits = 4;

    /// <summary>
    /// Horizontal mode code: 001 (3 bits).
    /// Followed by the run-length codes for a0a1 and a1a2.
    /// </summary>
    public const int HorizontalCode = 0b001;
    public const int HorizontalCodeBits = 3;

    /// <summary>
    /// Vertical mode codes.
    /// V(0) = a1 directly below b1
    /// VR(n) = a1 is n pixels to the right of b1
    /// VL(n) = a1 is n pixels to the left of b1
    /// </summary>
    public static class Vertical
    {
        // V(0): a1 is directly under b1
        public const int V0Code = 0b1;
        public const int V0Bits = 1;

        // VR(1): a1 is 1 pixel to the right of b1
        public const int VR1Code = 0b011;
        public const int VR1Bits = 3;

        // VR(2): a1 is 2 pixels to the right of b1
        public const int VR2Code = 0b000011;
        public const int VR2Bits = 6;

        // VR(3): a1 is 3 pixels to the right of b1
        public const int VR3Code = 0b0000011;
        public const int VR3Bits = 7;

        // VL(1): a1 is 1 pixel to the left of b1
        public const int VL1Code = 0b010;
        public const int VL1Bits = 3;

        // VL(2): a1 is 2 pixels to the left of b1
        public const int VL2Code = 0b000010;
        public const int VL2Bits = 6;

        // VL(3): a1 is 3 pixels to the left of b1
        public const int VL3Code = 0b0000010;
        public const int VL3Bits = 7;
    }

    /// <summary>
    /// Extension code for uncompressed mode (not commonly used).
    /// </summary>
    public const int ExtensionCode = 0b0000001111;
    public const int ExtensionCodeBits = 10;

    /// <summary>
    /// Gets the vertical mode code and bit length for a given offset.
    /// </summary>
    /// <param name="offset">The offset of a1 relative to b1 (-3 to +3).</param>
    /// <param name="code">The code bits.</param>
    /// <param name="bitLength">The number of bits in the code.</param>
    /// <returns>True if the offset is valid for vertical mode (-3 to +3), false otherwise.</returns>
    public static bool TryGetVerticalCode(int offset, out int code, out int bitLength)
    {
        switch (offset)
        {
            case 0:
                code = Vertical.V0Code;
                bitLength = Vertical.V0Bits;
                return true;
            case 1:
                code = Vertical.VR1Code;
                bitLength = Vertical.VR1Bits;
                return true;
            case 2:
                code = Vertical.VR2Code;
                bitLength = Vertical.VR2Bits;
                return true;
            case 3:
                code = Vertical.VR3Code;
                bitLength = Vertical.VR3Bits;
                return true;
            case -1:
                code = Vertical.VL1Code;
                bitLength = Vertical.VL1Bits;
                return true;
            case -2:
                code = Vertical.VL2Code;
                bitLength = Vertical.VL2Bits;
                return true;
            case -3:
                code = Vertical.VL3Code;
                bitLength = Vertical.VL3Bits;
                return true;
            default:
                code = 0;
                bitLength = 0;
                return false;
        }
    }

    /// <summary>
    /// Determines if vertical mode can be used for a given offset.
    /// </summary>
    /// <param name="offset">The offset of a1 relative to b1.</param>
    /// <returns>True if |offset| &lt;= 3.</returns>
    public static bool CanUseVerticalMode(int offset)
    {
        return offset >= -3 && offset <= 3;
    }
}