using System;
using System.Collections.Generic;
using System.Drawing;
using FontParser.Extensions;

namespace FontParser.Tables.WOFF2
{
    public class PointTransform
    {
        private readonly List<TransformRecord> _records = new List<TransformRecord>();

        public PointTransform()
        {
            BuildRecords();
        }

        private static int count;

        public Point? Transform(int index, byte[] data)
        {
            TransformRecord? record = _records[index];
            if (record is null) return null;
            int length = (record.XBits + record.YBits) / 8;
            if (data.Length != length) return null;
            Point toReturn = ParsePoint(record, data);
            toReturn.X += record.XOffset;
            toReturn.Y += record.YOffset;
            if (record.XSign != 0) toReturn.X *= record.XSign;
            if (record.YSign != 0) toReturn.Y *= record.YSign;
            return toReturn;
        }

        private static Point ParsePoint(TransformRecord record, byte[] data)
        {
            int x;
            int y;
            switch (record.XBits)
            {
                case 0:
                    x = 0;
                    return new Point(x, data.ParseBits(0, record.YBits));

                case 4:
                    x = data.ParseBits(4, 4);
                    y = data.ParseBits(0, 4);
                    return new Point(x, y);

                case 8:
                    x = data.ParseBits(0, record.XBits);
                    y = record.YBits == 8 ? data.ParseBits(record.XBits, record.YBits) : 0;
                    return new Point(x, y);

                case 12:
                    int xTranslated = (data[0] << 4) | (data[1] >> 4);
                    int yTranslated = ((data[1] & 0x0F) << 8) | data[2];
                    return new Point(xTranslated, yTranslated);

                case 16:
                    xTranslated = (data[0] << 8) | data[1];
                    yTranslated = (data[2] << 8) | data[3];
                    return new Point(xTranslated, yTranslated);

                default:
                    throw new FormatException($"Unhandled {record.XBits}");
            }
        }

        private void BuildRecords()
        {
            var index = 0;
            while (index < 0x7F)
            {
                _records.Add(BuildTransformRecord(index++));
            }
        }

        private static TransformRecord BuildTransformRecord(int index)
        {
            var record = new TransformRecord
            {
                Index = index
            };
            if (index < 10)
            {
                record.XBits = 0;
                record.YBits = 8;
                record.XOffset = 0;
                record.XSign = 0;
                if (index < 2)
                {
                    record.YOffset = 0;
                    CycleYSign(index, ref record);
                }
                else if (index < 4)
                {
                    record.YOffset = 256;
                    CycleYSign(index - 2, ref record);
                }
                else if (index < 6)
                {
                    record.YOffset = 512;
                    CycleYSign(index - 4, ref record);
                }
                else if (index < 8)
                {
                    record.YOffset = 768;
                    CycleYSign(index - 6, ref record);
                }
                else
                {
                    record.YOffset = 1024;
                    CycleYSign(index - 8, ref record);
                }
            }
            else if (index < 20)
            {
                record.XBits = 8;
                record.YBits = 0;
                record.YOffset = 0;
                record.YSign = 0;
                if (index < 12)
                {
                    record.XOffset = 0;
                    CycleXSign(index - 10, ref record);
                }
                else if (index < 14)
                {
                    record.XOffset = 256;
                    CycleXSign(index - 12, ref record);
                }
                else if (index < 16)
                {
                    record.XOffset = 512;
                    CycleXSign(index - 14, ref record);
                }
                else if (index < 18)
                {
                    record.XOffset = 768;
                    CycleXSign(index - 16, ref record);
                }
                else
                {
                    record.XOffset = 1024;
                    CycleXSign(index - 18, ref record);
                }
            }
            else if (index < 36)
            {
                record.XBits = 4;
                record.YBits = 4;
                record.XOffset = 1;
                if (index < 24)
                {
                    record.YOffset = 1;
                    CycleXySign(index - 20, ref record);
                }
                else if (index < 28)
                {
                    record.YOffset = 17;
                    CycleXySign(index - 24, ref record);
                }
                else if (index < 32)
                {
                    record.YOffset = 33;
                    CycleXySign(index - 28, ref record);
                }
                else
                {
                    record.YOffset = 49;
                    CycleXySign(index - 32, ref record);
                }
            }
            else if (index < 52)
            {
                record.XBits = 4;
                record.YBits = 4;
                record.XOffset = 17;
                if (index < 40)
                {
                    record.YOffset = 1;
                    CycleXySign(index - 36, ref record);
                }
                else if (index < 44)
                {
                    record.YOffset = 17;
                    CycleXySign(index - 40, ref record);
                }
                else if (index < 48)
                {
                    record.YOffset = 33;
                    CycleXySign(index - 44, ref record);
                }
                else
                {
                    record.YOffset = 49;
                    CycleXySign(index - 48, ref record);
                }
            }
            else if (index < 68)
            {
                record.XBits = 4;
                record.YBits = 4;
                record.XOffset = 33;
                if (index < 56)
                {
                    record.YOffset = 1;
                    CycleXySign(index - 52, ref record);
                }
                else if (index < 60)
                {
                    record.YOffset = 17;
                    CycleXySign(index - 56, ref record);
                }
                else if (index < 64)
                {
                    record.YOffset = 33;
                    CycleXySign(index - 60, ref record);
                }
                else
                {
                    record.YOffset = 49;
                    CycleXySign(index - 64, ref record);
                }
            }
            else if (index < 84)
            {
                record.XBits = 4;
                record.YBits = 4;
                record.XOffset = 49;
                if (index < 72)
                {
                    record.YOffset = 1;
                    CycleXySign(index - 68, ref record);
                }
                else if (index < 76)
                {
                    record.YOffset = 17;
                    CycleXySign(index - 72, ref record);
                }
                else if (index < 80)
                {
                    record.YOffset = 33;
                    CycleXySign(index - 76, ref record);
                }
                else
                {
                    record.YOffset = 49;
                    CycleXySign(index - 80, ref record);
                }
            }
            else if (index < 96)
            {
                record.XBits = 8;
                record.YBits = 8;
                record.XOffset = 1;
                if (index < 88)
                {
                    record.YOffset = 1;
                    CycleXySign(index - 84, ref record);
                }
                else if (index < 92)
                {
                    record.YOffset = 257;
                    CycleXySign(index - 88, ref record);
                }
                else
                {
                    record.YOffset = 513;
                    CycleXySign(index - 92, ref record);
                }
            }
            else if (index < 108)
            {
                record.XBits = 8;
                record.YBits = 8;
                record.XOffset = 257;
                if (index < 100)
                {
                    record.YOffset = 1;
                    CycleXySign(index - 96, ref record);
                }
                else if (index < 104)
                {
                    record.YOffset = 257;
                    CycleXySign(index - 100, ref record);
                }
                else
                {
                    record.YOffset = 513;
                    CycleXySign(index - 104, ref record);
                }
            }
            else if (index < 120)
            {
                record.XBits = 8;
                record.YBits = 8;
                record.XOffset = 513;
                if (index < 112)
                {
                    record.YOffset = 1;
                    CycleXySign(index - 108, ref record);
                }
                else if (index < 116)
                {
                    record.YOffset = 257;
                    CycleXySign(index - 112, ref record);
                }
                else
                {
                    record.YOffset = 513;
                    CycleXySign(index - 116, ref record);
                }
            }
            else if (index < 124)
            {
                record.XBits = 12;
                record.YBits = 12;
                record.XOffset = 0;
                record.YOffset = 0;
                CycleXySign(index - 120, ref record);
            }
            else
            {
                record.XBits = 16;
                record.YBits = 16;
                record.XOffset = 0;
                record.YOffset = 0;
                CycleXySign(index - 124, ref record);
            }
            return record;
        }

        private static void CycleYSign(int value, ref TransformRecord record)
        {
            record.YSign = value switch
            {
                0 => -1,
                1 => 1,
                _ => record.YSign
            };
        }

        private static void CycleXSign(int value, ref TransformRecord record)
        {
            record.XSign = value switch
            {
                0 => -1,
                1 => 1,
                _ => record.XSign
            };
        }

        private static void CycleXySign(int value, ref TransformRecord record)
        {
            record.XSign = value switch
            {
                0 => -1,
                1 => 1,
                2 => -1,
                3 => 1,
                _ => record.XSign
            };
            record.YSign = value switch
            {
                0 => -1,
                1 => -1,
                2 => 1,
                3 => 1,
                _ => record.YSign
            };
        }
    }

    internal class TransformRecord
    {
        internal int Index { get; set; }

        internal int XBits { get; set; }

        internal int YBits { get; set; }

        internal int XOffset { get; set; }

        internal int YOffset { get; set; }

        internal int XSign { get; set; }

        internal int YSign { get; set; }
    }
}