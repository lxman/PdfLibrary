using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Common.ClassDefinition
{
    public class ClassDefinitionFormat2 : IClassDefinition
    {
        public ushort Format { get; }

        public List<ClassRange> ClassRanges { get; } = new List<ClassRange>();

        public ClassDefinitionFormat2(BigEndianReader reader)
        {
            Format = reader.ReadUShort();
            ushort classRangeCount = reader.ReadUShort();
            for (var i = 0; i < classRangeCount; i++)
            {
                ClassRanges.Add(new ClassRange(reader));
            }
        }
    }
}