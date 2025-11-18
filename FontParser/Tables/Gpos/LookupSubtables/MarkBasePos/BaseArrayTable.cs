using FontParser.Reader;

namespace FontParser.Tables.Gpos.LookupSubtables.MarkBasePos
{
    public class BaseArrayTable
    {
        public BaseRecord[] BaseRecords { get; }

        public BaseArrayTable(BigEndianReader reader, ushort markClassCount)
        {
            ushort baseCount = reader.ReadUShort();
            BaseRecords = new BaseRecord[baseCount];
            for (var i = 0; i < baseCount; i++)
            {
                BaseRecords[i] = new BaseRecord(markClassCount, reader);
            }
        }
    }
}