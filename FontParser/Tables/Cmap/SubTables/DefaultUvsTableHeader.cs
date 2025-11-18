using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class DefaultUvsTableHeader
    {
        public List<UnicodeRangeRecord> UnicodeRangeRecords { get; } = new List<UnicodeRangeRecord>();

        public DefaultUvsTableHeader(BigEndianReader reader)
        {
            uint numUnicodeRangeRecords = reader.ReadUInt32();
            for (var i = 0; i < numUnicodeRangeRecords; i++)
            {
                UnicodeRangeRecords.Add(new UnicodeRangeRecord(reader.ReadBytes(UnicodeRangeRecord.RecordSize)));
            }
        }
    }
}