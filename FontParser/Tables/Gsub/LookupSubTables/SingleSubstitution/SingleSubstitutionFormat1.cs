using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gsub.LookupSubTables.SingleSubstitution
{
    public class SingleSubstitutionFormat1 : ILookupSubTable
    {
        public ICoverageFormat Coverage { get; }

        public ushort DeltaGlyphId { get; }

        public SingleSubstitutionFormat1(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            _ = reader.ReadUShort();
            ushort coverageOffset = reader.ReadUShort();
            DeltaGlyphId = reader.ReadUShort();
            reader.Seek(startOfTable + coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
        }
    }
}