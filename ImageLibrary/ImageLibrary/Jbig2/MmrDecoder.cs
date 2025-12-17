using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Decodes MMR (Modified Modified READ) encoded data, also known as Group 4 fax (ITU-T T.6).
/// T.88 Annex A describes MMR usage in JBIG2.
/// </summary>
internal sealed class MmrDecoder
{
    private readonly byte[] _data;
    private readonly int _startOffset;
    private readonly int _endOffset;
    private readonly int _width;
    private readonly int _height;
    private readonly Jbig2DecoderOptions _options;

    private int _dataIndex;
    private int _bitIndex;
    private uint _word;
    private long _decodeOperations;

    private const uint MINUS1 = uint.MaxValue;

    /// <summary>
    /// Whether to check for EOFB (End Of Facsimile Block) after all lines.
    /// Per T.88 Annex A.2, EOFB should NOT be present when height is known
    /// (e.g., halftone bit planes, generic regions with explicit height).
    /// Default is true for backwards compatibility, but should be false for halftone planes.
    /// </summary>
    public bool CheckForEofb { get; set; } = true;

    /// <summary>
    /// Gets the number of bytes consumed during decoding.
    /// This tracks bit-level consumption and returns the ceiling in bytes.
    /// </summary>
    public int BytesConsumed
    {
        get
        {
            // Total bits consumed = (bytes read * 8) - remaining valid bits in buffer
            // remaining valid bits = 32 - _bitIndex (since _bitIndex is empty slots)
            // totalBitsConsumed = (_dataIndex - _startOffset) * 8 - (32 - _bitIndex)
            //                   = (_dataIndex - _startOffset) * 8 - 32 + _bitIndex
            int bytesRead = _dataIndex - _startOffset;
            int bitsConsumed = bytesRead * 8 - 32 + _bitIndex;

            // Round up to byte boundary
            int consumed = (bitsConsumed + 7) / 8;

            // Ensure non-negative
            if (consumed < 0) consumed = 0;

            return consumed;
        }
    }

    public MmrDecoder(byte[] data, int offset, int length, int width, int height)
        : this(data, offset, length, width, height, null)
    {
    }

    public MmrDecoder(byte[] data, int offset, int length, int width, int height, Jbig2DecoderOptions? options)
    {
        _data = data;
        _startOffset = offset;
        _endOffset = offset + length;
        _width = width;
        _height = height;
        _options = options ?? Jbig2DecoderOptions.Default;
        _dataIndex = offset;
        _bitIndex = 32;
        _word = 0;
        _decodeOperations = 0;

        // Initialize word buffer
        while (_bitIndex >= 8 && _dataIndex < _endOffset)
        {
            _bitIndex -= 8;
            _word |= (uint)_data[_dataIndex] << _bitIndex;
            _dataIndex++;
        }
    }

    private void Consume(int nBits)
    {
        _decodeOperations += nBits;
        if (_decodeOperations > _options.MaxDecodeOperations)
            throw new Jbig2ResourceException($"MMR decode operation limit exceeded ({_options.MaxDecodeOperations})");

        _word <<= nBits;
        _bitIndex += nBits;
        while (_bitIndex >= 8 && _dataIndex < _endOffset)
        {
            _bitIndex -= 8;
            _word |= (uint)_data[_dataIndex] << _bitIndex;
            _dataIndex++;
        }
    }

    /// <summary>
    /// Decodes the MMR data into a bitmap.
    /// </summary>
    public Bitmap Decode()
    {
        var bitmap = new Bitmap(_width, _height);
        int stride = (_width + 7) / 8;
        var refLine = new byte[stride];
        var dstLine = new byte[stride];

        int startBits = (_dataIndex - _startOffset) * 8 - 32 + _bitIndex;

        for (var y = 0; y < _height; y++)
        {
            Array.Clear(dstLine, 0, stride);
            int bitsBeforeLine = (_dataIndex - _startOffset) * 8 - 32 + _bitIndex;
            bool eofb = DecodeLine(refLine, dstLine);
            int bitsAfterLine = (_dataIndex - _startOffset) * 8 - 32 + _bitIndex;

            // Copy decoded line to bitmap
            for (var x = 0; x < _width; x++)
            {
                if (GetBit(dstLine, x))
                    bitmap.SetPixel(x, y, 1);
            }

            // Swap reference and destination
            byte[] tmp = refLine;
            refLine = dstLine;
            dstLine = tmp;

            if (eofb)
                break;
        }

        // Check for EOFB (two EOL sequences, 24 bits)
        // Per T.88 Annex A.2, EOFB should NOT be present when height is known
        if (CheckForEofb && (_word >> 8) == 0x001001)
        {
            Consume(24);
        }

        // JBIG2 MMR: Pad to byte boundary at end of each stripe
        int bytesRead = _dataIndex - _startOffset;
        int bitsConsumed = bytesRead * 8 - 32 + _bitIndex;
        int bitsToAlign = (8 - (bitsConsumed % 8)) % 8;
        if (bitsToAlign > 0)
        {
            Consume(bitsToAlign);
        }

        return bitmap;
    }

    private bool DecodeLine(byte[] refLine, byte[] dstLine)
    {
        uint a0 = MINUS1;
        var c = 0; // 0 = white, 1 = black
        var iterations = 0;
        int maxIterations = _options.MaxLoopIterations;

        while (true)
        {
            if (++iterations > maxIterations)
                throw new Jbig2ResourceException($"MMR DecodeLine iteration limit exceeded ({maxIterations})");

            if (a0 != MINUS1 && a0 >= (uint)_width)
                break;

            uint word = _word;

            // H mode: 001
            if ((word >> 29) == 1)
            {
                Consume(3);
                if (a0 == MINUS1) a0 = 0;

                int run1, run2;
                uint a1, a2;

                if (c == 0)
                {
                    // White then black
                    run1 = DecodeRun(WhiteTable, 8);
                    run2 = DecodeRun(BlackTable, 7);
                    a1 = a0 + (uint)run1;
                    a2 = a1 + (uint)run2;
                    if (a1 > (uint)_width) a1 = (uint)_width;
                    if (a2 > (uint)_width) a2 = (uint)_width;
                    if (a1 < (uint)_width)
                        SetBits(dstLine, a1, a2);
                }
                else
                {
                    // Black then white
                    run1 = DecodeRun(BlackTable, 7);
                    run2 = DecodeRun(WhiteTable, 8);
                    a1 = a0 + (uint)run1;
                    a2 = a1 + (uint)run2;
                    if (a1 > (uint)_width) a1 = (uint)_width;
                    if (a2 > (uint)_width) a2 = (uint)_width;
                    if (a0 < (uint)_width)
                        SetBits(dstLine, a0, a1);
                }
                a0 = a2;
            }
            // P mode: 0001
            else if ((word >> 28) == 1)
            {
                Consume(4);
                uint b1 = FindChangingElementOfColor(refLine, a0, c == 0 ? 1 : 0);
                uint b2 = FindChangingElement(refLine, b1);
                if (c != 0 && a0 < (uint)_width)
                    SetBits(dstLine, a0, b2);
                a0 = b2;
            }
            // V(0) mode: 1
            else if ((word >> 31) == 1)
            {
                Consume(1);
                uint b1 = FindChangingElementOfColor(refLine, a0, c == 0 ? 1 : 0);
                if (c != 0 && a0 < (uint)_width)
                    SetBits(dstLine, a0, b1);
                a0 = b1;
                c = 1 - c;
            }
            // VR(1) mode: 011
            else if ((word >> 29) == 3)
            {
                Consume(3);
                uint b1 = FindChangingElementOfColor(refLine, a0, c == 0 ? 1 : 0);
                if (b1 + 1 <= (uint)_width) b1 += 1;
                if (c != 0 && a0 < (uint)_width)
                    SetBits(dstLine, a0, b1);
                a0 = b1;
                c = 1 - c;
            }
            // VR(2) mode: 000011
            else if ((word >> 26) == 3)
            {
                Consume(6);
                uint b1 = FindChangingElementOfColor(refLine, a0, c == 0 ? 1 : 0);
                if (b1 + 2 <= (uint)_width) b1 += 2;
                if (c != 0 && a0 < (uint)_width)
                    SetBits(dstLine, a0, b1);
                a0 = b1;
                c = 1 - c;
            }
            // VR(3) mode: 0000011
            else if ((word >> 25) == 3)
            {
                Consume(7);
                uint b1 = FindChangingElementOfColor(refLine, a0, c == 0 ? 1 : 0);
                if (b1 + 3 <= (uint)_width) b1 += 3;
                if (c != 0 && a0 < (uint)_width)
                    SetBits(dstLine, a0, b1);
                a0 = b1;
                c = 1 - c;
            }
            // VL(1) mode: 010
            else if ((word >> 29) == 2)
            {
                Consume(3);
                uint b1 = FindChangingElementOfColor(refLine, a0, c == 0 ? 1 : 0);
                if (b1 >= 1) b1 -= 1;
                if (c != 0 && a0 < (uint)_width)
                    SetBits(dstLine, a0, b1);
                a0 = b1;
                c = 1 - c;
            }
            // VL(2) mode: 000010
            else if ((word >> 26) == 2)
            {
                Consume(6);
                uint b1 = FindChangingElementOfColor(refLine, a0, c == 0 ? 1 : 0);
                if (b1 >= 2) b1 -= 2;
                if (c != 0 && a0 < (uint)_width)
                    SetBits(dstLine, a0, b1);
                a0 = b1;
                c = 1 - c;
            }
            // VL(3) mode: 0000010
            else if ((word >> 25) == 2)
            {
                Consume(7);
                uint b1 = FindChangingElementOfColor(refLine, a0, c == 0 ? 1 : 0);
                if (b1 >= 3) b1 -= 3;
                if (c != 0 && a0 < (uint)_width)
                    SetBits(dstLine, a0, b1);
                a0 = b1;
                c = 1 - c;
            }
            // Check for EOFB (End Of Facsimile Block): 000000000001 000000000001 = 0x001001
            // Per jbig2dec, EOFB can appear mid-stream when the encoder truncates the bit plane
            else if ((word >> 8) == 0x001001)
            {
                Consume(24);
                return true; // Signal EOFB found
            }
            else
            {
                // Unknown code - just break and return
                break;
            }
        }

        return false;
    }

    private int DecodeRun(MmrTableEntry[] table, int initialBits)
    {
        var result = 0;
        int val;

        do
        {
            val = DecodeCode(table, initialBits);
            if (val < 0)
                throw new Jbig2DataException("Invalid MMR code");
            try
            {
                result = checked(result + val);
            }
            catch (OverflowException)
            {
                throw new Jbig2DataException($"MMR run length overflow: {result} + {val}");
            }
        } while (val >= 64);

        return result;
    }

    private int DecodeCode(MmrTableEntry[] table, int initialBits)
    {
        var index = (int)(_word >> (32 - initialBits));
        MmrTableEntry entry = table[index];
        int val = entry.Value;
        int nBits = entry.Bits;

        if (nBits > initialBits)
        {
            // Extended code - need more bits
            uint mask = (1u << (32 - initialBits)) - 1;
            index = val + (int)((_word & mask) >> (32 - nBits));
            entry = table[index];
            val = entry.Value;
            nBits = initialBits + entry.Bits;
        }

        Consume(nBits);
        return val;
    }

    private uint FindChangingElement(byte[] line, uint x)
    {
        bool a;
        if (x == MINUS1)
        {
            // Before the line, the imaginary pixel is white
            a = false;
            x = 0;
        }
        else if (x < (uint)_width)
        {
            a = GetBit(line, (int)x);
            x++;
        }
        else
        {
            return (uint)_width;
        }

        // Search for first pixel that differs from 'a'
        while (x < (uint)_width)
        {
            if (GetBit(line, (int)x) != a)
                return x;
            x++;
        }
        return (uint)_width;
    }

    private uint FindChangingElementOfColor(byte[] line, uint x, int color)
    {
        x = FindChangingElement(line, x);
        if (x < (uint)_width && (GetBit(line, (int)x) ? 1 : 0) != color)
            x = FindChangingElement(line, x);
        return x;
    }

    private static bool GetBit(byte[] line, int x)
    {
        return (line[x >> 3] >> (7 - (x & 7)) & 1) != 0;
    }

    private static void SetBits(byte[] line, uint x0, uint x1)
    {
        if (x0 >= x1) return;

        var a0 = (int)(x0 >> 3);
        var a1 = (int)(x1 >> 3);
        var b0 = (int)(x0 & 7);
        var b1 = (int)(x1 & 7);

        if (a0 == a1)
        {
            line[a0] |= (byte)(LeftMask[b0] & RightMask[b1]);
        }
        else
        {
            line[a0] |= LeftMask[b0];
            for (int a = a0 + 1; a < a1; a++)
                line[a] = 0xFF;
            if (b1 != 0)
                line[a1] |= RightMask[b1];
        }
    }

    private static readonly byte[] LeftMask = [0xFF, 0x7F, 0x3F, 0x1F, 0x0F, 0x07, 0x03, 0x01];
    private static readonly byte[] RightMask = [0x00, 0x80, 0xC0, 0xE0, 0xF0, 0xF8, 0xFC, 0xFE];

    private readonly record struct MmrTableEntry(short Value, short Bits);

    // White run-length decode table (304 entries) - from jbig2dec
    // Indexed by top 8 bits of the bit stream
    private static readonly MmrTableEntry[] WhiteTable =
    [
        new(256,12), new(272,12), new(29,8), new(30,8), new(45,8), new(46,8), new(22,7), new(22,7),
        new(23,7), new(23,7), new(47,8), new(48,8), new(13,6), new(13,6), new(13,6), new(13,6),
        new(20,7), new(20,7), new(33,8), new(34,8), new(35,8), new(36,8), new(37,8), new(38,8),
        new(19,7), new(19,7), new(31,8), new(32,8), new(1,6), new(1,6), new(1,6), new(1,6),
        new(12,6), new(12,6), new(12,6), new(12,6), new(53,8), new(54,8), new(26,7), new(26,7),
        new(39,8), new(40,8), new(41,8), new(42,8), new(43,8), new(44,8), new(21,7), new(21,7),
        new(28,7), new(28,7), new(61,8), new(62,8), new(63,8), new(0,8), new(320,8), new(384,8),
        new(10,5), new(10,5), new(10,5), new(10,5), new(10,5), new(10,5), new(10,5), new(10,5),
        new(11,5), new(11,5), new(11,5), new(11,5), new(11,5), new(11,5), new(11,5), new(11,5),
        new(27,7), new(27,7), new(59,8), new(60,8), new(288,9), new(290,9), new(18,7), new(18,7),
        new(24,7), new(24,7), new(49,8), new(50,8), new(51,8), new(52,8), new(25,7), new(25,7),
        new(55,8), new(56,8), new(57,8), new(58,8), new(192,6), new(192,6), new(192,6), new(192,6),
        new(1664,6), new(1664,6), new(1664,6), new(1664,6), new(448,8), new(512,8), new(292,9), new(640,8),
        new(576,8), new(294,9), new(296,9), new(298,9), new(300,9), new(302,9), new(256,7), new(256,7),
        new(2,4), new(2,4), new(2,4), new(2,4), new(2,4), new(2,4), new(2,4), new(2,4),
        new(2,4), new(2,4), new(2,4), new(2,4), new(2,4), new(2,4), new(2,4), new(2,4),
        new(3,4), new(3,4), new(3,4), new(3,4), new(3,4), new(3,4), new(3,4), new(3,4),
        new(3,4), new(3,4), new(3,4), new(3,4), new(3,4), new(3,4), new(3,4), new(3,4),
        new(128,5), new(128,5), new(128,5), new(128,5), new(128,5), new(128,5), new(128,5), new(128,5),
        new(8,5), new(8,5), new(8,5), new(8,5), new(8,5), new(8,5), new(8,5), new(8,5),
        new(9,5), new(9,5), new(9,5), new(9,5), new(9,5), new(9,5), new(9,5), new(9,5),
        new(16,6), new(16,6), new(16,6), new(16,6), new(17,6), new(17,6), new(17,6), new(17,6),
        new(4,4), new(4,4), new(4,4), new(4,4), new(4,4), new(4,4), new(4,4), new(4,4),
        new(4,4), new(4,4), new(4,4), new(4,4), new(4,4), new(4,4), new(4,4), new(4,4),
        new(5,4), new(5,4), new(5,4), new(5,4), new(5,4), new(5,4), new(5,4), new(5,4),
        new(5,4), new(5,4), new(5,4), new(5,4), new(5,4), new(5,4), new(5,4), new(5,4),
        new(14,6), new(14,6), new(14,6), new(14,6), new(15,6), new(15,6), new(15,6), new(15,6),
        new(64,5), new(64,5), new(64,5), new(64,5), new(64,5), new(64,5), new(64,5), new(64,5),
        new(6,4), new(6,4), new(6,4), new(6,4), new(6,4), new(6,4), new(6,4), new(6,4),
        new(6,4), new(6,4), new(6,4), new(6,4), new(6,4), new(6,4), new(6,4), new(6,4),
        new(7,4), new(7,4), new(7,4), new(7,4), new(7,4), new(7,4), new(7,4), new(7,4),
        new(7,4), new(7,4), new(7,4), new(7,4), new(7,4), new(7,4), new(7,4), new(7,4),
        new(-2,3), new(-2,3), new(-1,0), new(-1,0), new(-1,0), new(-1,0), new(-1,0), new(-1,0),
        new(-1,0), new(-1,0), new(-1,0), new(-1,0), new(-1,0), new(-1,0), new(-1,0), new(-3,4),
        new(1792,3), new(1792,3), new(1984,4), new(2048,4), new(2112,4), new(2176,4), new(2240,4), new(2304,4),
        new(1856,3), new(1856,3), new(1920,3), new(1920,3), new(2368,4), new(2432,4), new(2496,4), new(2560,4),
        new(1472,1), new(1536,1), new(1600,1), new(1728,1), new(704,1), new(768,1), new(832,1), new(896,1),
        new(960,1), new(1024,1), new(1088,1), new(1152,1), new(1216,1), new(1280,1), new(1344,1), new(1408,1)
    ];

    // Black run-length decode table (320 entries) - from jbig2dec
    // Indexed by top 7 bits of the bit stream
    private static readonly MmrTableEntry[] BlackTable =
    [
        new(128,12), new(160,13), new(224,12), new(256,12), new(10,7), new(11,7), new(288,12), new(12,7),
        new(9,6), new(9,6), new(8,6), new(8,6), new(7,5), new(7,5), new(7,5), new(7,5),
        new(6,4), new(6,4), new(6,4), new(6,4), new(6,4), new(6,4), new(6,4), new(6,4),
        new(5,4), new(5,4), new(5,4), new(5,4), new(5,4), new(5,4), new(5,4), new(5,4),
        new(1,3), new(1,3), new(1,3), new(1,3), new(1,3), new(1,3), new(1,3), new(1,3),
        new(1,3), new(1,3), new(1,3), new(1,3), new(1,3), new(1,3), new(1,3), new(1,3),
        new(4,3), new(4,3), new(4,3), new(4,3), new(4,3), new(4,3), new(4,3), new(4,3),
        new(4,3), new(4,3), new(4,3), new(4,3), new(4,3), new(4,3), new(4,3), new(4,3),
        new(3,2), new(3,2), new(3,2), new(3,2), new(3,2), new(3,2), new(3,2), new(3,2),
        new(3,2), new(3,2), new(3,2), new(3,2), new(3,2), new(3,2), new(3,2), new(3,2),
        new(3,2), new(3,2), new(3,2), new(3,2), new(3,2), new(3,2), new(3,2), new(3,2),
        new(3,2), new(3,2), new(3,2), new(3,2), new(3,2), new(3,2), new(3,2), new(3,2),
        new(2,2), new(2,2), new(2,2), new(2,2), new(2,2), new(2,2), new(2,2), new(2,2),
        new(2,2), new(2,2), new(2,2), new(2,2), new(2,2), new(2,2), new(2,2), new(2,2),
        new(2,2), new(2,2), new(2,2), new(2,2), new(2,2), new(2,2), new(2,2), new(2,2),
        new(2,2), new(2,2), new(2,2), new(2,2), new(2,2), new(2,2), new(2,2), new(2,2),
        new(-2,4), new(-2,4), new(-1,0), new(-1,0), new(-1,0), new(-1,0), new(-1,0), new(-1,0),
        new(-1,0), new(-1,0), new(-1,0), new(-1,0), new(-1,0), new(-1,0), new(-1,0), new(-3,5),
        new(1792,4), new(1792,4), new(1984,5), new(2048,5), new(2112,5), new(2176,5), new(2240,5), new(2304,5),
        new(1856,4), new(1856,4), new(1920,4), new(1920,4), new(2368,5), new(2432,5), new(2496,5), new(2560,5),
        new(18,3), new(18,3), new(18,3), new(18,3), new(18,3), new(18,3), new(18,3), new(18,3),
        new(52,5), new(52,5), new(640,6), new(704,6), new(768,6), new(832,6), new(55,5), new(55,5),
        new(56,5), new(56,5), new(1280,6), new(1344,6), new(1408,6), new(1472,6), new(59,5), new(59,5),
        new(60,5), new(60,5), new(1536,6), new(1600,6), new(24,4), new(24,4), new(24,4), new(24,4),
        new(25,4), new(25,4), new(25,4), new(25,4), new(1664,6), new(1728,6), new(320,5), new(320,5),
        new(384,5), new(384,5), new(448,5), new(448,5), new(512,6), new(576,6), new(53,5), new(53,5),
        new(54,5), new(54,5), new(896,6), new(960,6), new(1024,6), new(1088,6), new(1152,6), new(1216,6),
        new(64,3), new(64,3), new(64,3), new(64,3), new(64,3), new(64,3), new(64,3), new(64,3),
        new(13,1), new(13,1), new(13,1), new(13,1), new(13,1), new(13,1), new(13,1), new(13,1),
        new(13,1), new(13,1), new(13,1), new(13,1), new(13,1), new(13,1), new(13,1), new(13,1),
        new(23,4), new(23,4), new(50,5), new(51,5), new(44,5), new(45,5), new(46,5), new(47,5),
        new(57,5), new(58,5), new(61,5), new(256,5), new(16,3), new(16,3), new(16,3), new(16,3),
        new(17,3), new(17,3), new(17,3), new(17,3), new(48,5), new(49,5), new(62,5), new(63,5),
        new(30,5), new(31,5), new(32,5), new(33,5), new(40,5), new(41,5), new(22,4), new(22,4),
        new(14,1), new(14,1), new(14,1), new(14,1), new(14,1), new(14,1), new(14,1), new(14,1),
        new(14,1), new(14,1), new(14,1), new(14,1), new(14,1), new(14,1), new(14,1), new(14,1),
        new(15,2), new(15,2), new(15,2), new(15,2), new(15,2), new(15,2), new(15,2), new(15,2),
        new(128,5), new(192,5), new(26,5), new(27,5), new(28,5), new(29,5), new(19,4), new(19,4),
        new(20,4), new(20,4), new(34,5), new(35,5), new(36,5), new(37,5), new(38,5), new(39,5),
        new(21,4), new(21,4), new(42,5), new(43,5), new(0,3), new(0,3), new(0,3), new(0,3)
    ];
}
