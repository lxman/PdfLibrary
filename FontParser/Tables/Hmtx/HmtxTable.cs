using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Hmtx
{
    public class HmtxTable : IFontTable
    {
        public static string Tag => "hmtx";

        public List<LongHMetricRecord> LongHMetricRecords { get; } = new List<LongHMetricRecord>();

        public List<short> LeftSideBearings { get; } = new List<short>();

        private readonly BigEndianReader _reader;

        public HmtxTable(byte[] data)
        {
            _reader = new BigEndianReader(data);
        }

        // numberOfHMetricRecords: From the 'hhea' table.
        // numOfGlyphs: From the 'maxp' table.
        public void Process(ushort numberOfHMetricRecords, ushort numOfGlyphs)
        {
            for (var i = 0; i < numberOfHMetricRecords; i++)
            {
                LongHMetricRecords.Add(new LongHMetricRecord(_reader));
            }

            if (LongHMetricRecords.Count >= numOfGlyphs) return;
            {
                for (int i = LongHMetricRecords.Count; i < numOfGlyphs; i++)
                {
                    LeftSideBearings.Add(_reader.ReadShort());
                }
            }
        }

        /// <summary>
        /// Gets the advance width for a glyph ID
        /// </summary>
        public ushort GetAdvanceWidth(ushort glyphId)
        {
            if (glyphId < LongHMetricRecords.Count)
            {
                return LongHMetricRecords[glyphId].AdvanceWidth;
            }

            // For glyphs beyond numberOfHMetrics, use the last advance width
            if (LongHMetricRecords.Count > 0)
            {
                return LongHMetricRecords[LongHMetricRecords.Count - 1].AdvanceWidth;
            }

            return 0;
        }

        /// <summary>
        /// Gets the left side bearing for a glyph ID
        /// </summary>
        public short GetLeftSideBearing(ushort glyphId)
        {
            if (glyphId < LongHMetricRecords.Count)
            {
                return LongHMetricRecords[glyphId].LeftSideBearing;
            }

            // For glyphs beyond numberOfHMetrics, use the LeftSideBearings array
            int lsbIndex = glyphId - LongHMetricRecords.Count;
            if (lsbIndex >= 0 && lsbIndex < LeftSideBearings.Count)
            {
                return LeftSideBearings[lsbIndex];
            }

            return 0;
        }
    }
}