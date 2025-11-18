using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.Common.ClassDefinition
{
    public class ClassDefinitionFormat1 : IClassDefinition
    {
        public ushort Format { get; }

        public ushort StartGlyph { get; }

        public List<ushort> ClassValues { get; }

        public ClassDefinitionFormat1(BigEndianReader reader)
        {
            Format = reader.ReadUShort();
            StartGlyph = reader.ReadUShort();
            ushort glyphCount = reader.ReadUShort();
            ClassValues = reader.ReadUShortArray(glyphCount).ToList();
        }
    }
}