using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;

namespace FontParser.Reader
{
    public class FileByteReader
    {
        public uint BytesRemaining => (uint)_data.Length - Position;

        public uint Position { get; private set; }

        private readonly byte[] _data;

        public FileByteReader(FileStream fs)
        {
            _data = new byte[fs.Length];
            Task.Run(() => fs.ReadAsync(_data, 0, _data.Length)).ConfigureAwait(false);
        }

        public FileByteReader(string file)
        {
            if (string.IsNullOrEmpty(file))
            {
                throw new ArgumentNullException(nameof(file));
            }
            if (!File.Exists(file))
            {
                throw new FileNotFoundException("File not found", file);
            }
            _data = File.ReadAllBytes(file);
        }

        public void Seek(uint position)
        {
            Position = position;
        }

        public byte[] ReadBytes(uint count)
        {
            var result = new byte[count];
            Array.Copy(_data, Position, result, 0, count);
            Position += count;
            return result;
        }

        public string ReadString(uint count)
        {
            return System.Text.Encoding.UTF8.GetString(ReadBytes(count));
        }

        public uint ReadUInt32()
        {
            return BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4));
        }

        public ushort ReadUInt16()
        {
            return BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(2));
        }

        public uint ReadUintBase128()
        {
            uint accumulator = 0;
            for (var i = 0; i < 5; i++)
            {
                byte b = ReadBytes(1)[0];
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

            byte code = ReadBytes(1)[0];
            switch (code)
            {
                case 253:
                    value = ReadUInt16();
                    break;

                case 254:
                    value = ReadBytes(1)[0];
                    value += 253 * 2;
                    break;

                case 255:
                    value = ReadBytes(1)[0];
                    value += 253;
                    break;

                default:
                    value = code;
                    break;
            }

            return value;
        }
    }
}