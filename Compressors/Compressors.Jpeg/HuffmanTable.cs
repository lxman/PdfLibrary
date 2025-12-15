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
        for (var i = 15; i >= 0; i--)
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
        // Debug: check if we're at the failing position
        long streamPos = reader.GetStreamPosition();
        int bitsBefore = reader.BitsInBuffer;
        bool isFailPosition = streamPos == 0x18B8 && bitsBefore == 7;

        if (isFailPosition)
        {
            Console.WriteLine($"  HUFFMAN DEBUG: Starting decode at pos=0x{streamPos:X6}, bits={bitsBefore}");
        }

        // CRITICAL: Check for markers BEFORE peeking bits!
        // This only works when the bit buffer is empty (after consuming all bits from previous codes)
        // If we're at a marker, return EOB (0x00) to signal end of block
        if (reader.IsAtMarker())
        {
            if (isFailPosition)
                Console.WriteLine($"  HUFFMAN DEBUG: At marker, returning 0x00");
            return 0x00;
        }

        // Try fast lookup first (only if we can peek 8 bits)
        int peek = reader.PeekBits(LookupBits);
        if (isFailPosition)
        {
            Console.WriteLine($"  HUFFMAN DEBUG: PeekBits({LookupBits}) returned {peek} (0x{peek:X2})");
            if (peek >= 0)
                Console.WriteLine($"    Binary: 0b{Convert.ToString(peek, 2).PadLeft(8, '0')}");
        }

        if (peek >= 0)
        {
            int lookup = _decodeLookup[peek];
            if (isFailPosition)
                Console.WriteLine($"  HUFFMAN DEBUG: Lookup[{peek}] = {lookup} (0x{lookup:X4})");

            if (lookup >= 0)
            {
                int length = lookup >> 8;
                int symbol = lookup & 0xFF;
                if (isFailPosition)
                    Console.WriteLine($"  HUFFMAN DEBUG: Fast decode: length={length}, symbol={symbol}");
                reader.SkipBits(length);
                return symbol;
            }
            else if (isFailPosition)
            {
                Console.WriteLine($"  HUFFMAN DEBUG: Fast lookup failed, going to slow path");
            }
        }

        // Slow path: read bit by bit
        // This handles codes longer than LookupBits and end-of-data scenarios
        var code = 0;
        for (var length = 1; length <= MaxCodeLength; length++)
        {
            int bit = reader.ReadBit();
            if (isFailPosition)
                Console.WriteLine($"  HUFFMAN DEBUG: Slow path length={length}, ReadBit()={bit}");

            if (bit < 0)
            {
                // Check if we're at a marker boundary
                if (reader.IsAtMarker())
                {
                    if (isFailPosition)
                        Console.WriteLine($"  HUFFMAN DEBUG: At marker during slow path, returning 0x00");
                    return 0x00;
                }
                if (isFailPosition)
                    Console.WriteLine($"  HUFFMAN DEBUG: ReadBit() returned -1, returning error");
                return -1;
            }

            code = (code << 1) | bit;
            if (isFailPosition)
                Console.WriteLine($"  HUFFMAN DEBUG: Code so far = {code} (0b{Convert.ToString(code, 2).PadLeft(length, '0')})");

            // Check if this code exists at this length
            int index = FindCode(code, length);
            if (isFailPosition)
                Console.WriteLine($"  HUFFMAN DEBUG: FindCode({code}, {length}) = {index}");

            if (index >= 0)
            {
                int symbol = Values[index];
                if (isFailPosition)
                    Console.WriteLine($"  HUFFMAN DEBUG: Slow decode success: symbol={symbol}");
                return symbol;
            }
        }

        // JPEG PADDING DETECTION:
        // After exhausting all code lengths without a match, check if this is a padding pattern.
        // JPEG encoders pad with all-1s bits before restart markers to align to byte boundaries.
        // Some encoders also use this padding between blocks. If we have an all-1s pattern
        // (e.g., 0b1111111), treat it as padding and realign to the next byte boundary.
        int maxCode = (1 << MaxCodeLength) - 1; // All 1s for MaxCodeLength bits
        if (code == maxCode)
        {
            // This is an all-1s padding pattern
            Console.WriteLine($"  HUFFMAN: Detected padding pattern 0b{Convert.ToString(code, 2).PadLeft(MaxCodeLength, '0')} at length {MaxCodeLength}");
            Console.WriteLine($"  HUFFMAN: Discarding padding and realigning to next byte boundary");

            // Align to next byte boundary and try decoding again
            reader.AlignToByte();

            // Recursively try to decode from the next byte
            // Note: This assumes padding only happens once per code
            return Decode(reader);
        }

        if (isFailPosition)
            Console.WriteLine($"  HUFFMAN DEBUG: Slow path exhausted all lengths, returning -1");
        return -1; // Invalid code
    }

    /// <summary>
    /// Builds the encoding lookup table.
    /// </summary>
    private void BuildEncodeTable()
    {
        // Initialize all entries as invalid
        for (var i = 0; i < 256; i++)
        {
            _encodeTable[i] = (0, 0);
        }

        var code = 0;
        var valueIndex = 0;

        for (var length = 1; length <= 16; length++)
        {
            for (var i = 0; i < Bits[length - 1]; i++)
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
        for (var i = 0; i < _decodeLookup.Length; i++)
        {
            _decodeLookup[i] = -1;
        }

        var code = 0;
        var valueIndex = 0;

        for (var length = 1; length <= 16; length++)
        {
            for (var i = 0; i < Bits[length - 1]; i++)
            {
                byte symbol = Values[valueIndex++];

                // If code fits in lookup table, add all entries
                if (length <= LookupBits)
                {
                    int shift = LookupBits - length;
                    int baseIndex = code << shift;
                    int count = 1 << shift;

                    for (var j = 0; j < count; j++)
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
        var checkCode = 0;
        var valueIndex = 0;

        for (var len = 1; len <= length; len++)
        {
            for (var i = 0; i < Bits[len - 1]; i++)
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

        var totalValues = 0;
        for (var i = 0; i < 16; i++)
            totalValues += Bits[i];

        var data = new byte[1 + 16 + totalValues];
        data[0] = (byte)((tableClass << 4) | (tableId & 0x0F));

        for (var i = 0; i < 16; i++)
            data[1 + i] = Bits[i];

        for (var i = 0; i < totalValues; i++)
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

        ReadOnlySpan<byte> bits = data.Slice(1, 16);

        var totalValues = 0;
        for (var i = 0; i < 16; i++)
            totalValues += bits[i];

        if (data.Length < 17 + totalValues)
            throw new ArgumentException("DHT segment data too short for values", nameof(data));

        ReadOnlySpan<byte> values = data.Slice(17, totalValues);

        return new HuffmanTable(bits, values);
    }
}
