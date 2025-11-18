using FontParser.Reader;

namespace FontParser.Tables.Common.CoverageFormat
{
    public class RangeRecord
    {
        public ushort Start { get; }
        public ushort End { get; }
        public ushort StartCoverageIndex { get; }

        public RangeRecord(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            Start = reader.ReadUShort();
            End = reader.ReadUShort();
            StartCoverageIndex = reader.ReadUShort();
        }
    }
}