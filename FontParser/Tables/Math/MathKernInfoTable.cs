using System;
using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Math
{
    public class MathKernInfoTable
    {
        public ICoverageFormat MathKernCoverage { get; }

        public List<MathKernInfoRecord> MathKernInfoRecords { get; } = new List<MathKernInfoRecord>();

        public MathKernInfoTable(BigEndianReader reader)
        {
            long position = reader.Position;

            ushort mathKernCoverageOffset = reader.ReadUShort();

            ushort mathKernCount = reader.ReadUShort();

            ushort[] mathKernValues = reader.ReadUShortArray(Convert.ToUInt32(4 * mathKernCount));
            ushort mathKernIndex = 0;

            for (var i = 0; i < mathKernCount; i++)
            {
                MathKernInfoRecords.Add(new MathKernInfoRecord(reader, position, mathKernValues[mathKernIndex..(mathKernIndex + 4)]));
                mathKernIndex += 4;
            }

            reader.Seek(position + mathKernCoverageOffset);
            MathKernCoverage = CoverageTable.Retrieve(reader);
        }
    }
}