using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Math
{
    public class MathItalicsCorrectionInfo
    {
        public ICoverageFormat Coverage { get; }

        public List<MathValueRecord> ItalicsCorrections { get; } = new List<MathValueRecord>();

        public MathItalicsCorrectionInfo(BigEndianReader reader)
        {
            long position = reader.Position;

            ushort italicCorrectionCoverageOffset = reader.ReadUShort();

            ushort italicsCorrectionCount = reader.ReadUShort();

            for (var i = 0; i < italicsCorrectionCount; i++)
            {
                ItalicsCorrections.Add(new MathValueRecord(reader, position));
            }

            reader.Seek(position + italicCorrectionCoverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
        }
    }
}