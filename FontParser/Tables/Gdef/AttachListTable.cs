using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gdef
{
    public class AttachListTable
    {
        public ICoverageFormat Coverage { get; }

        public List<ushort> AttachPointOffsets { get; } = new List<ushort>();

        public AttachListTable(BigEndianReader reader)
        {
            long position = reader.Position;
            ushort coverageOffset = reader.ReadUShort();
            reader.Seek(position + coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
            ushort glyphCount = reader.ReadUShort();
            for (var i = 0; i < glyphCount; i++)
            {
                AttachPointOffsets.Add(reader.ReadUShort());
            }
        }
    }
}