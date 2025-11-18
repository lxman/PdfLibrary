using FontParser.Reader;

namespace FontParser.Tables.Optional
{
    public class VerticalMetricsEntry
    {
        public ushort AdvanceHeight { get; }

        public short TopSideBearing { get; }

        public VerticalMetricsEntry(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            AdvanceHeight = reader.ReadUShort();
            TopSideBearing = reader.ReadShort();
        }
    }
}