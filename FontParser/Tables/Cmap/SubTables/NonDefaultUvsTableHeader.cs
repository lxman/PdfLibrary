using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class NonDefaultUvsTableHeader
    {
        public List<UvsMappingRecord> UvsMappings { get; } = new List<UvsMappingRecord>();

        public NonDefaultUvsTableHeader(BigEndianReader reader)
        {
            uint numUvsMappings = reader.ReadUInt32();
            for (var i = 0; i < numUvsMappings; i++)
            {
                UvsMappings.Add(new UvsMappingRecord(reader.ReadBytes(UvsMappingRecord.RecordSize)));
            }
        }
    }
}