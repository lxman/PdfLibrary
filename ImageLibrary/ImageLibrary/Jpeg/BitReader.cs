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
    /// Reads the next byte, handling byte stuffing and RST markers.
    /// In JPEG, 0xFF is followed by 0x00 for stuffing (the 0x00 is discarded).
    /// RST markers (0xFF 0xD0-0xD7) should be skipped entirely.
    /// </summary>
    private byte ReadNextByte()
    {
        if (_bytePosition >= _endOffset)
        {
            return 0; // Pad with zeros at end
        }

        byte b = _data[_bytePosition++];

        // Handle byte stuffing and markers
        if (b == 0xFF && _bytePosition < _endOffset)
        {
            byte next = _data[_bytePosition];
            if (next == 0x00)
            {
                // Byte stuffing: 0xFF 0x00 -> 0xFF
                _bytePosition++; // Skip the stuffed 0x00
            }
            else if (next >= 0xD0 && next <= 0xD7)
            {
                // RST marker: skip both bytes and read the next byte
                // The decoder handles byte-alignment and DC predictor reset separately
                _bytePosition++; // Skip the marker byte
                return ReadNextByte(); // Recursively read next byte
            }
            // Other markers shouldn't appear in entropy data
        }

        return b;
    }

    /// <summary>
    /// Peeks at the next N bits without consuming them.
    /// </summary>
    public int PeekBits(int count)
    {
        EnsureBits(count);

        int result;
        if (_bitsInBuffer < count)
        {
            // Not enough bits - pad with zeros
            result = _bitBuffer << (count - _bitsInBuffer);
        }
        else
        {
            result = (_bitBuffer >> (_bitsInBuffer - count)) & ((1 << count) - 1);
        }

        return result;
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
    /// Advances to the next byte boundary, discarding any remaining bits.
    /// Used after restart markers to byte-align the bit stream.
    /// </summary>
    public void AdvanceAlignByte()
    {
        // If we have buffered bits, we need to adjust byte position
        // to account for the complete bytes in the buffer that we're discarding
        if (_bitsInBuffer > 0)
        {
            // Calculate how many complete bytes are in the buffer
            int bufferedBytes = _bitsInBuffer / 8;

            // Rewind by the number of complete buffered bytes
            // (partial bits from the last byte are discarded to align)
            _bytePosition -= bufferedBytes;
        }

        _bitBuffer = 0;
        _bitsInBuffer = 0;
    }

    /// <summary>
    /// Skips the next restart marker (2 bytes: 0xFF 0xDN).
    /// Should be called after AdvanceAlignByte() when a restart marker is expected.
    /// </summary>
    public void SkipRestartMarker()
    {
        if (_bytePosition + 1 < _endOffset)
        {
            byte b1 = _data[_bytePosition];
            byte b2 = _data[_bytePosition + 1];

            // Verify it's actually a restart marker (0xFF 0xD0-0xD7)
            if (b1 == 0xFF && b2 >= 0xD0 && b2 <= 0xD7)
            {
                _bytePosition += 2; // Skip the 2-byte marker
            }
        }
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
