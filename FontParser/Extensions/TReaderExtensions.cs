using System;
using System.Buffers.Binary;
using FontParser.Reader;

// ReSharper disable InconsistentNaming

namespace FontParser.Extensions
{
    public static class TReaderExtensions
    {
        public static ushort Read255Uint16(this TReader<byte> data)
        {
            ushort value;

            byte code = data.Read();
            switch (code)
            {
                case 253:
                    value = BinaryPrimitives.ReadUInt16BigEndian(data.Read(2));
                    break;

                case 254:
                    value = data.Read();
                    value += 253 * 2;
                    break;

                case 255:
                    value = data.Read();
                    value += 253;
                    break;

                default:
                    value = code;
                    break;
            }

            return value;
        }

        public static uint ReadUintBase128(this TReader<byte> data)
        {
            uint accumulator = 0;
            for (var i = 0; i < 5; i++)
            {
                byte b = data.Read();
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
    }
}