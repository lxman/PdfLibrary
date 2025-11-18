using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Common.SequenceContext.Format2
{
    public class ClassSequenceRuleSet
    {
        public List<ClassSequenceRule> ClassSeqRules { get; } = new List<ClassSequenceRule>();

        public ClassSequenceRuleSet(BigEndianReader reader)
        {
            long start = reader.Position;

            ushort classSeqRuleCount = reader.ReadUShort();
            ushort[] classSeqRuleOffsets = reader.ReadUShortArray(classSeqRuleCount);
            for (var i = 0; i < classSeqRuleCount; i++)
            {
                reader.Seek(start + classSeqRuleOffsets[i]);
                ClassSeqRules.Add(new ClassSequenceRule(reader));
            }
        }
    }
}