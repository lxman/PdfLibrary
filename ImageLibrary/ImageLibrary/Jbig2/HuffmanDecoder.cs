using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Decodes Huffman-coded values from a bit stream.
/// T.88 Annex B describes the Huffman coding procedures.
/// </summary>
internal sealed class HuffmanDecoder
{
    private readonly byte[] _data;
    private readonly int _startOffset;
    private readonly int _endOffset;
    private readonly Jbig2DecoderOptions _options;

    // Double-buffered word stream for efficient bit reading
    private uint _thisWord;
    private uint _nextWord;
    private int _offsetBits;  // Bit offset within current word (0-31)
    private int _offset;      // Byte offset of current word
    private long _decodeOperations;

    /// <summary>
    /// Special value returned to indicate out-of-band.
    /// </summary>
    public const int OOB = int.MinValue;

    public HuffmanDecoder(byte[] data, int offset, int length)
        : this(data, offset, length, null)
    {
    }

    public HuffmanDecoder(byte[] data, int offset, int length, Jbig2DecoderOptions? options)
    {
        _data = data;
        _startOffset = offset;
        _endOffset = offset + length;
        _options = options ?? Jbig2DecoderOptions.Default;
        _offset = offset;
        _offsetBits = 0;
        _decodeOperations = 0;

        // Initialize the double-buffered words
        _thisWord = GetWord(_offset);
        _nextWord = GetWord(_offset + 4);
    }

    /// <summary>
    /// Current byte position in the stream.
    /// </summary>
    public int BytePosition => _offset + (_offsetBits >> 3);

    /// <summary>
    /// Number of bytes remaining.
    /// </summary>
    public int RemainingBytes => _endOffset - BytePosition;

    /// <summary>
    /// Gets the underlying data array.
    /// </summary>
    public byte[] GetData() => _data;

    /// <summary>
    /// Read a 32-bit big-endian word from the specified offset.
    /// Returns 0 for bytes beyond the end of data.
    /// </summary>
    private uint GetWord(int offset)
    {
        uint result = 0;
        for (var i = 0; i < 4; i++)
        {
            result <<= 8;
            int pos = offset + i;
            if (pos >= _startOffset && pos < _endOffset)
                result |= _data[pos];
        }
        return result;
    }

    /// <summary>
    /// Refill the word buffers after consuming bits.
    /// </summary>
    private void Refill()
    {
        if (_offsetBits >= 32)
        {
            _thisWord = _nextWord;
            _offset += 4;
            _nextWord = GetWord(_offset + 4);
            _offsetBits -= 32;

            if (_offsetBits > 0)
            {
                _thisWord = (_thisWord << _offsetBits) | (_nextWord >> (32 - _offsetBits));
            }
        }
    }

    /// <summary>
    /// Read raw bits from the stream without Huffman decoding.
    /// </summary>
    public int ReadBits(int count)
    {
        if (count <= 0 || count > 32)
            throw new ArgumentOutOfRangeException(nameof(count));

        _decodeOperations += count;
        if (_decodeOperations > _options.MaxDecodeOperations)
            throw new Jbig2ResourceException($"Huffman decode operation limit exceeded ({_options.MaxDecodeOperations})");

        uint result = _thisWord >> (32 - count);
        _offsetBits += count;

        if (_offsetBits >= 32)
        {
            _offset += 4;
            _offsetBits -= 32;
            _thisWord = _nextWord;
            _nextWord = GetWord(_offset + 4);

            if (_offsetBits > 0)
            {
                _thisWord = (_thisWord << _offsetBits) | (_nextWord >> (32 - _offsetBits));
            }
        }
        else
        {
            _thisWord = (_thisWord << count) | (_nextWord >> (32 - _offsetBits));
        }

        return (int)result;
    }

    /// <summary>
    /// Skip to the next byte boundary.
    /// </summary>
    public void SkipToByteAlign()
    {
        int bits = _offsetBits & 7;
        if (bits != 0)
        {
            bits = 8 - bits;
            _offsetBits += bits;
            _thisWord = (_thisWord << bits) | (_nextWord >> (32 - _offsetBits));
        }

        Refill();
    }

    /// <summary>
    /// Advance by a specified number of bytes.
    /// </summary>
    public void Advance(int bytes)
    {
        _offset += bytes & ~3;
        _offsetBits += (bytes & 3) << 3;

        if (_offsetBits >= 32)
        {
            _offset += 4;
            _offsetBits -= 32;
        }

        _thisWord = GetWord(_offset);
        _nextWord = GetWord(_offset + 4);

        if (_offsetBits > 0)
        {
            _thisWord = (_thisWord << _offsetBits) | (_nextWord >> (32 - _offsetBits));
        }
    }

    /// <summary>
    /// Decode a value using the specified Huffman table.
    /// Returns OOB (int.MinValue) if an out-of-band value is decoded.
    /// </summary>
    public int Decode(HuffmanTable table)
    {
        // Look up entry using the top bits of thisWord
        int logTableSize = table.LogTableSize;
        int index = logTableSize > 0 ? (int)(_thisWord >> (32 - logTableSize)) : 0;
        ref HuffmanEntry entry = ref table.Entries[index];

        if (entry.PrefixLength == 0xFF)
            throw new Jbig2DataException("Encountered unpopulated Huffman table entry");

        int prefLen = entry.PrefixLength;
        int rangeLen = entry.RangeLength;
        HuffmanFlags flags = entry.Flags;
        int result = entry.RangeLow;

        // Track decode operations (prefix + range bits)
        _decodeOperations += prefLen + rangeLen;
        if (_decodeOperations > _options.MaxDecodeOperations)
            throw new Jbig2ResourceException($"Huffman decode operation limit exceeded ({_options.MaxDecodeOperations})");

        // Consume the prefix bits
        _offsetBits += prefLen;

        if (_offsetBits >= 32)
        {
            _offset += 4;
            _offsetBits -= 32;
            _thisWord = _nextWord;
            _nextWord = GetWord(_offset + 4);
            prefLen = _offsetBits;
        }

        if (prefLen > 0)
        {
            _thisWord = (_thisWord << prefLen) | (_nextWord >> (32 - _offsetBits));
        }

        // Check for OOB
        if ((flags & HuffmanFlags.IsOOB) != 0)
        {
            return OOB;
        }

        // Read additional range bits if needed
        if (rangeLen > 0)
        {
            var htOffset = (int)(_thisWord >> (32 - rangeLen));

            if ((flags & HuffmanFlags.IsLow) != 0)
                result -= htOffset;
            else
                result += htOffset;

            _offsetBits += rangeLen;

            if (_offsetBits >= 32)
            {
                _offset += 4;
                _offsetBits -= 32;
                _thisWord = _nextWord;
                _nextWord = GetWord(_offset + 4);
                rangeLen = _offsetBits;
            }

            if (rangeLen > 0)
            {
                _thisWord = (_thisWord << rangeLen) | (_nextWord >> (32 - _offsetBits));
            }
        }

        return result;
    }

    /// <summary>
    /// Debug state string.
    /// </summary>
    public string DebugState => $"offset={_offset}, bits={_offsetBits}, thisWord=0x{_thisWord:X8}";
}
