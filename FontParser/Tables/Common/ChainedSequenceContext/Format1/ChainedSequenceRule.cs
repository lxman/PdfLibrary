using System;
using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common.SequenceContext.Format1;

namespace FontParser.Tables.Common.ChainedSequenceContext.Format1
{
    public class ChainedSequenceRule
    {
        public ushort[] BacktrackSequence { get; }

        public ushort[] InputSequence { get; }

        public ushort[] LookaheadSequence { get; }

        public List<SequenceLookup> SequenceLookups { get; } = new List<SequenceLookup>();

        public ChainedSequenceRule(BigEndianReader reader)
        {
            ushort backtrackGlyphCount = reader.ReadUShort();
            BacktrackSequence = reader.ReadUShortArray(backtrackGlyphCount);
            ushort inputGlyphCount = reader.ReadUShort();
            InputSequence = reader.ReadUShortArray(Convert.ToUInt32(inputGlyphCount - 1));
            ushort lookaheadGlyphCount = reader.ReadUShort();
            LookaheadSequence = reader.ReadUShortArray(lookaheadGlyphCount);
            ushort seqLookupCount = reader.ReadUShort();
            for (var i = 0; i < seqLookupCount; i++)
            {
                SequenceLookups.Add(new SequenceLookup(reader.ReadBytes(4)));
            }
        }
    }
}