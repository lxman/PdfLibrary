using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Common.ChainedSequenceContext.Format1
{
    public class ChainedSequenceRuleSet
    {
        public List<ChainedSequenceRule> ChainedSequenceRules { get; } = new List<ChainedSequenceRule>();

        public ChainedSequenceRuleSet(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            ushort chainedSequenceRuleCount = reader.ReadUShort();
            ushort[] chainedSequenceRuleOffsets = reader.ReadUShortArray(chainedSequenceRuleCount);
            for (var i = 0; i < chainedSequenceRuleCount; i++)
            {
                reader.Seek(startOfTable + chainedSequenceRuleOffsets[i]);
                ChainedSequenceRules.Add(new ChainedSequenceRule(reader));
            }
        }
    }
}