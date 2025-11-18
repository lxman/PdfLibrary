using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace FontParser.Reader
{
    public class BigEndianReader : IDisposable
    {
        public long BytesRemaining => _data.Length - Position;

        public long WordsRemaining => (_data.Length / 2) - (Position / 2);

        public long Position { get; private set; }

        public bool LogChanges { get; set; }

        private readonly byte[] _data;

        public BigEndianReader(byte[] data)
        {
            _data = ArrayPool<byte>.Shared.Rent(data.Length);
            data.CopyTo(_data, 0);
        }

        public void Seek(long position)
        {
            Position = position;
        }

        public byte[] ReadBytes(
            long count,
            [CallerMemberName] string member = "",
            [CallerFilePath] string path = "",
            [CallerLineNumber] int line = -1)
        {
            if (Position + count > _data.Length)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Source array was not long enough by {Position + count - _data.Length} bytes.");
                sb.AppendLine($"Called from {path}");
                sb.AppendLine(member);
                sb.AppendLine($"Line #{line}");
                throw new ArgumentException(sb.ToString(), nameof(_data));
            }
            var result = new byte[count];
            Array.Copy(_data, Position, result, 0, count);
            Position += count;
            return result;
        }

        public byte[] PeekBytes(int count)
        {
            var result = new byte[count];
            Array.Copy(_data, Position, result, 0, count);
            return result;
        }

        public byte ReadByte()
        {
            return _data[Position++];
        }

        public sbyte ReadSByte()
        {
            return (sbyte)ReadByte();
        }

        public sbyte[] ReadSbytes(int count)
        {
            var result = new sbyte[count];
            Array.Copy(_data, Position, result, 0, count);
            Position += count;
            return result;
        }

        public ushort ReadUShort()
        {
            return ReadUShort16();
        }

        public short ReadShort()
        {
            return BinaryPrimitives.ReadInt16BigEndian(ReadBytes(2));
        }

        public int ReadInt16()
        {
            return ReadShort();
        }

        public uint ReadUInt24()
        {
            byte[] data = ReadBytes(3);
            return (uint)((data[0] << 16) | (data[1] << 8) | data[2]);
        }

        public uint ReadUInt32()
        {
            return BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4));
        }

        public long ReadLong()
        {
            return BinaryPrimitives.ReadInt64BigEndian(ReadBytes(8));
        }

        public int ReadInt32()
        {
            byte[] data = ReadBytes(4);
            return BinaryPrimitives.ReadInt32BigEndian(data);
        }

        public float ReadF16Dot16()
        {
            byte[] data = ReadBytes(4);
            return BinaryPrimitives.ReadInt32BigEndian(data) / 65536.0f;
        }

        public float ReadF2Dot14()
        {
            byte[] data = ReadBytes(2);
            return BinaryPrimitives.ReadInt16BigEndian(data) / 16384.0f;
        }

        public long ReadLongDateTime()
        {
            return ReadLong();
        }

        private ushort ReadUShort16()
        {
            return BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(2));
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

        public uint ReadOffset(int offSize)
        {
            return offSize switch
            {
                1 => ReadByte(),
                2 => ReadUShort(),
                3 => ReadUInt24(),
                4 => ReadUInt32(),
                _ => 0
            };
        }

        public uint[] ReadOffsets(int offSize, uint count)
        {
            var result = new uint[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = ReadOffset(offSize);
            }
            return result;
        }

        public string ReadNullTerminatedString(bool isUnicode)
        {
            var data = new List<byte>();
            if (isUnicode)
            {
                while (PeekBytes(2) != new byte[] { 0, 0 })
                {
                    data.Add(ReadByte());
                    data.Add(ReadByte());
                }
                _ = ReadBytes(2);

                return Encoding.Unicode.GetString(data.ToArray());
            }
            while (PeekBytes(1)[0] != 0)
            {
                data.Add(ReadByte());
            }

            _ = ReadByte();

            return Encoding.ASCII.GetString(data.ToArray());
        }

        public uint ReadUintBase128()
        {
            uint accumulator = 0;
            for (var i = 0; i < 5; i++)
            {
                byte b = ReadByte();
                if (i == 0 && b == 0x80)
                {
                    throw new Exception("Invalid base 128 value");
                }
                if ((accumulator & 0xFE000000) != 0)
                {
                    throw new Exception("Invalid base 128 value");
                }
                accumulator = (accumulator << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    return accumulator;
                }
            }

            throw new Exception("Invalid base 128 value");
        }

        public ushort Read255UInt16()
        {
            ushort value;

            byte code = ReadByte();
            switch (code)
            {
                case 253:
                    value = ReadUShort();
                    break;

                case 254:
                    value = ReadByte();
                    value += 253 * 2;
                    break;

                case 255:
                    value = ReadByte();
                    value += 253;
                    break;

                default:
                    value = code;
                    break;
            }

            return value;
        }

        public void Dispose()
        {
            ArrayPool<byte>.Shared.Return(_data);
        }
    }
}