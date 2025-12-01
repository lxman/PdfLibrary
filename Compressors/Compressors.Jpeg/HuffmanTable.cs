using System;

namespace Compressors.Jpeg;

/// <summary>
/// Represents a Huffman table for JPEG encoding/decoding.
/// Can be used for both DC and AC coefficient coding.
/// </summary>
public class HuffmanTable
{
    /// <summary>
    /// Number of codes of each length (1-16 bits). Index 0 = 1-bit codes.
    /// </summary>
    public readonly byte[] Bits;

    /// <summary>
    /// Symbol values in order of increasing code length.
    /// </summary>
    public readonly byte[] Values;

    /// <summary>
    /// Encoding lookup: symbol -> (code, length)
    /// </summary>
    private readonly (ushort Code, byte Length)[] _encodeTable;

    /// <summary>
    /// Decoding lookup: built as a tree or table for fast lookup
    /// </summary>
    private readonly int[] _decodeTable;
    private readonly int[] _decodeLookup;
    private const int LookupBits = 8;

    /// <summary>
    /// Maximum code length in this table.
    /// </summary>
    public int MaxCodeLength { get; }

    /// <summary>
    /// Creates a Huffman table from BITS and HUFFVAL arrays.
    /// </summary>
    /// <param name="bits">Number of codes of each length (16 elements for lengths 1-16)</param>
    /// <param name="values">Symbol values</param>
    public HuffmanTable(ReadOnlySpan<byte> bits, ReadOnlySpan<byte> values)
    {
        if (bits.Length != 16)
            throw new ArgumentException("Bits array must have exactly 16 elements", nameof(bits));

        Bits = bits.ToArray();
        Values = values.ToArray();

        // Find max code length
        MaxCodeLength = 0;
        for (int i = 15; i >= 0; i--)
        {
            if (Bits[i] > 0)
            {
                MaxCodeLength = i + 1;
                break;
            }
        }

        // Build encoding table
        _encodeTable = new (ushort, byte)[256];
        BuildEncodeTable();

        // Build decoding structures
        _decodeTable = new int[1 << MaxCodeLength];
        _decodeLookup = new int[1 << LookupBits];
        BuildDecodeTable();
    }

    /// <summary>
    /// Creates standard DC luminance Huffman table.
    /// </summary>
    public static HuffmanTable CreateDcLuminance()
    {
        return new HuffmanTable(JpegConstants.DcLuminanceBits, JpegConstants.DcLuminanceValues);
    }

    /// <summary>
    /// Creates standard DC chrominance Huffman table.
    /// </summary>
    public static HuffmanTable CreateDcChrominance()
    {
        return new HuffmanTable(JpegConstants.DcChrominanceBits, JpegConstants.DcChrominanceValues);
    }

    /// <summary>
    /// Creates standard AC luminance Huffman table.
    /// </summary>
    public static HuffmanTable CreateAcLuminance()
    {
        return new HuffmanTable(JpegConstants.AcLuminanceBits, JpegConstants.AcLuminanceValues);
    }

    /// <summary>
    /// Creates standard AC chrominance Huffman table.
    /// </summary>
    public static HuffmanTable CreateAcChrominance()
    {
        return new HuffmanTable(JpegConstants.AcChrominanceBits, JpegConstants.AcChrominanceValues);
    }

    /// <summary>
    /// Gets the Huffman code and length for a symbol.
    /// </summary>
    public (ushort Code, byte Length) Encode(byte symbol)
    {
        return _encodeTable[symbol];
    }

    /// <summary>
    /// Decodes a symbol from a bit reader.
    /// </summary>
    /// <param name="reader">Bit reader positioned at the start of a code</param>
    /// <returns>Decoded symbol, or -1 on error</returns>
    public int Decode(BitReader reader)
    {
        // Try fast lookup first (only if we can peek 8 bits)
        int peek = reader.PeekBits(LookupBits);
        if (peek >= 0)
        {
            int lookup = _decodeLookup[peek];
            if (lookup >= 0)
            {
                int length = lookup >> 8;
                int symbol = lookup & 0xFF;
                reader.SkipBits(length);
                return symbol;
            }
        }

        // Slow path: read bit by bit
        // This handles codes longer than LookupBits and end-of-data scenarios
        int code = 0;
        for (int length = 1; length <= MaxCodeLength; length++)
        {
            int bit = reader.ReadBit();
            if (bit < 0)
                return -1;

            code = (code << 1) | bit;

            // Check if this code exists at this length
            int index = FindCode(code, length);
            if (index >= 0)
                return Values[index];
        }

        return -1; // Invalid code
    }

    /// <summary>
    /// Builds the encoding lookup table.
    /// </summary>
    private void BuildEncodeTable()
    {
        // Initialize all entries as invalid
        for (int i = 0; i < 256; i++)
        {
            _encodeTable[i] = (0, 0);
        }

        int code = 0;
        int valueIndex = 0;

        for (int length = 1; length <= 16; length++)
        {
            for (int i = 0; i < Bits[length - 1]; i++)
            {
                byte symbol = Values[valueIndex++];
                _encodeTable[symbol] = ((ushort)code, (byte)length);
                code++;
            }
            code <<= 1;
        }
    }

    /// <summary>
    /// Builds the decoding lookup table.
    /// </summary>
    private void BuildDecodeTable()
    {
        // Initialize lookup as invalid
        for (int i = 0; i < _decodeLookup.Length; i++)
        {
            _decodeLookup[i] = -1;
        }

        int code = 0;
        int valueIndex = 0;

        for (int length = 1; length <= 16; length++)
        {
            for (int i = 0; i < Bits[length - 1]; i++)
            {
                byte symbol = Values[valueIndex++];

                // If code fits in lookup table, add all entries
                if (length <= LookupBits)
                {
                    int shift = LookupBits - length;
                    int baseIndex = code << shift;
                    int count = 1 << shift;

                    for (int j = 0; j < count; j++)
                    {
                        _decodeLookup[baseIndex + j] = (length << 8) | symbol;
                    }
                }

                code++;
            }
            code <<= 1;
        }
    }

    /// <summary>
    /// Finds the value index for a code of the specified length.
    /// </summary>
    private int FindCode(int code, int length)
    {
        int checkCode = 0;
        int valueIndex = 0;

        for (int len = 1; len <= length; len++)
        {
            for (int i = 0; i < Bits[len - 1]; i++)
            {
                if (len == length && checkCode == code)
                    return valueIndex;

                valueIndex++;
                checkCode++;
            }
            checkCode <<= 1;
        }

        return -1;
    }

    /// <summary>
    /// Writes this Huffman table to a DHT segment.
    /// </summary>
    public byte[] ToSegmentData(int tableClass, int tableId)
    {
        // tableClass: 0 = DC, 1 = AC
        // tableId: 0-3

        int totalValues = 0;
        for (int i = 0; i < 16; i++)
            totalValues += Bits[i];

        var data = new byte[1 + 16 + totalValues];
        data[0] = (byte)((tableClass << 4) | (tableId & 0x0F));

        for (int i = 0; i < 16; i++)
            data[1 + i] = Bits[i];

        for (int i = 0; i < totalValues; i++)
            data[17 + i] = Values[i];

        return data;
    }

    /// <summary>
    /// Creates a Huffman table from DHT segment data.
    /// </summary>
    public static HuffmanTable FromSegmentData(ReadOnlySpan<byte> data, out int tableClass, out int tableId)
    {
        if (data.Length < 17)
            throw new ArgumentException("DHT segment data too short", nameof(data));

        byte info = data[0];
        tableClass = (info >> 4) & 0x0F;
        tableId = info & 0x0F;

        var bits = data.Slice(1, 16);

        int totalValues = 0;
        for (int i = 0; i < 16; i++)
            totalValues += bits[i];

        if (data.Length < 17 + totalValues)
            throw new ArgumentException("DHT segment data too short for values", nameof(data));

        var values = data.Slice(17, totalValues);

        return new HuffmanTable(bits, values);
    }
}
