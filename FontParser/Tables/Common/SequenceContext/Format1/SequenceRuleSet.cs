using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Common.SequenceContext.Format1
{
    public class SequenceRuleSet
    {
        public List<SequenceRule> SequenceRules { get; } = new List<SequenceRule>();

        public SequenceRuleSet(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            ushort ruleCount = reader.ReadUShort();
            ushort[] ruleOffsets = reader.ReadUShortArray(ruleCount);
            for (var i = 0; i < ruleCount; i++)
            {
                reader.Seek(startOfTable + ruleOffsets[i]);
                SequenceRules.Add(new SequenceRule(reader));
            }
        }
    }
}