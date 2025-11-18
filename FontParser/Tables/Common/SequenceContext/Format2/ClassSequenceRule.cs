using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common.SequenceContext.Format1;

namespace FontParser.Tables.Common.SequenceContext.Format2
{
    public class ClassSequenceRule
    {
        public ushort[]? InputSequences { get; }

        public List<SequenceLookup> SequenceLookups { get; } = new List<SequenceLookup>();

        public ClassSequenceRule(BigEndianReader reader)
        {
            ushort glyphCount = reader.ReadUShort();
            ushort sequenceLookupCount = reader.ReadUShort();
            if (glyphCount == 0 && sequenceLookupCount == 0)
            {
                return;
            }

            InputSequences = new ushort[glyphCount - 1];
            for (var i = 0; i < glyphCount - 1; i++)
            {
                InputSequences[i] = reader.ReadUShort();
            }
            for (var i = 0; i < sequenceLookupCount; i++)
            {
                SequenceLookups.Add(new SequenceLookup(reader.ReadBytes(4)));
            }
        }
    }
}