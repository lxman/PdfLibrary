using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Math
{
    public class MathTopAccentAttachment
    {
        public ICoverageFormat TopAccentCoverage { get; }

        public List<MathValueRecord> TopAccentAttachments { get; } = new List<MathValueRecord>();

        public MathTopAccentAttachment(BigEndianReader reader)
        {
            long position = reader.Position;

            ushort topAccentCoverageOffset = reader.ReadUShort();

            ushort topAccentAttachmentCount = reader.ReadUShort();

            for (var i = 0; i < topAccentAttachmentCount; i++)
            {
                TopAccentAttachments.Add(new MathValueRecord(reader, position));
            }

            reader.Seek(position + topAccentCoverageOffset);
            TopAccentCoverage = CoverageTable.Retrieve(reader);
        }
    }
}