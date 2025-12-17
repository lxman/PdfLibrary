using System;

namespace ImageLibrary.Jpeg;

/// <summary>
/// Reads bits from JPEG entropy-coded data, handling byte stuffing (0xFF00 -> 0xFF).
/// </summary>
internal class BitReader
{
    private readonly byte[] _data;
    private readonly int _startOffset;
    private readonly int _endOffset;

    private int _bytePosition;
    private int _bitBuffer;
    private int _bitsInBuffer;

    /// <summary>
    /// Creates a new BitReader for the given data range.
    /// </summary>
    public BitReader(byte[] data, int startOffset, int length)
    {
        _data = data;
        _startOffset = startOffset;
        _endOffset = startOffset + length;
        _bytePosition = startOffset;
        _bitBuffer = 0;
        _bitsInBuffer = 0;
    }

    /// <summary>
    /// Gets the current byte position in the data.
    /// </summary>
    public int BytePosition => _bytePosition;

    /// <summary>
    /// Gets whether we've reached the end of the data.
    /// </summary>
    public bool IsAtEnd => _bytePosition >= _endOffset && _bitsInBuffer == 0;

    /// <summary>
    /// Ensures we have at least the specified number of bits in the buffer.
    /// </summary>
    private void EnsureBits(int count)
    {
        while (_bitsInBuffer < count && _bytePosition < _endOffset)
        {
            byte b = ReadNextByte();
            _bitBuffer = (_bitBuffer << 8) | b;
            _bitsInBuffer += 8;
        }
    }

    /// <summary>
    /// Reads the next byte, handling byte stuffing.
    /// In JPEG, 0xFF is followed by 0x00 for stuffing (the 0x00 is discarded).
    /// </summary>
    private byte ReadNextByte()
    {
        if (_bytePosition >= _endOffset)
        {
            return 0; // Pad with zeros at end
        }

        byte b = _data[_bytePosition++];

        // Handle byte stuffing: 0xFF 0x00 -> 0xFF
        if (b == 0xFF && _bytePosition < _endOffset)
        {
            byte next = _data[_bytePosition];
            if (next == 0x00)
            {
                _bytePosition++; // Skip the stuffed 0x00
            }
            // If next is not 0x00, it's a marker - but we shouldn't encounter
            // markers in the entropy data if LocateEntropyData worked correctly
        }

        return b;
    }

    /// <summary>
    /// Peeks at the next N bits without consuming them.
    /// </summary>
    public int PeekBits(int count)
    {
        EnsureBits(count);

        if (_bitsInBuffer < count)
        {
            // Not enough bits - pad with zeros
            return _bitBuffer << (count - _bitsInBuffer);
        }

        return (_bitBuffer >> (_bitsInBuffer - count)) & ((1 << count) - 1);
    }

    /// <summary>
    /// Skips the specified number of bits.
    /// </summary>
    public void SkipBits(int count)
    {
        if (count <= _bitsInBuffer)
        {
            _bitsInBuffer -= count;
            _bitBuffer &= (1 << _bitsInBuffer) - 1;
        }
        else
        {
            // Need to read more bytes
            count -= _bitsInBuffer;
            _bitsInBuffer = 0;
            _bitBuffer = 0;

            // Skip whole bytes
            while (count >= 8)
            {
                ReadNextByte();
                count -= 8;
            }

            // Handle remaining bits
            if (count > 0)
            {
                EnsureBits(count);
                _bitsInBuffer -= count;
                _bitBuffer &= (1 << _bitsInBuffer) - 1;
            }
        }
    }

    /// <summary>
    /// Reads a single bit.
    /// </summary>
    public int ReadBit()
    {
        EnsureBits(1);

        if (_bitsInBuffer == 0)
        {
            return 0;
        }

        _bitsInBuffer--;
        int bit = (_bitBuffer >> _bitsInBuffer) & 1;
        _bitBuffer &= (1 << _bitsInBuffer) - 1;
        return bit;
    }

    /// <summary>
    /// Reads the specified number of bits as an unsigned integer.
    /// </summary>
    public int ReadBits(int count)
    {
        if (count == 0)
        {
            return 0;
        }

        EnsureBits(count);

        int available = Math.Min(count, _bitsInBuffer);
        _bitsInBuffer -= available;
        int result = (_bitBuffer >> _bitsInBuffer) & ((1 << available) - 1);
        _bitBuffer &= (1 << _bitsInBuffer) - 1;

        // If we didn't get enough bits, pad with zeros
        if (available < count)
        {
            result <<= (count - available);
        }

        return result;
    }

    /// <summary>
    /// Reads a signed value using JPEG's sign extension.
    /// Used for DC and AC coefficient decoding.
    /// </summary>
    public int ReadSignedValue(int bits)
    {
        if (bits == 0)
        {
            return 0;
        }

        int value = ReadBits(bits);

        // JPEG sign extension: if high bit is 0, value is negative
        // For 'bits' bits, if value < 2^(bits-1), subtract 2^bits - 1
        int threshold = 1 << (bits - 1);
        if (value < threshold)
        {
            value -= (1 << bits) - 1;
        }

        return value;
    }

    /// <summary>
    /// Resets the bit reader to the start of the data.
    /// </summary>
    public void Reset()
    {
        _bytePosition = _startOffset;
        _bitBuffer = 0;
        _bitsInBuffer = 0;
    }
}
