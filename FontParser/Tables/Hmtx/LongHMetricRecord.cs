using FontParser.Reader;

namespace FontParser.Tables.Hmtx
{
    public class LongHMetricRecord
    {
        public static long RecordSize => 4;

        public ushort AdvanceWidth { get; }

        public short LeftSideBearing { get; }

        public LongHMetricRecord(
            BigEndianReader reader,
            ushort? advanceWidth = null,
            short? leftSideBearing = null)
        {
            AdvanceWidth = reader.BytesRemaining > 0 ? reader.ReadUShort() : advanceWidth ?? 0;
            LeftSideBearing = reader.BytesRemaining > 0 ? reader.ReadShort() : leftSideBearing ?? 0;
        }
    }
}