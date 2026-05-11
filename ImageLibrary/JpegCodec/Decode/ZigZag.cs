namespace JpegCodec.Decode;

// 8x8 zigzag scan order per T.81 Figure 5.
//
// JPEG stores DCT coefficients in zigzag order to cluster low-frequency
// terms first. ZigzagToNatural[k] gives the natural-order index (row*8 +
// col) of the k-th zigzag position. NaturalToZigzag is the inverse.
internal static class ZigZag
{
    public static readonly byte[] ZigzagToNatural =
    [
        0,  1,  8, 16,  9,  2,  3, 10,
       17, 24, 32, 25, 18, 11,  4,  5,
       12, 19, 26, 33, 40, 48, 41, 34,
       27, 20, 13,  6,  7, 14, 21, 28,
       35, 42, 49, 56, 57, 50, 43, 36,
       29, 22, 15, 23, 30, 37, 44, 51,
       58, 59, 52, 45, 38, 31, 39, 46,
       53, 60, 61, 54, 47, 55, 62, 63,
    ];

    public static readonly byte[] NaturalToZigzag = BuildInverse();

    private static byte[] BuildInverse()
    {
        var inv = new byte[64];
        for (byte k = 0; k < 64; k++)
            inv[ZigzagToNatural[k]] = k;
        return inv;
    }
}
