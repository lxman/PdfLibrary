using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common.ClassDefinition;
using FontParser.Tables.Common.CoverageFormat;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8601 // Possible null reference assignment.

namespace FontParser.Tables.Common.SequenceContext.Format2
{
    public class SequenceContextFormat2 : ILookupSubTable, ISequenceContext
    {
        public List<ClassSequenceRuleSet> ClassSequenceRuleSets { get; } = new List<ClassSequenceRuleSet>();

        public ICoverageFormat Coverage { get; }

        public IClassDefinition ClassDef { get; }

        public SequenceContextFormat2(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            _ = reader.ReadUShort();
            ushort coverageOffset = reader.ReadUShort();
            ushort classDefOffset = reader.ReadUShort();
            ushort classSequenceRuleSetCount = reader.ReadUShort();
            ushort[] classSequenceRuleSetOffsets = reader.ReadUShortArray(classSequenceRuleSetCount);

            for (var i = 0; i < classSequenceRuleSetCount; i++)
            {
                if (classSequenceRuleSetOffsets[i] == 0)
                {
                    continue;
                }
                reader.Seek(startOfTable + classSequenceRuleSetOffsets[i]);
                ClassSequenceRuleSets.Add(new ClassSequenceRuleSet(reader));
            }
            reader.Seek(startOfTable + coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
            reader.Seek(startOfTable + classDefOffset);
            byte classDefFormat = reader.PeekBytes(2)[1];
            ClassDef = classDefFormat switch
            {
                1 => new ClassDefinition.ClassDefinitionFormat1(reader),
                2 => new ClassDefinition.ClassDefinitionFormat2(reader),
                _ => ClassDef
            };
        }
    }
}