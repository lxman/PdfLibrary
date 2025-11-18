using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class CmapSubtableFormat0 : ICmapSubtable
    {
        public ushort Format { get; }

        public int Language { get; }

        public List<uint> GlyphIndexArray { get; } = new List<uint>();

        public CmapSubtableFormat0(BigEndianReader reader)
        {
            Format = reader.ReadUShort();
            ushort length = reader.ReadUShort();
            Language = reader.ReadInt16();
            for (var i = 0; i < 256; i++)
            {
                GlyphIndexArray.Add(reader.ReadByte());
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            return codePoint < 256 ? (ushort)GlyphIndexArray[codePoint] : (ushort)0;
        }
    }
}