using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Stat.AxisValue;

namespace FontParser.Tables.Stat
{
    public class DesignAxesArray
    {
        public List<AxisRecord> DesignAxes { get; } = new List<AxisRecord>();

        public List<IAxisValueTable> AxisValueTables { get; } = new List<IAxisValueTable>();

        public DesignAxesArray(
            BigEndianReader reader,
            ushort designAxisSize,
            ushort designAxisCount,
            ushort axisValueCount,
            uint designAxesOffset,
            uint offsetToAxisValueOffsets)
        {
            reader.Seek(designAxesOffset);
            for (var i = 0; i < designAxisCount; i++)
            {
                reader.Seek(reader.Position + (designAxisSize - 8));
                DesignAxes.Add(new AxisRecord(reader));
            }

            reader.Seek(offsetToAxisValueOffsets);
            var axisValueOffsets = new List<ushort>();
            for (var i = 0; i < axisValueCount; i++)
            {
                axisValueOffsets.Add(reader.ReadUShort());
            }

            for (var i = 0; i < axisValueCount; i++)
            {
                reader.Seek(offsetToAxisValueOffsets + axisValueOffsets[i]);
                byte format = reader.PeekBytes(2)[1];
                switch (format)
                {
                    case 1:
                        AxisValueTables.Add(new AxisValueFormat1(reader));
                        break;

                    case 2:
                        AxisValueTables.Add(new AxisValueFormat2(reader));
                        break;

                    case 3:
                        AxisValueTables.Add(new AxisValueFormat3(reader));
                        break;

                    case 4:
                        AxisValueTables.Add(new AxisValueFormat4(reader));
                        break;
                }
            }
        }
    }
}