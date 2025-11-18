using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gdef
{
    public class LigCaretListTable
    {
        public ICoverageFormat Coverage { get; }

        public List<LigGlyphTable> LigGlyphOffsets { get; } = new List<LigGlyphTable>();

        public LigCaretListTable(BigEndianReader reader)
        {
            long startOfTable = reader.Position;

            ushort coverageOffset = reader.ReadUShort();
            ushort ligGlyphCount = reader.ReadUShort();
            ushort[] ligGlyphOffsets = reader.ReadUShortArray(ligGlyphCount);
            for (var i = 0; i < ligGlyphCount; i++)
            {
                if (ligGlyphOffsets[i] == 0) continue;
                reader.Seek(startOfTable + ligGlyphOffsets[i]);
                LigGlyphOffsets.Add(new LigGlyphTable(reader));
            }
            reader.Seek(startOfTable + coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
        }
    }
}