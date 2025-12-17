using System;

namespace ImageLibrary.Jp2.Tier2;

/// <summary>
/// Reads bits from a byte array for packet header parsing.
/// Handles the JPEG2000 bit-stuffing where 0xFF is followed by 0x00.
/// </summary>
internal class BitReader
{
    private readonly byte[] _data;
    private int _bytePosition;
    private int _bitPosition;
    private int _currentByte;
    private bool _lastWasFF;

    public BitReader(byte[] data, int offset = 0)
    {
        _data = data;
        _bytePosition = offset;
        _bitPosition = 8; // Forces read on first bit request
        _currentByte = 0;
        _lastWasFF = false;
    }

    /// <summary>
    /// Gets the current byte position in the data.
    /// </summary>
    public int BytePosition => _bytePosition;

    /// <summary>
    /// Gets the current bit position within the current byte (0-7).
    /// </summary>
    public int BitPosition => _bitPosition;

    /// <summary>
    /// Gets the total number of bytes available.
    /// </summary>
    public int Length => _data.Length;

    /// <summary>
    /// Returns true if there is more data to read.
    /// </summary>
    public bool HasMoreData => _bytePosition < _data.Length || _bitPosition < 8;

    /// <summary>
    /// Reads a single bit from the stream.
    /// </summary>
    public int ReadBit()
    {
        if (_bitPosition >= 8)
        {
            if (_bytePosition >= _data.Length)
            {
                throw new Jp2Exception("Unexpected end of packet data");
            }

            _currentByte = _data[_bytePosition++];

            // Handle bit-stuffing: after 0xFF, next byte has only 7 data bits
            if (_lastWasFF)
            {
                _bitPosition = 1; // Skip the stuffed bit
            }
            else
            {
                _bitPosition = 0;
            }

            _lastWasFF = (_currentByte == 0xFF);
        }

        int bit = (_currentByte >> (7 - _bitPosition)) & 1;
        _bitPosition++;
        return bit;
    }

    /// <summary>
    /// Reads multiple bits from the stream.
    /// </summary>
    public int ReadBits(int count)
    {
        var value = 0;
        for (var i = 0; i < count; i++)
        {
            value = (value << 1) | ReadBit();
        }
        return value;
    }

    /// <summary>
    /// Aligns to the next byte boundary.
    /// </summary>
    public void AlignToByte()
    {
        if (_bitPosition > 0 && _bitPosition < 8)
        {
            _bitPosition = 8;
            _lastWasFF = false;
        }
    }

    /// <summary>
    /// Reads raw bytes (after byte alignment).
    /// </summary>
    public byte[] ReadBytes(int count)
    {
        AlignToByte();

        if (_bytePosition + count > _data.Length)
        {
            count = _data.Length - _bytePosition;
        }

        var result = new byte[count];
        Array.Copy(_data, _bytePosition, result, 0, count);
        _bytePosition += count;
        _bitPosition = 8;
        _lastWasFF = false;
        return result;
    }

    /// <summary>
    /// Gets the number of bytes consumed so far.
    /// </summary>
    public int BytesConsumed => _bitPosition >= 8 ? _bytePosition : _bytePosition - 1;

    /// <summary>
    /// Checks for a marker (0xFF followed by non-zero byte).
    /// </summary>
    public bool PeekMarker()
    {
        AlignToByte();
        if (_bytePosition + 1 >= _data.Length)
            return false;

        return _data[_bytePosition] == 0xFF && _data[_bytePosition + 1] != 0x00;
    }

    /// <summary>
    /// Skips to the specified byte position.
    /// </summary>
    public void Seek(int bytePosition)
    {
        _bytePosition = bytePosition;
        _bitPosition = 8;
        _lastWasFF = false;
    }
}