using FontParser.Reader;

namespace FontParser.Tables.Common.CoverageFormat
{
    public class CoverageFormat2 : ICoverageFormat
    {
        public ushort Format => 2;

        public RangeRecord[] RangeRecords { get; }

        public CoverageFormat2(BigEndianReader reader)
        {
            _ = reader.ReadUShort(); // Skip format
            ushort rangeCount = reader.ReadUShort();
            RangeRecords = new RangeRecord[rangeCount];
            for (var i = 0; i < rangeCount; i++)
            {
                RangeRecords[i] = new RangeRecord(reader.ReadBytes(6));
            }
        }
    }
}