using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class CmapSubtablesFormat12 : ICmapSubtable
    {
        public int Language { get; }

        public List<SequentialMapGroup> Groups { get; } = new List<SequentialMapGroup>();

        public CmapSubtablesFormat12(BigEndianReader reader)
        {
            ushort format = reader.ReadUShort();
            _ = reader.ReadUShort();
            uint length = reader.ReadUInt32();
            Language = reader.ReadInt32();
            uint numGroups = reader.ReadUInt32();
            for (var i = 0; i < numGroups; i++)
            {
                Groups.Add(new SequentialMapGroup(reader.ReadBytes(SequentialMapGroup.RecordSize)));
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            return (from @group in Groups
                    where codePoint >= @group.StartCharCode && codePoint <= @group.EndCharCode
                    select (ushort)(@group.StartGlyphId + (codePoint - @group.StartCharCode)))
                .FirstOrDefault();
        }
    }
}