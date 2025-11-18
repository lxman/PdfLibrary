using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gpos
{
    public class SinglePosFormat2Table : ISinglePosFormatTable
    {
        public ushort PosFormat { get; }

        public ICoverageFormat Coverage { get; }

        public ValueFormat ValueFormat { get; }

        public ValueRecord[] ValueRecords { get; }

        public SinglePosFormat2Table(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            PosFormat = reader.ReadUShort();
            ushort coverageOffset = reader.ReadUShort();
            ValueFormat = (ValueFormat)reader.ReadUShort();
            ushort valueCount = reader.ReadUShort();

            ValueRecords = new ValueRecord[valueCount];
            for (var i = 0; i < valueCount; i++)
            {
                ValueRecords[i] = new ValueRecord(ValueFormat, reader);
            }
            reader.Seek(coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
        }
    }
}