using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Pfed.SubTables
{
    public class PointerToAnchorClassNamePointer
    {
        public List<AnchorClassNamePointer> AnchorClassNamePointers { get; } = new List<AnchorClassNamePointer>();

        public PointerToAnchorClassNamePointer(BigEndianReader reader)
        {
            ushort count = reader.ReadUShort();
            for (var i = 0; i < count; i++)
            {
                AnchorClassNamePointers.Add(new AnchorClassNamePointer(reader));
            }
        }
    }
}