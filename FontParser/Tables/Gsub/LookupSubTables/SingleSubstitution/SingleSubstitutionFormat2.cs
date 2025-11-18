using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gsub.LookupSubTables.SingleSubstitution
{
    public class SingleSubstitutionFormat2 : ILookupSubTable
    {
        public ICoverageFormat Coverage { get; }

        public ushort[] SubstituteGlyphIds { get; }

        public SingleSubstitutionFormat2(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            _ = reader.ReadUShort();
            ushort coverageOffset = reader.ReadUShort();
            ushort glyphCount = reader.ReadUShort();
            SubstituteGlyphIds = reader.ReadUShortArray(glyphCount);
            reader.Seek(startOfTable + coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
        }
    }
}