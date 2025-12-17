namespace ImageLibrary.Jpeg;

/// <summary>
/// Performs dequantization of DCT coefficients.
/// This is Stage 4 of the decoder.
/// </summary>
internal class Dequantizer
{
    private readonly JpegFrame _frame;

    /// <summary>
    /// Creates a new dequantizer for the given frame.
    /// </summary>
    public Dequantizer(JpegFrame frame)
    {
        _frame = frame;
    }

    /// <summary>
    /// Dequantizes all DCT coefficient blocks.
    /// Multiplies each coefficient by the corresponding quantization table value.
    /// </summary>
    /// <param name="blocks">The encoded DCT coefficients [component][block][coefficient]</param>
    /// <returns>The dequantized DCT coefficients [component][block][coefficient]</returns>
    public int[][][] DequantizeAll(short[][][] blocks)
    {
        var result = new int[blocks.Length][][];

        for (var c = 0; c < blocks.Length; c++)
        {
            JpegComponent comp = _frame.Components[c];
            ushort[]? qt = _frame.QuantizationTables[comp.QuantizationTableId];

            if (qt == null)
            {
                throw new JpegException($"Quantization table {comp.QuantizationTableId} not found for component {c}");
            }

            result[c] = new int[blocks[c].Length][];

            for (var b = 0; b < blocks[c].Length; b++)
            {
                result[c][b] = DequantizeBlock(blocks[c][b], qt);
            }
        }

        return result;
    }

    /// <summary>
    /// Dequantizes a single 8x8 block of DCT coefficients.
    /// </summary>
    /// <param name="block">The encoded DCT coefficients (64 values in zig-zag order)</param>
    /// <param name="qt">The quantization table (64 values in zig-zag order)</param>
    /// <returns>The dequantized DCT coefficients (64 values in natural order)</returns>
    public static int[] DequantizeBlock(short[] block, ushort[] qt)
    {
        var result = new int[64];

        // The block is stored in natural 8x8 order after zig-zag reordering in entropy decoder.
        // The quantization table is stored in zig-zag order in the JPEG file.
        // We need to match them up correctly.

        for (var i = 0; i < 64; i++)
        {
            // block[i] is the coefficient at position i in 8x8 order (row-major)
            // qt is in zig-zag order, so we need to use the zig-zag index
            int zigzagIndex = GetZigZagIndex(i);
            result[i] = block[i] * qt[zigzagIndex];
        }

        return result;
    }

    /// <summary>
    /// Maps natural (row-major) 8x8 index to zig-zag index.
    /// This is the inverse of the ZigZagOrder table.
    /// </summary>
    private static readonly byte[] NaturalToZigZag = BuildNaturalToZigZag();

    private static byte[] BuildNaturalToZigZag()
    {
        var result = new byte[64];
        for (var i = 0; i < 64; i++)
        {
            result[EntropyDecoder.ZigZagOrder[i]] = (byte)i;
        }
        return result;
    }

    private static int GetZigZagIndex(int naturalIndex)
    {
        return NaturalToZigZag[naturalIndex];
    }

    /// <summary>
    /// Dequantizes a single block for testing purposes.
    /// </summary>
    public int[] DequantizeSingleBlock(short[] block, int componentIndex)
    {
        JpegComponent comp = _frame.Components[componentIndex];
        ushort[]? qt = _frame.QuantizationTables[comp.QuantizationTableId];

        if (qt == null)
        {
            throw new JpegException($"Quantization table {comp.QuantizationTableId} not found");
        }

        return DequantizeBlock(block, qt);
    }
}
