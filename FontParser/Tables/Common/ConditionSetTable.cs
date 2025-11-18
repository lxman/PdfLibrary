using FontParser.Reader;
using FontParser.Tables.Common.Condition.Format1;

namespace FontParser.Tables.Common
{
    public class ConditionSetTable
    {
        public ConditionTableFormat1[] Conditions { get; }

        public ConditionSetTable(BigEndianReader reader)
        {
            ushort conditionCount = reader.ReadUShort();

            uint[] conditionOffsets = reader.ReadUInt32Array(conditionCount);

            Conditions = new ConditionTableFormat1[conditionCount];
            for (var i = 0; i < conditionCount; i++)
            {
                reader.Seek(conditionOffsets[i]);
                Conditions[i] = new ConditionTableFormat1(reader);
            }
        }
    }
}