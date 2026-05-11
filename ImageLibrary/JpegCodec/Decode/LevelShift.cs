namespace JpegCodec.Decode;

// T.81 §F.1.1.3 / Annex A.3.1 — sample precision and level shift.
// For 8-bit samples, the encoder subtracts 128 before DCT to map [0..255]
// to [-128..127]. The decoder adds 128 back and clamps to [0..255].
internal static class LevelShift
{
    public static byte Shift(int value)
    {
        int shifted = value + 128;
        if (shifted < 0) return 0;
        if (shifted > 255) return 255;
        return (byte)shifted;
    }

    public static void ShiftBlockInPlace(short[] block, byte[] destination, int destinationOffset, int stride)
    {
        for (var y = 0; y < 8; y++)
        {
            int srcRow = y * 8;
            int dstRow = destinationOffset + y * stride;
            for (var x = 0; x < 8; x++)
                destination[dstRow + x] = Shift(block[srcRow + x]);
        }
    }
}
