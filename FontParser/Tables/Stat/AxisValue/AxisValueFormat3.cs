using FontParser.Reader;

namespace FontParser.Tables.Stat.AxisValue
{
    public class AxisValueFormat3 : IAxisValueTable
    {
        public ushort Format { get; }

        public ushort AxisIndex { get; }

        public AxisValueFlags Flags { get; }

        public ushort ValueNameId { get; }

        public float Value { get; }

        public float LinkedValue { get; }

        public AxisValueFormat3(BigEndianReader reader)
        {
            Format = reader.ReadUShort();
            AxisIndex = reader.ReadUShort();
            Flags = (AxisValueFlags)reader.ReadUShort();
            ValueNameId = reader.ReadUShort();
            Value = reader.ReadF16Dot16();
            LinkedValue = reader.ReadF16Dot16();
        }
    }
}