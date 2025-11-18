using System;

namespace FontParser.Tables.Cff.Type1
{
    public static class Calc
    {
        public static int Integer(byte[] data, ref int index)
        {
            if (data[index] >= 0x20 && data[index] <= 0xF6)
            {
                return data[index++] - 0x8B;
            }
            if (data[index] >= 0xF7 && data[index] <= 0xFA)
            {
                return (data[index++] - 0xF7) * 0x100 + data[index++] + 0x6C;
            }
            if (data[index] >= 0xFB && data[index] <= 0xFE)
            {
                return -(data[index++] - 0xFB) * 0x100 - data[index++] - 0x6C;
            }
            return data[index++] switch
            {
                0x1C => (short)(data[index++] << 8 | data[index++]),
                0x1D => data[index++] << 0x18 | data[index++] << 0x10 | data[index++] << 8 | data[index++],
                _ => throw new NotImplementedException()
            };
        }

        public static double Double(byte[] data, ref int index)
        {
            if (data[index] != 0x1E) throw new ArgumentException("Invalid byte marker for double value.");
            index++; // Skip the initial 0x1E marker
            var result = 0.0;
            var fraction = 0.1;
            var isFraction = false;
            var isNegative = false;
            while (index < data.Length)
            {
                byte b = data[index++];
                for (var i = 0; i < 2; i++)
                {
                    int nibble = i == 0 ? b >> 4 : b & 0x0F;
                    switch (nibble)
                    {
                        case 0xF:
                            return isNegative ? -result : result;

                        case 0xE:
                            isNegative = true;
                            continue;
                        case 0xA:
                            isFraction = true;
                            continue;
                    }
                    if (nibble < 0 || nibble > 9) continue;
                    if (isFraction)
                    {
                        result += nibble * fraction;
                        fraction *= 0.1;
                    }
                    else
                    {
                        result = result * 10 + nibble;
                    }
                }
            }
            throw new ArgumentException("Unexpected end of data while parsing double value.");
        }
    }
}