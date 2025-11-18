using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gsub.LookupSubTables.ReverseChainSingleSubstitution
{
    public class ReverseChainSingleSubstitutionFormat1 : ILookupSubTable
    {
        public ICoverageFormat Coverage { get; }

        public List<ICoverageFormat> BacktrackCoverages { get; } = new List<ICoverageFormat>();

        public List<ICoverageFormat> LookaheadCoverages { get; } = new List<ICoverageFormat>();

        public List<ushort> SubstituteGlyphIds { get; }

        public ReverseChainSingleSubstitutionFormat1(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            _ = reader.ReadUShort();
            ushort coverageOffset = reader.ReadUShort();
            ushort backtrackGlyphCount = reader.ReadUShort();
            ushort[] backtrackOffsets = reader.ReadUShortArray(backtrackGlyphCount);
            ushort lookaheadGlyphCount = reader.ReadUShort();
            ushort[] lookaheadOffsets = reader.ReadUShortArray(lookaheadGlyphCount);
            ushort substCount = reader.ReadUShort();
            SubstituteGlyphIds = reader.ReadUShortArray(substCount).ToList();
            for (var i = 0; i < backtrackGlyphCount; i++)
            {
                reader.Seek(startOfTable + backtrackOffsets[i]);
                BacktrackCoverages.Add(CoverageTable.Retrieve(reader));
            }
            for (var i = 0; i < lookaheadGlyphCount; i++)
            {
                reader.Seek(startOfTable + lookaheadOffsets[i]);
                LookaheadCoverages.Add(CoverageTable.Retrieve(reader));
            }
            reader.Seek(startOfTable + coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
        }
    }
}