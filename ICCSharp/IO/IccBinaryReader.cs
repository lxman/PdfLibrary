using System;
using System.Buffers.Binary;
using System.Text;

namespace ICCSharp.IO;

/// <summary>
/// Big-endian primitive reader for ICC profile data (ICC.1:2010 §4).
/// Stateful cursor over a <see cref="ReadOnlyMemory{Byte}"/>; bounds-checked.
/// </summary>
public sealed class IccBinaryReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private int _position;

    public IccBinaryReader(ReadOnlyMemory<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public IccBinaryReader(byte[] data) : this(new ReadOnlyMemory<byte>(data ?? throw new ArgumentNullException(nameof(data))))
    {
    }

    public int Position
    {
        get => _position;
        set
        {
            if ((uint)value > (uint)_data.Length)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Position outside data.");
            _position = value;
        }
    }

    public int Length => _data.Length;
    public int Remaining => _data.Length - _position;

    public void Skip(int count)
    {
        EnsureAvailable(count);
        _position += count;
    }

    public byte ReadUInt8()
    {
        EnsureAvailable(1);
        return _data.Span[_position++];
    }

    public sbyte ReadInt8() => (sbyte)ReadUInt8();

    public ushort ReadUInt16()
    {
        EnsureAvailable(2);
        ushort v = BinaryPrimitives.ReadUInt16BigEndian(_data.Span.Slice(_position, 2));
        _position += 2;
        return v;
    }

    public short ReadInt16()
    {
        EnsureAvailable(2);
        short v = BinaryPrimitives.ReadInt16BigEndian(_data.Span.Slice(_position, 2));
        _position += 2;
        return v;
    }

    public uint ReadUInt32()
    {
        EnsureAvailable(4);
        uint v = BinaryPrimitives.ReadUInt32BigEndian(_data.Span.Slice(_position, 4));
        _position += 4;
        return v;
    }

    public int ReadInt32()
    {
        EnsureAvailable(4);
        int v = BinaryPrimitives.ReadInt32BigEndian(_data.Span.Slice(_position, 4));
        _position += 4;
        return v;
    }

    public ulong ReadUInt64()
    {
        EnsureAvailable(8);
        ulong v = BinaryPrimitives.ReadUInt64BigEndian(_data.Span.Slice(_position, 8));
        _position += 8;
        return v;
    }

    public long ReadInt64()
    {
        EnsureAvailable(8);
        long v = BinaryPrimitives.ReadInt64BigEndian(_data.Span.Slice(_position, 8));
        _position += 8;
        return v;
    }

    public float ReadFloat32()
    {
        // netstandard2.1 lacks ReadSingleBigEndian; reinterpret the BE uint32.
        uint bits = ReadUInt32();
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }

    /// <summary>ICC.1:2010 §4.12 s15Fixed16Number — signed 16.16, range [-32768, 32767.9999847…].</summary>
    public double ReadS15Fixed16() => ReadInt32() / 65536.0;

    /// <summary>ICC.1:2010 §4.13 u16Fixed16Number — unsigned 16.16.</summary>
    public double ReadU16Fixed16() => ReadUInt32() / 65536.0;

    /// <summary>ICC.1:2010 §4.15 u1Fixed15Number — unsigned 1.15. Range [0, 1.9999694…].</summary>
    public double ReadU1Fixed15() => ReadUInt16() / 32768.0;

    /// <summary>ICC.1:2010 §4.16 u8Fixed8Number — unsigned 8.8.</summary>
    public double ReadU8Fixed8() => ReadUInt16() / 256.0;

    /// <summary>ICC.1:2010 §4.4 four-byte signature.</summary>
    public IccSignature ReadSignature() => new(ReadUInt32());

    /// <summary>ICC.1:2010 §4.14 XYZNumber — three s15Fixed16.</summary>
    public XyzNumber ReadXyz()
    {
        double x = ReadS15Fixed16();
        double y = ReadS15Fixed16();
        double z = ReadS15Fixed16();
        return new XyzNumber(x, y, z);
    }

    /// <summary>ICC.1:2010 §4.2 dateTimeNumber — six uInt16 fields.</summary>
    public IccDateTime ReadDateTime()
    {
        ushort y = ReadUInt16();
        ushort mo = ReadUInt16();
        ushort d = ReadUInt16();
        ushort h = ReadUInt16();
        ushort mi = ReadUInt16();
        ushort s = ReadUInt16();
        return new IccDateTime(y, mo, d, h, mi, s);
    }

    /// <summary>ICC.1:2010 §4.10 positionNumber — offset (uInt32) + size (uInt32).</summary>
    public PositionNumber ReadPosition()
    {
        uint off = ReadUInt32();
        uint size = ReadUInt32();
        return new PositionNumber(off, size);
    }

    /// <summary>ICC.1:2010 §4.11 response16Number — deviceCode + reserved + s15Fixed16 measurement.</summary>
    public Response16Number ReadResponse16()
    {
        ushort code = ReadUInt16();
        ushort reserved = ReadUInt16();
        double measure = ReadS15Fixed16();
        return new Response16Number(code, reserved, measure);
    }

    /// <summary>
    /// Reads a fixed-length ASCII string. Trailing NULs are trimmed; the cursor advances by the
    /// full <paramref name="length"/> regardless of NUL padding.
    /// </summary>
    public string ReadAsciiString(int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        EnsureAvailable(length);
        ReadOnlySpan<byte> slice = _data.Span.Slice(_position, length);
        _position += length;

        int end = slice.Length;
        while (end > 0 && slice[end - 1] == 0) end--;

        return Encoding.ASCII.GetString(slice.Slice(0, end));
    }

    /// <summary>Returns a slice without advancing the cursor.</summary>
    public ReadOnlySpan<byte> Peek(int count)
    {
        if ((uint)count > (uint)Remaining)
            throw new IccEndOfStreamException(count, Remaining);
        return _data.Span.Slice(_position, count);
    }

    /// <summary>Reads <paramref name="count"/> raw bytes and advances the cursor.</summary>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        EnsureAvailable(count);
        ReadOnlySpan<byte> slice = _data.Span.Slice(_position, count);
        _position += count;
        return slice;
    }

    private void EnsureAvailable(int count)
    {
        if ((uint)count > (uint)Remaining)
            throw new IccEndOfStreamException(count, Remaining);
    }
}
