using System;
using FontParser.Reader;

namespace FontParser.Tables.WOFF2
{
    public class TransformedHmtxTable
    {
        public ushort[] AdvanceWidth { get; }

        public short[]? Lsb { get; }

        public short[]? LeftSideBearing { get; }

        public TransformedHmtxTable(
            BigEndianReader reader,
            ushort numOfHMetrics,
            ushort glyphCount)
        {
            byte flags = reader.ReadByte();
            bool lsbPresent = (flags & 0x01) == 0;
            bool leftSideBearingPresent = (flags & 0x02) == 0;
            AdvanceWidth = reader.ReadUShortArray(numOfHMetrics);
            if (lsbPresent)
            {
                Lsb = reader.ReadShortArray(numOfHMetrics);
            }
            if (leftSideBearingPresent)
            {
                LeftSideBearing = reader.ReadShortArray(Convert.ToUInt32(glyphCount - numOfHMetrics));
            }
        }
    }
}