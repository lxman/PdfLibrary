using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// JBIG2 arithmetic decoder as defined in T.88 Annex E.
/// Uses the same MQ coder as JPEG2000.
/// </summary>
internal sealed class ArithmeticDecoder
{
    // Qe values for probability estimation (Table E.1 in T.88)
    private static readonly ushort[] QeValues =
    [
        0x5601, 0x3401, 0x1801, 0x0AC1, 0x0521, 0x0221, 0x5601, 0x5401,
        0x4801, 0x3801, 0x3001, 0x2401, 0x1C01, 0x1601, 0x5601, 0x5401,
        0x5101, 0x4801, 0x3801, 0x3401, 0x3001, 0x2801, 0x2401, 0x2201,
        0x1C01, 0x1801, 0x1601, 0x1401, 0x1201, 0x1101, 0x0AC1, 0x09C1,
        0x08A1, 0x0521, 0x0441, 0x02A1, 0x0221, 0x0141, 0x0111, 0x0085,
        0x0049, 0x0025, 0x0015, 0x0009, 0x0005, 0x0001, 0x5601
    ];

    // Next state after MPS (More Probable Symbol)
    private static readonly byte[] NextStateMps =
    [
        1,  2,  3,  4,  5, 38,  7,  8,  9, 10, 11, 12, 13, 29, 15, 16,
        17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
        33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 45, 46
    ];

    // Next state after LPS (Less Probable Symbol)
    private static readonly byte[] NextStateLps =
    [
        1,  6,  9, 12, 29, 33,  6, 14, 14, 14, 17, 18, 20, 21, 14, 14,
        15, 16, 17, 18, 19, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29,
        30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 46
    ];

    // Switch flag - whether to switch MPS sense after LPS
    private static readonly byte[] SwitchFlag =
    [
        1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
    ];

    private readonly byte[] _data;
    private int _bytePointer;
    private readonly int _dataEnd;
    private bool _markerFound;  // True if marker code encountered (0xFF followed by > 0x8F)

    // Word buffer like jbig2dec
    private uint _nextWord;
    private int _nextWordBytes;

    // Decoder state
    private uint _c;        // C register (code register)
    private uint _a;        // A register (interval)
    private int _ct;        // Counter for bit stuffing

    // Optional decoder for tracking operations
    private readonly Jbig2Decoder? _decoder;

    /// <summary>
    /// Context state - index into Qe table.
    /// </summary>
    internal sealed class Context
    {
        public byte State { get; set; }
        public byte Mps { get; set; }  // Most probable symbol (0 or 1)

        public Context()
        {
            State = 0;
            Mps = 0;
        }
    }

    private readonly int _startOffset;

    /// <summary>
    /// Gets the number of bytes consumed from the start of the data.
    /// </summary>
    public int BytesConsumed => _bytePointer - _startOffset;

    /// <summary>
    /// Gets internal state for debugging.
    /// </summary>
    public string DebugState => $"A=0x{_a:X}, C=0x{_c:X}, CT={_ct}, markerFound={_markerFound}, nextWord=0x{_nextWord:X8}, nextWordBytes={_nextWordBytes}";

    public ArithmeticDecoder(byte[] data, int offset, int length, Jbig2Decoder? decoder = null)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative");
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative");
        if (offset + length > data.Length)
            throw new ArgumentException($"Offset {offset} + length {length} exceeds data length {data.Length}");

        _data = data;
        _startOffset = offset;
        _bytePointer = offset;
        _dataEnd = offset + length;
        _decoder = decoder;
        Initialize();
    }

    public ArithmeticDecoder(byte[] data)
        : this(data, 0, data?.Length ?? 0)
    {
    }

    private int GetNextWord()
    {
        // Read up to 4 bytes into the word buffer, like jbig2dec
        if (_bytePointer >= _dataEnd) return 0;
        uint val = 0;
        var count = 0;
        for (var i = 0; i < 4 && _bytePointer + i < _dataEnd; i++)
        {
            val |= (uint)_data[_bytePointer + i] << (24 - i * 8);
            count++;
        }
        _nextWord = val;
        return count;
    }

    private void Initialize()
    {
        // Match jbig2dec's jbig2_arith_new exactly
        // Read first word (up to 4 bytes)
        _nextWordBytes = GetNextWord();
        _bytePointer += _nextWordBytes;

        // C = (~(next_word >> 8)) & 0xFF0000
        _c = (~(_nextWord >> 8)) & 0xFF0000;

        // First ByteIn
        ByteIn();

        _c <<= 7;
        _ct -= 7;
        _a = 0x8000;
    }

    /// <summary>
    /// Decodes a single bit using the given context.
    /// </summary>
    public int DecodeBit(Context cx)
    {
        if (cx == null)
            throw new ArgumentNullException(nameof(cx));

        // Track decode operations if decoder is available
        _decoder?.TrackDecodeOperation();

        int qeIndex = cx.State;

        // Validate state index
        if (qeIndex < 0 || qeIndex >= QeValues.Length)
            throw new Jbig2DataException($"Invalid context state index: {qeIndex}");

        uint qe = QeValues[qeIndex];
        int d;

        _a -= qe;
        uint cHigh = _c >> 16;

        if (cHigh < _a)
        {
            // MPS path
            if (_a < 0x8000)
            {
                d = (_a < qe) ? 1 - cx.Mps : cx.Mps;
                if (_a < qe)
                {
                    // Conditional LPS exchange
                    if (SwitchFlag[qeIndex] != 0)
                        cx.Mps = (byte)(1 - cx.Mps);
                    cx.State = NextStateLps[qeIndex];
                }
                else
                {
                    cx.State = NextStateMps[qeIndex];
                }
                Renormalize();
            }
            else
            {
                d = cx.Mps;
            }
        }
        else
        {
            // LPS path
            _c -= _a << 16;
            d = (_a < qe) ? cx.Mps : 1 - cx.Mps;
            if (_a >= qe)
            {
                // Conditional LPS exchange
                if (SwitchFlag[qeIndex] != 0)
                    cx.Mps = (byte)(1 - cx.Mps);
                cx.State = NextStateLps[qeIndex];
            }
            else
            {
                cx.State = NextStateMps[qeIndex];
            }
            _a = qe;
            Renormalize();
        }

        return d;
    }

    /// <summary>
    /// Decodes an integer using the JBIG2 integer arithmetic decoding procedure.
    /// T.88 Annex A.2
    /// </summary>
    public int DecodeInt(Context[] contexts)
    {
        if (contexts == null)
            throw new ArgumentNullException(nameof(contexts));
        if (contexts.Length < 512)
            throw new ArgumentException("Integer decoding requires 512 contexts", nameof(contexts));

        // NOTE: Do NOT return OOB just because marker was found!
        // The marker signals "no more bytes to read", but the bits already in C
        // are still valid and should be decoded. ByteIn() will handle the marker
        // by setting CT=8 and not fetching more bytes.
        // The decode should produce a natural OOB (s=1, v=0) if appropriate.

        var prev = 1;
        int s = DecodeIntBit(contexts, ref prev);

        // Decode the value class - trace each decision bit
        int v;
        int d1 = DecodeIntBit(contexts, ref prev);
        if (d1 == 0)
        {
            // 2-bit value
            v = DecodeIntBits(contexts, ref prev, 2);
        }
        else
        {
            int d2 = DecodeIntBit(contexts, ref prev);
            if (d2 == 0)
            {
                // 4-bit value + 4
                v = DecodeIntBits(contexts, ref prev, 4) + 4;
            }
            else
            {
                int d3 = DecodeIntBit(contexts, ref prev);
                if (d3 == 0)
                {
                    // 6-bit value + 20
                    v = DecodeIntBits(contexts, ref prev, 6) + 20;
                }
                else
                {
                    int d4 = DecodeIntBit(contexts, ref prev);
                    if (d4 == 0)
                    {
                        // 8-bit value + 84
                        v = DecodeIntBits(contexts, ref prev, 8) + 84;
                    }
                    else
                    {
                        int d5 = DecodeIntBit(contexts, ref prev);
                        if (d5 == 0)
                        {
                            // 12-bit value + 340
                            v = DecodeIntBits(contexts, ref prev, 12) + 340;
                        }
                        else
                        {
                            // OOB - all 5 decision bits were 1
                            return int.MinValue;
                        }
                    }
                }
            }
        }

        // NOTE: Don't abort if marker was found during THIS decode.
        // The marker signals end-of-data, but the current integer decode should
        // complete with whatever bits are already in C. Only the NEXT decode
        // (which checks _markerFound at the start) will return OOB.

        if (s == 0)
        {
            return v;
        }

        if (v == 0)
        {
            return int.MinValue; // OOB (out of band)
        }

        return -v;
    }

    private int DecodeIntBit(Context[] contexts, ref int prev)
    {
        int bit = DecodeBit(contexts[prev]);
        prev = ((prev << 1) | bit) & 0x1FF; // 9-bit context window
        return bit;
    }

    private int DecodeIntBits(Context[] contexts, ref int prev, int count)
    {
        // T.88 Annex A.2 value bit decoding
        // The context index formula for value bits differs from decision bits:
        // PREV = ((PREV << 1) & 511) | (PREV & 256) | D
        // This preserves bit 8 when PREV >= 256
        var value = 0;
        for (var i = 0; i < count; i++)
        {
            int bit = DecodeBit(contexts[prev]);
            prev = ((prev << 1) & 0x1FF) | (prev & 0x100) | bit;
            value = (value << 1) | bit;
        }
        return value;
    }

    private void Renormalize()
    {
        // RENORMD procedure
        do
        {
            if (_ct == 0)
                ByteIn();
            _a <<= 1;
            _c <<= 1;
            _ct--;
        } while (_a < 0x8000);
    }

    private void ByteIn()
    {
        // Match jbig2dec's jbig2_arith_bytein exactly
        // If marker already found, just set CT=8
        if (_markerFound)
        {
            _ct = 8;
            return;
        }

        var B = (byte)((_nextWord >> 24) & 0xFF);

        if (B == 0xFF)
        {
            // FF byte handling - match jbig2dec exactly
            byte B1;

            if (_nextWordBytes <= 1)
            {
                // Need to read more data to get B1
                _nextWordBytes = GetNextWord();
                _bytePointer += _nextWordBytes;
                if (_nextWordBytes == 0)
                {
                    // EOF after FF - pad with 0xFF90 marker
                    _nextWord = 0xFF900000;
                    _nextWordBytes = 2;
                    _c += 0xFF00;
                    _ct = 8;
                    _markerFound = true;
                    return;
                }

                // B1 is the first byte of the new word (position 24)
                B1 = (byte)((_nextWord >> 24) & 0xFF);

                if (B1 > 0x8F)
                {
                    // Marker detected
                    _ct = 8;
                    // Restore 0xFF to buffer (like jbig2dec)
                    _nextWord = 0xFF000000 | (_nextWord >> 8);
                    _nextWordBytes = 2;
                    _bytePointer--;
                    _markerFound = true;
                }
                else
                {
                    // Stuffed byte
                    var add = (uint)(0xFE00 - (B1 << 9));
                    _c += add;
                    _ct = 7;
                }
            }
            else
            {
                // B1 is the second byte in current word (position 16)
                B1 = (byte)((_nextWord >> 16) & 0xFF);

                if (B1 > 0x8F)
                {
                    // Marker detected
                    _ct = 8;
                    _markerFound = true;
                }
                else
                {
                    // Stuffed byte - consume the B1 byte from buffer
                    _nextWordBytes--;
                    _nextWord <<= 8;
                    var add = (uint)(0xFE00 - (B1 << 9));
                    _c += add;
                    _ct = 7;
                }
            }
        }
        else
        {
            // Normal byte handling
            _nextWord <<= 8;
            _nextWordBytes--;

            if (_nextWordBytes == 0)
            {
                // Need to read more data
                _nextWordBytes = GetNextWord();
                _bytePointer += _nextWordBytes;
                if (_nextWordBytes == 0)
                {
                    // EOF during normal read - add 0xFF00 (pad with FF)
                    _c += 0xFF00;
                    _ct = 8;
                    _markerFound = true;
                    return;
                }
            }

            // Read new B after shift and add its complement
            B = (byte)((_nextWord >> 24) & 0xFF);
            var add = (uint)(0xFF00 - (B << 8));
            _c += add;
            _ct = 8;
        }
    }

    /// <summary>
    /// Creates an array of contexts for integer decoding.
    /// </summary>
    public static Context[] CreateIntContexts()
    {
        var contexts = new Context[512];
        for (var i = 0; i < 512; i++)
            contexts[i] = new Context();
        return contexts;
    }
}
