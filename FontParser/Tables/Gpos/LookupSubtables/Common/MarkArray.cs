using FontParser.Reader;

namespace FontParser.Tables.Gpos.LookupSubtables.Common
{
    public class MarkArray
    {
        public MarkRecord[] MarkRecords { get; }

        public MarkArray(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            ushort markCount = reader.ReadUShort();
            MarkRecords = new MarkRecord[markCount];
            for (var i = 0; i < markCount; i++)
            {
                MarkRecords[i] = new MarkRecord(reader, startOfTable);
            }
        }
    }
}