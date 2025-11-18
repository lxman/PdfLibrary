using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gdef
{
    public class MarkGlyphSetsTable
    {
        public readonly ushort Format;

        public readonly List<ICoverageFormat> MarkSetTables = new List<ICoverageFormat>();

        public MarkGlyphSetsTable(BigEndianReader reader)
        {
            long position = reader.Position;

            Format = reader.ReadUShort();
            ushort markSetCount = reader.ReadUShort();
            var markSetOffsets = new uint[markSetCount];
            for (var i = 0; i < markSetCount; i++)
            {
                markSetOffsets[i] = reader.ReadUInt32();
            }
            for (var i = 0; i < markSetCount; i++)
            {
                reader.Seek(position + markSetOffsets[i]);
                MarkSetTables.Add(CoverageTable.Retrieve(reader));
            }
        }
    }
}