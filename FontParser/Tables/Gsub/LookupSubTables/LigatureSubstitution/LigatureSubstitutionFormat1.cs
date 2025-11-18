using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gsub.LookupSubTables.LigatureSubstitution
{
    public class LigatureSubstitutionFormat1 : ILookupSubTable
    {
        public ICoverageFormat Coverage { get; }

        public List<LigatureSet> LigatureSets { get; } = new List<LigatureSet>();

        public LigatureSubstitutionFormat1(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            _ = reader.ReadUShort();
            ushort coverageOffset = reader.ReadUShort();
            ushort ligSetCount = reader.ReadUShort();
            ushort[] ligSetOffsets = reader.ReadUShortArray(ligSetCount);
            for (var i = 0; i < ligSetCount; i++)
            {
                reader.Seek(startOfTable + ligSetOffsets[i]);
                LigatureSets.Add(new LigatureSet(reader));
            }
            reader.Seek(startOfTable + coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
        }
    }
}