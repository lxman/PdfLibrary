using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class CmapSubtablesFormat8 : ICmapSubtable
    {
        public int Language { get; }

        public byte[] Is32 { get; }

        public List<SequentialMapGroup> SequentialMapGroups { get; } = new List<SequentialMapGroup>();

        public CmapSubtablesFormat8(BigEndianReader reader)
        {
            ushort format = reader.ReadUShort();
            _ = reader.ReadUShort();
            uint length = reader.ReadUInt32();
            Language = reader.ReadInt32();
            Is32 = reader.ReadBytes(8192);
            uint numGroups = reader.ReadUInt32();
            for (var i = 0; i < numGroups; i++)
            {
                SequentialMapGroups.Add(new SequentialMapGroup(reader.ReadBytes(SequentialMapGroup.RecordSize)));
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            return (from @group in SequentialMapGroups
                    where codePoint >= @group.StartCharCode && codePoint <= @group.EndCharCode
                    select (ushort)(@group.StartGlyphId + (codePoint - @group.StartCharCode)))
                .FirstOrDefault();
        }
    }
}