using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;
using FontParser.Tables.Gpos.LookupSubtables.Common;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace FontParser.Tables.Gpos.LookupSubtables.MarkLigPos
{
    public class MarkLigPosFormat1 : ILookupSubTable
    {
        public ushort Format { get; }

        public ICoverageFormat MarkCoverage { get; }

        public ICoverageFormat LigatureCoverage { get; }

        public MarkArray MarkArray { get; }

        public LigatureArrayTable? LigatureArrayTable { get; }

        public MarkLigPosFormat1(BigEndianReader reader)
        {
            long startOfTable = reader.Position;

            Format = reader.ReadUShort();
            ushort markCoverageOffset = reader.ReadUShort();
            ushort ligatureCoverageOffset = reader.ReadUShort();
            ushort markClassCount = reader.ReadUShort();
            ushort markArrayOffset = reader.ReadUShort();
            ushort ligatureArrayOffset = reader.ReadUShort();

            if (ligatureArrayOffset == 0) return;
            reader.Seek(startOfTable + ligatureArrayOffset);
            LigatureArrayTable = new LigatureArrayTable(reader, markClassCount);
            reader.Seek(startOfTable + markCoverageOffset);
            MarkCoverage = CoverageTable.Retrieve(reader);
            reader.Seek(startOfTable + ligatureCoverageOffset);
            LigatureCoverage = CoverageTable.Retrieve(reader);
            reader.Seek(startOfTable + markArrayOffset);
            MarkArray = new MarkArray(reader);
        }
    }
}