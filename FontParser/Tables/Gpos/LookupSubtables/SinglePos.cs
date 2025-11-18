using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gpos.LookupSubtables
{
    public class SinglePos : ILookupSubTable
    {
        public ushort Format { get; }

        public ICoverageFormat? Coverage { get; }

        public ValueFormat ValueFormat { get; }

        public ValueRecord? ValueRecord { get; }

        public List<ValueRecord>? ValueRecords { get; }

        public SinglePos(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            Format = reader.ReadUShort();
            ushort coverageOffset = reader.ReadUShort();
            ValueFormat = (ValueFormat)reader.ReadUShort();
            if (Format == 1)
            {
                ValueRecord = new ValueRecord(ValueFormat, reader);
                return;
            }
            ushort valueCount = reader.ReadUShort();
            ValueRecords = new List<ValueRecord>();
            for (var i = 0; i < valueCount; i++)
            {
                ValueRecords.Add(new ValueRecord(ValueFormat, reader));
            }
            reader.Seek(startOfTable + coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
        }
    }
}