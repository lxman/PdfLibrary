using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cmap.SubTables
{
    public class CmapSubtablesFormat10 : ICmapSubtable
    {
        public int Language { get; }

        public uint StartChar { get; }

        public List<uint> GlyphIndexArray { get; } = new List<uint>();

        private readonly uint _numChars;

        public CmapSubtablesFormat10(BigEndianReader reader)
        {
            ushort format = reader.ReadUShort();
            _ = reader.ReadUShort();
            uint length = reader.ReadUInt32();
            Language = reader.ReadInt32();
            StartChar = reader.ReadUInt32();
            _numChars = reader.ReadUInt32();
            for (var i = 0; i < _numChars; i++)
            {
                GlyphIndexArray.Add(reader.ReadUShort());
            }
        }

        public ushort GetGlyphId(ushort codePoint)
        {
            uint index = codePoint - StartChar;
            if (index < _numChars)
            {
                return (ushort)GlyphIndexArray[(int)index];
            }
            return 0;
        }
    }
}