using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Pfed.SubTables
{
    public class GcmnTable : IPfedSubtable
    {
        public ushort Version { get; }

        public List<NameLookup> NameLookups { get; } = new List<NameLookup>();

        public GcmnTable(BigEndianReader reader)
        {
            long start = reader.Position;
            Version = reader.ReadUShort();
            ushort count = reader.ReadUShort();
            for (var i = 0; i < count; i++)
            {
                NameLookups.Add(new NameLookup(reader, start));
            }
        }
    }
}