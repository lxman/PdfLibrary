using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Stat.AxisValue
{
    public class AxisValueFormat4 : IAxisValueTable
    {
        public ushort Format { get; }

        public AxisValueFlags Flags { get; }

        public ushort ValueNameId { get; }

        public List<AxisValueRecord> AxisValues { get; } = new List<AxisValueRecord>();

        public AxisValueFormat4(BigEndianReader reader)
        {
            Format = reader.ReadUShort();
            ushort axisCount = reader.ReadUShort();
            Flags = (AxisValueFlags)reader.ReadUShort();
            ValueNameId = reader.ReadUShort();

            for (var i = 0; i < axisCount; i++)
            {
                AxisValues.Add(new AxisValueRecord(reader));
            }
        }
    }
}