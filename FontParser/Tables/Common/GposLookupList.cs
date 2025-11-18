using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Common
{
    public class GposLookupList
    {
        public List<GposLookupTable> LookupTables { get; } = new List<GposLookupTable>();

        public GposLookupList(BigEndianReader reader)
        {
            long lookupListStart = reader.Position;

            ushort lookupCount = reader.ReadUShort();
            ushort[] lookupOffsets = reader.ReadUShortArray(lookupCount);

            for (var i = 0; i < lookupCount; i++)
            {
                reader.Seek(lookupOffsets[i] + lookupListStart);
                LookupTables.Add(new GposLookupTable(reader));
            }
        }
    }
}