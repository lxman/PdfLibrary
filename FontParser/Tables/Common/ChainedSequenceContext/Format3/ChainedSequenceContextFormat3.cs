using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;
using FontParser.Tables.Common.CoverageFormat;
using FontParser.Tables.Common.SequenceContext.Format1;

namespace FontParser.Tables.Common.ChainedSequenceContext.Format3
{
    public class ChainedSequenceContextFormat3 : ILookupSubTable, IChainedSequenceContext
    {
        public List<ICoverageFormat> BacktrackCoverages { get; } = new List<ICoverageFormat>();

        public List<ICoverageFormat> InputCoverages { get; } = new List<ICoverageFormat>();

        public List<ICoverageFormat> LookaheadCoverages { get; } = new List<ICoverageFormat>();

        public List<SequenceLookup> SequenceLookups { get; } = new List<SequenceLookup>();

        public ChainedSequenceContextFormat3(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            _ = reader.ReadUShort();
            ushort backtrackGlyphCount = reader.ReadUShort();
            List<ushort> backtrackCoverageOffsets = reader.ReadUShortArray(backtrackGlyphCount).ToList();
            ushort inputGlyphCount = reader.ReadUShort();
            List<ushort> inputCoverageOffsets = reader.ReadUShortArray(inputGlyphCount).ToList();
            ushort lookaheadGlyphCount = reader.ReadUShort();
            List<ushort> lookaheadCoverageOffsets = reader.ReadUShortArray(lookaheadGlyphCount).ToList();
            ushort sequenceLookupCount = reader.ReadUShort();
            for (var i = 0; i < sequenceLookupCount; i++)
            {
                SequenceLookups.Add(new SequenceLookup(reader.ReadBytes(4)));
            }

            long before = reader.Position;
            backtrackCoverageOffsets.ForEach(co =>
            {
                reader.Seek(startOfTable + co);
                BacktrackCoverages.Add(CoverageTable.Retrieve(reader));
            });
            inputCoverageOffsets.ForEach(ic =>
            {
                reader.Seek(startOfTable + ic);
                InputCoverages.Add(CoverageTable.Retrieve(reader));
            });
            lookaheadCoverageOffsets.ForEach(lc =>
            {
                reader.Seek(startOfTable + lc);
                LookaheadCoverages.Add(CoverageTable.Retrieve(reader));
            });
            reader.Seek(before);
        }
    }
}