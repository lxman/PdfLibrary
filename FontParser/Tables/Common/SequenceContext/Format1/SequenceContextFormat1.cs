using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Common.SequenceContext.Format1
{
    public class SequenceContextFormat1 : ILookupSubTable, ISequenceContext
    {
        public ICoverageFormat Coverage { get; }

        public List<SequenceRuleSet> SequenceRuleSets { get; } = new List<SequenceRuleSet>();

        public SequenceContextFormat1(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            _ = reader.ReadUShort();
            ushort coverageOffset = reader.ReadUShort();
            ushort ruleSetCount = reader.ReadUShort();
            ushort[] ruleSetOffsets = reader.ReadUShortArray(ruleSetCount);
            for (var i = 0; i < ruleSetCount; i++)
            {
                reader.Seek(startOfTable + ruleSetOffsets[i]);
                SequenceRuleSets.Add(new SequenceRuleSet(reader));
            }

            reader.Seek(startOfTable + coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
        }
    }
}