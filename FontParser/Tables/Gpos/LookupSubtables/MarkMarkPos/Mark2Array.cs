using FontParser.Reader;

namespace FontParser.Tables.Gpos.LookupSubtables.MarkMarkPos
{
    public class Mark2Array
    {
        public Mark2Record[] Mark2Records { get; }

        public Mark2Array(BigEndianReader reader, ushort markClassCount)
        {
            long startOfTable = reader.Position;
            ushort mark2Count = reader.ReadUShort();
            Mark2Records = new Mark2Record[mark2Count];
            if (markClassCount <= 0) return;
            for (var i = 0; i < mark2Count; i++)
            {
                Mark2Records[i] = new Mark2Record(reader, markClassCount, startOfTable);
            }
        }
    }
}