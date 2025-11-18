using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Common.ChainedSequenceContext.Format1
{
    public class ChainedSequenceContextFormat1 : ILookupSubTable, IChainedSequenceContext
    {
        public ICoverageFormat CoverageFormat { get; }

        public List<ChainedSequenceRuleSet> ChainedSequenceRuleSets { get; } = new List<ChainedSequenceRuleSet>();

        public ChainedSequenceContextFormat1(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            _ = reader.ReadUShort();
            ushort coverageOffset = reader.ReadUShort();
            ushort chainedSequenceRuleSetCount = reader.ReadUShort();
            ushort[] chainedSequenceRuleSetOffsets = reader.ReadUShortArray(chainedSequenceRuleSetCount);
            for (var i = 0; i < chainedSequenceRuleSetCount; i++)
            {
                reader.Seek(startOfTable + chainedSequenceRuleSetOffsets[i]);
                ChainedSequenceRuleSets.Add(new ChainedSequenceRuleSet(reader));
            }
            reader.Seek(startOfTable + coverageOffset);
            CoverageFormat = CoverageTable.Retrieve(reader);
        }
    }
}