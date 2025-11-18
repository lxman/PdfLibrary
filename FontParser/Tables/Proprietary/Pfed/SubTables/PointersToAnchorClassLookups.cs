using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Pfed.SubTables
{
    public class PointersToAnchorClassLookups
    {
        public List<AnchorClassLookup> LookupItems { get; } = new List<AnchorClassLookup>();

        public PointersToAnchorClassLookups(BigEndianReader reader)
        {
            ushort count = reader.ReadUShort();
            for (var i = 0; i < count; i++)
            {
                LookupItems.Add(new AnchorClassLookup(reader));
            }
        }
    }
}