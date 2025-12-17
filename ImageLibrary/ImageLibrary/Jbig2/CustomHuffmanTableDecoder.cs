using System.Collections.Generic;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Decodes custom Huffman table segments (type 53).
/// T.88 Section 7.4.12 defines the format.
/// </summary>
internal static class CustomHuffmanTableDecoder
{
    /// <summary>
    /// Decode a custom Huffman table from segment data.
    /// </summary>
    /// <param name="data">The segment data bytes</param>
    /// <returns>A compiled HuffmanTable ready for decoding</returns>
    public static HuffmanTable Decode(byte[] data) => Decode(data, null);

    /// <summary>
    /// Decode a custom Huffman table from segment data with options.
    /// </summary>
    /// <param name="data">The segment data bytes</param>
    /// <param name="options">Decoder options for resource limits</param>
    /// <returns>A compiled HuffmanTable ready for decoding</returns>
    public static HuffmanTable Decode(byte[] data, Jbig2DecoderOptions? options)
    {
        if (data.Length < 1)
            throw new Jbig2DataException("Custom Huffman table segment too short");

        int maxLines = options?.MaxHuffmanTableLines ?? 4096;

        var reader = new BitReader(data);

        // 7.4.12.1 - Table flags (1 byte)
        byte flags = reader.ReadByte();

        // HTPS: Prefix length field size (bits 0-2) - value is (HTPS + 1) bits
        int htps = (flags & 0x07) + 1;

        // HTRS: Range length field size (bits 3-5) - value is (HTRS + 1) bits
        int htrs = ((flags >> 3) & 0x07) + 1;

        // HTLOW: Has low extension line (bit 6)
        bool htlow = (flags & 0x40) != 0;

        // HTOOB: Has out-of-band line (bit 7)
        bool htoob = (flags & 0x80) != 0;

        // 7.4.12.2 - Read lines until we get the terminator (PREFLEN=0 and RANGELEN=0)
        var lines = new List<HuffmanLine>();
        var bitOffset = 8; // Start after flags byte

        while (true)
        {
            // Enforce maximum lines limit to prevent DoS
            if (lines.Count >= maxLines)
                throw new Jbig2ResourceException($"Custom Huffman table exceeds maximum lines ({maxLines})");

            // Each line: PREFLEN (HTPS bits), RANGELEN (HTRS bits), RANGELOW (32 bits signed)
            int prefLen = ReadBits(data, ref bitOffset, htps);
            int rangeLen = ReadBits(data, ref bitOffset, htrs);

            // Check for terminator
            if (prefLen == 0 && rangeLen == 0)
            {
                break;
            }

            // Read RANGELOW as 32-bit signed integer
            int rangeLow = ReadSignedBits(data, ref bitOffset, 32);

            lines.Add(new HuffmanLine(prefLen, rangeLen, rangeLow));
        }

        if (lines.Count == 0)
            throw new Jbig2DataException("Custom Huffman table has no lines");

        // T.88 B.2: Add the low and OOB lines if specified
        // The low line has PREFLEN=0, RANGELEN=32 (unbounded)
        // The OOB line has PREFLEN=last_line_preflen, RANGELEN=0, RANGELOW=0
        //
        // Actually, per T.88 7.4.12 and B.2, the table segment already contains
        // all lines including low/high extensions. The HTLOW and HTOOB flags
        // indicate whether those lines are present in the decoded table.
        //
        // The standard HuffmanTable.Build expects lines in a specific order:
        // - Normal lines first
        // - Low extension line (second to last if present)
        // - High extension line (last if no OOB, or second to last if OOB)
        // - OOB line (last if present)

        // Build the HuffmanParams with the parsed lines
        HuffmanLine[] lineArray = lines.ToArray();
        var huffParams = new HuffmanParams(htoob, lineArray);

        return HuffmanTable.Build(huffParams);
    }

    /// <summary>
    /// Read unsigned bits from data starting at bitOffset.
    /// </summary>
    private static int ReadBits(byte[] data, ref int bitOffset, int count)
    {
        var result = 0;
        for (var i = 0; i < count; i++)
        {
            int byteIdx = bitOffset >> 3;
            int bitIdx = 7 - (bitOffset & 7);

            if (byteIdx >= data.Length)
                throw new Jbig2DataException("Custom Huffman table data truncated");

            int bit = (data[byteIdx] >> bitIdx) & 1;
            result = (result << 1) | bit;
            bitOffset++;
        }
        return result;
    }

    /// <summary>
    /// Read signed bits from data (two's complement).
    /// </summary>
    private static int ReadSignedBits(byte[] data, ref int bitOffset, int count)
    {
        uint result = 0;
        for (var i = 0; i < count; i++)
        {
            int byteIdx = bitOffset >> 3;
            int bitIdx = 7 - (bitOffset & 7);

            if (byteIdx >= data.Length)
                throw new Jbig2DataException("Custom Huffman table data truncated");

            var bit = (uint)((data[byteIdx] >> bitIdx) & 1);
            result = (result << 1) | bit;
            bitOffset++;
        }

        // Sign extend if needed
        if (count == 32)
            return (int)result;

        // For smaller counts, check sign bit and extend
        if ((result & (1u << (count - 1))) != 0)
        {
            // Negative - sign extend
            result |= ~((1u << count) - 1);
        }
        return (int)result;
    }
}
