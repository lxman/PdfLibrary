using System;
using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;
using FontParser.Tables.Common.SequenceContext.Format1;

namespace FontParser.Tables.Common.ChainedSequenceContext.Format2
{
    public class ChainedClassSequenceRule
    {
        public List<ushort> BacktrackSequences { get; }

        public List<ushort> InputSequences { get; }

        public List<ushort> LookaheadSequences { get; }

        public List<SequenceLookup> SequenceLookups { get; } = new List<SequenceLookup>();

        public ChainedClassSequenceRule(BigEndianReader reader)
        {
            ushort backtrackGlyphCount = reader.ReadUShort();
            BacktrackSequences = reader.ReadUShortArray(backtrackGlyphCount).ToList();
            ushort inputGlyphCount = reader.ReadUShort();
            InputSequences = reader.ReadUShortArray(Convert.ToUInt32(inputGlyphCount - 1)).ToList();
            ushort lookaheadGlyphCount = reader.ReadUShort();
            LookaheadSequences = reader.ReadUShortArray(lookaheadGlyphCount).ToList();
            ushort seqLookupCount = reader.ReadUShort();
            for (var i = 0; i < seqLookupCount; i++)
            {
                SequenceLookups.Add(new SequenceLookup(reader.ReadBytes(4)));
            }
        }
    }
}