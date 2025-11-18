using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Math
{
    public class MathVariantsTable
    {
        public ushort MinConnectorOverlap { get; }

        public ICoverageFormat? VertGlyphCoverage { get; }

        public ICoverageFormat? HorizGlyphCoverage { get; }

        public List<MathGlyphConstructionTable> VertGlyphConstruction { get; } = new List<MathGlyphConstructionTable>();

        public List<MathGlyphConstructionTable> HorizGlyphConstruction { get; } = new List<MathGlyphConstructionTable>();

        public MathVariantsTable(BigEndianReader reader)
        {
            long position = reader.Position;

            MinConnectorOverlap = reader.ReadUShort();

            ushort vertGlyphCoverageOffset = reader.ReadUShort();
            ushort horizGlyphCoverageOffset = reader.ReadUShort();

            ushort vertGlyphCount = reader.ReadUShort();
            ushort horizGlyphCount = reader.ReadUShort();

            ushort[] vertGlyphConstructionOffsets = reader.ReadUShortArray(vertGlyphCount);
            ushort[] horizGlyphConstructionOffsets = reader.ReadUShortArray(horizGlyphCount);

            if (vertGlyphCoverageOffset > 0)
            {
                reader.Seek(position + vertGlyphCoverageOffset);
                VertGlyphCoverage = CoverageTable.Retrieve(reader);
            }

            if (horizGlyphCoverageOffset > 0)
            {
                reader.Seek(position + horizGlyphCoverageOffset);
                HorizGlyphCoverage = CoverageTable.Retrieve(reader);
            }

            for (var i = 0; i < vertGlyphCount; i++)
            {
                reader.Seek(position + vertGlyphConstructionOffsets[i]);
                VertGlyphConstruction.Add(new MathGlyphConstructionTable(reader));
            }

            for (var i = 0; i < horizGlyphCount; i++)
            {
                reader.Seek(position + horizGlyphConstructionOffsets[i]);
                HorizGlyphConstruction.Add(new MathGlyphConstructionTable(reader));
            }
        }
    }
}