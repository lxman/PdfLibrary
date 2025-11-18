using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gpos
{
    public class SinglePosFormat1Table : ISinglePosFormatTable
    {
        public ushort PosFormat { get; }

        public ICoverageFormat Coverage { get; }

        public ValueFormat ValueFormat { get; }

        public ValueRecord ValueRecord { get; }

        public SinglePosFormat1Table(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            PosFormat = reader.ReadUShort();
            ushort coverageOffset = reader.ReadUShort();
            ValueFormat = (ValueFormat)reader.ReadUShort();
            ValueRecord = new ValueRecord(ValueFormat, reader);
            reader.Seek(coverageOffset);
            Coverage = CoverageTable.Retrieve(reader);
        }
    }
}