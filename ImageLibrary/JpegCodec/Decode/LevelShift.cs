namespace JpegCodec.Decode;

// T.81 §F.1.1.3 / Annex A.3.1 — sample precision and level shift.
// For 8-bit samples, the encoder subtracts 128 before DCT to map [0..255]
// to [-128..127]. The decoder adds 128 back and clamps to [0..255].
internal static class LevelShift
{
    private const int TableOffset = 384;
    private static readonly byte[] ClampTable = BuildClampTable();

    private static byte[] BuildClampTable()
    {
        var table = new byte[768];
        for (var i = 0; i < 768; i++)
        {
            int v = i - TableOffset + 128;
            if (v < 0) table[i] = 0;
            else if (v > 255) table[i] = 255;
            else table[i] = (byte)v;
        }
        return table;
    }

    public static byte Shift(int value)
    {
        int idx = value + TableOffset;
        if ((uint)idx < 768u) return ClampTable[idx];
        return (byte)(value + 128 < 0 ? 0 : 255);
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
