using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Pfed.SubTables
{
    public class ColrTable : IPfedSubtable
    {
        public ushort Version { get; }

        public List<ColrItem> ColorEntries { get; } = new List<ColrItem>();

        public ColrTable(BigEndianReader reader)
        {
            Version = reader.ReadUShort();
            ushort count = reader.ReadUShort();
            for (var i = 0; i < count; i++)
            {
                ColorEntries.Add(new ColrItem(reader));
            }
        }
    }
}