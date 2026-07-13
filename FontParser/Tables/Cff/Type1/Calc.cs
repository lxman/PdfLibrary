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
            // CFF DICT real operand (Adobe TN#5176 §5, Table 5). Nibbles after the 0x1E marker:
            //   0-9 digit, 0xa '.', 0xb 'E' (positive exponent), 0xc 'E-' (negative exponent),
            //   0xd reserved, 0xe '-' (mantissa sign), 0xf end. The exponent nibbles are required:
            //   real fonts write the FontMatrix scale in scientific notation (e.g. 1.004E-3), and
            //   dropping the exponent decoded such values ~10^n too large.
            if (data[index] != 0x1E) throw new ArgumentException("Invalid byte marker for double value.");
            index++; // Skip the initial 0x1E marker
            var mantissa = 0.0;
            var fraction = 0.1;
            var isFraction = false;
            var isNegative = false;
            var inExponent = false;
            var exponentNegative = false;
            var exponent = 0;
            while (index < data.Length)
            {
                byte b = data[index++];
                for (var i = 0; i < 2; i++)
                {
                    int nibble = i == 0 ? b >> 4 : b & 0x0F;
                    switch (nibble)
                    {
                        case 0xF:
                        {
                            double value = isNegative ? -mantissa : mantissa;
                            if (exponent != 0)
                                value *= Math.Pow(10, exponentNegative ? -exponent : exponent);
                            return value;
                        }
                        case 0xE:
                            isNegative = true;
                            continue;
                        case 0xD: // reserved
                            continue;
                        case 0xC: // 'E-' : exponent follows, negative
                            inExponent = true;
                            exponentNegative = true;
                            continue;
                        case 0xB: // 'E' : exponent follows, positive
                            inExponent = true;
                            continue;
                        case 0xA:
                            isFraction = true;
                            continue;
                    }
                    if (nibble < 0 || nibble > 9) continue;
                    if (inExponent)
                    {
                        exponent = exponent * 10 + nibble;
                    }
                    else if (isFraction)
                    {
                        mantissa += nibble * fraction;
                        fraction *= 0.1;
                    }
                    else
                    {
                        mantissa = mantissa * 10 + nibble;
                    }
                }
            }
            throw new ArgumentException("Unexpected end of data while parsing double value.");
        }
    }
}