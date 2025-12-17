using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Reads bits and bytes from a stream or buffer.
/// Big-endian bit order (MSB first) as used in JBIG2.
/// </summary>
internal sealed class BitReader
{
    private readonly byte[] _data;
    private int _bytePosition;
    private int _bitPosition; // 0-7, where 0 is MSB

    public BitReader(byte[] data)
    {
        _data = data;
        _bytePosition = 0;
        _bitPosition = 0;
    }

    public BitReader(byte[] data, int offset)
    {
        _data = data;
        _bytePosition = offset;
        _bitPosition = 0;
    }

    /// <summary>
    /// Current byte position in the stream.
    /// </summary>
    public int BytePosition => _bytePosition;

    /// <summary>
    /// Current bit position within the current byte (0 = MSB).
    /// </summary>
    public int BitPosition => _bitPosition;

    /// <summary>
    /// Whether we've reached the end of the data.
    /// </summary>
    public bool IsAtEnd => _bytePosition >= _data.Length;

    /// <summary>
    /// Remaining bytes available.
    /// </summary>
    public int RemainingBytes => Math.Max(0, _data.Length - _bytePosition);

    /// <summary>
    /// Reads a single bit.
    /// </summary>
    public int ReadBit()
    {
        if (_bytePosition >= _data.Length)
            throw new InvalidOperationException("Attempted to read past end of data");

        int bit = (_data[_bytePosition] >> (7 - _bitPosition)) & 1;

        _bitPosition++;
        if (_bitPosition >= 8)
        {
            _bitPosition = 0;
            _bytePosition++;
        }

        return bit;
    }

    /// <summary>
    /// Reads multiple bits (up to 32).
    /// </summary>
    public uint ReadBits(int count)
    {
        if (count < 0 || count > 32)
            throw new ArgumentOutOfRangeException(nameof(count));

        uint result = 0;
        for (var i = 0; i < count; i++)
        {
            result = (result << 1) | (uint)ReadBit();
        }
        return result;
    }

    /// <summary>
    /// Reads a single byte (aligns to byte boundary first if needed).
    /// </summary>
    public byte ReadByte()
    {
        AlignToByte();
        if (_bytePosition >= _data.Length)
            throw new InvalidOperationException("Attempted to read past end of data");
        return _data[_bytePosition++];
    }

    /// <summary>
    /// Reads multiple bytes.
    /// </summary>
    public byte[] ReadBytes(int count)
    {
        AlignToByte();
        if (_bytePosition + count > _data.Length)
            throw new InvalidOperationException("Attempted to read past end of data");

        var result = new byte[count];
        Array.Copy(_data, _bytePosition, result, 0, count);
        _bytePosition += count;
        return result;
    }

    /// <summary>
    /// Reads a 16-bit big-endian unsigned integer.
    /// </summary>
    public ushort ReadUInt16BigEndian()
    {
        byte b0 = ReadByte();
        byte b1 = ReadByte();
        return (ushort)((b0 << 8) | b1);
    }

    /// <summary>
    /// Reads a 32-bit big-endian unsigned integer.
    /// </summary>
    public uint ReadUInt32BigEndian()
    {
        byte b0 = ReadByte();
        byte b1 = ReadByte();
        byte b2 = ReadByte();
        byte b3 = ReadByte();
        return (uint)((b0 << 24) | (b1 << 16) | (b2 << 8) | b3);
    }

    /// <summary>
    /// Reads a 32-bit big-endian signed integer.
    /// </summary>
    public int ReadInt32BigEndian()
    {
        return (int)ReadUInt32BigEndian();
    }

    /// <summary>
    /// Aligns the reader to the next byte boundary.
    /// </summary>
    public void AlignToByte()
    {
        if (_bitPosition != 0)
        {
            _bitPosition = 0;
            _bytePosition++;
        }
    }

    /// <summary>
    /// Skips the specified number of bytes.
    /// </summary>
    public void SkipBytes(int count)
    {
        AlignToByte();
        _bytePosition += count;
    }

    /// <summary>
    /// Seeks to an absolute byte position.
    /// </summary>
    public void Seek(int bytePosition)
    {
        _bytePosition = bytePosition;
        _bitPosition = 0;
    }

    /// <summary>
    /// Peeks at the next byte without advancing position.
    /// </summary>
    public byte PeekByte()
    {
        if (_bitPosition != 0)
            throw new InvalidOperationException("Cannot peek when not byte-aligned");
        if (_bytePosition >= _data.Length)
            throw new InvalidOperationException("Attempted to peek past end of data");
        return _data[_bytePosition];
    }
}
