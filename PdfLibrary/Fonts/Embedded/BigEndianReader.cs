using System.Buffers.Binary;

namespace PdfLibrary.Fonts.Embedded
{
    /// <summary>
    /// Binary reader for big-endian font data (TrueType, OpenType, etc.)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class BigEndianReader : IDisposable
    {
        public long BytesRemaining => _data.Length - Position;
        public long Position { get; private set; }

        private readonly byte[] _data;

        public BigEndianReader(byte[] data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public void Seek(long position)
        {
            if (position < 0 || position > _data.Length)
                throw new ArgumentOutOfRangeException(nameof(position));
            Position = position;
        }

        public byte[] ReadBytes(long count)
        {
            if (Position + count > _data.Length)
            {
                throw new InvalidOperationException(
                    $"Cannot read {count} bytes at position {Position}. " +
                    $"Only {_data.Length - Position} bytes remaining.");
            }

            var result = new byte[count];
            Array.Copy(_data, Position, result, 0, count);
            Position += count;
            return result;
        }

        public byte[] PeekBytes(int count)
        {
            if (Position + count > _data.Length)
                return [];

            var result = new byte[count];
            Array.Copy(_data, Position, result, 0, count);
            return result;
        }

        public byte ReadByte()
        {
            return Position >= _data.Length
                ? throw new InvalidOperationException("End of stream reached.")
                : _data[Position++];
        }

        public sbyte ReadSByte()
        {
            return (sbyte)ReadByte();
        }

        public ushort ReadUShort()
        {
            return BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(2));
        }

        public short ReadShort()
        {
            return BinaryPrimitives.ReadInt16BigEndian(ReadBytes(2));
        }

        public int ReadInt16()
        {
            return ReadShort();
        }

        public uint ReadUInt32()
        {
            return BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4));
        }

        public int ReadInt32()
        {
            return BinaryPrimitives.ReadInt32BigEndian(ReadBytes(4));
        }

        public long ReadLong()
        {
            return BinaryPrimitives.ReadInt64BigEndian(ReadBytes(8));
        }

        public float ReadF16Dot16()
        {
            // Fixed-point format: 16 bits integer, 16 bits fractional
            return BinaryPrimitives.ReadInt32BigEndian(ReadBytes(4)) / 65536.0f;
        }

        public float ReadF2Dot14()
        {
            // Fixed-point format: 2 bits integer, 14 bits fractional
            return BinaryPrimitives.ReadInt16BigEndian(ReadBytes(2)) / 16384.0f;
        }

        public long ReadLongDateTime()
        {
            return ReadLong();
        }

        public ushort[] ReadUShortArray(uint count)
        {
            var result = new ushort[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = ReadUShort();
            }
            return result;
        }

        public short[] ReadShortArray(uint count)
        {
            var result = new short[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = ReadShort();
            }
            return result;
        }

        public uint[] ReadUInt32Array(uint count)
        {
            var result = new uint[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = ReadUInt32();
            }
            return result;
        }

        public void Dispose()
        {
            // No resources to dispose of in this simplified version
        }
    }
}
