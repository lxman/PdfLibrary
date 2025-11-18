using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class CmapSubtablesFormat13 : ICmapSubtable
    {
        public int Language { get; }

        public List<ConstantMapGroup> Groups { get; } = new List<ConstantMapGroup>();

        public CmapSubtablesFormat13(BigEndianReader reader)
        {
            ushort format = reader.ReadUShort();
            _ = reader.ReadUShort();
            uint length = reader.ReadUInt32();
            Language = reader.ReadInt32();
            int numGroups = reader.ReadInt32();
            for (var i = 0; i < numGroups; i++)
            {
                Groups.Add(new ConstantMapGroup(reader.ReadBytes(ConstantMapGroup.RecordSize)));
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            return (from @group in Groups
                    where codePoint >= @group.StartCharCode && codePoint <= @group.EndCharCode
                    select (ushort)@group.GlyphId)
                .FirstOrDefault();
        }
    }
}