using FontParser.Reader;

namespace FontParser.Tables.Common.Condition.Format1
{
    public class ConditionTableFormat1
    {
        public ushort Format { get; }

        public ushort AxisIndex { get; }

        public float FilterRangeMinValue { get; }

        public float FilterRangeMaxValue { get; }

        public ConditionTableFormat1(BigEndianReader reader)
        {
            Format = reader.ReadUShort();
            AxisIndex = reader.ReadUShort();
            FilterRangeMinValue = reader.ReadF2Dot14();
            FilterRangeMaxValue = reader.ReadF2Dot14();
        }
    }
}