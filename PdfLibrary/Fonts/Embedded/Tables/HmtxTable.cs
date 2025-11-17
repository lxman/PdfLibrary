namespace PdfLibrary.Fonts.Embedded.Tables
{
    /// <summary>
    /// TrueType 'hmtx' table parser - horizontal metrics for all glyphs
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class HmtxTable
    {
        public static string Tag => "hmtx";

        public List<LongHMetricRecord> LongHMetricRecords { get; } = new List<LongHMetricRecord>();
        public List<short> LeftSideBearings { get; } = new List<short>();

        private readonly BigEndianReader _reader;

        public HmtxTable(byte[] data)
        {
            _reader = new BigEndianReader(data);
        }

        /// <summary>
        /// Process the hmtx table data
        /// </summary>
        /// <param name="numberOfHMetricRecords">From the 'hhea' table NumberOfHMetrics</param>
        /// <param name="numOfGlyphs">Total number of glyphs in the font (from 'maxp' table, or estimated)</param>
        public void Process(ushort numberOfHMetricRecords, ushort numOfGlyphs)
        {
            // Read the long horizontal metric records
            for (var i = 0; i < numberOfHMetricRecords; i++)
            {
                LongHMetricRecords.Add(new LongHMetricRecord(_reader));
            }

            // Read remaining left side bearings (if any)
            // These glyphs share the advance width of the last LongHMetricRecord
            if (LongHMetricRecords.Count < numOfGlyphs)
            {
                for (int i = LongHMetricRecords.Count; i < numOfGlyphs; i++)
                {
                    LeftSideBearings.Add(_reader.ReadShort());
                }
            }
        }

        /// <summary>
        /// Get the advance width for a specific glyph ID
        /// </summary>
        public ushort GetAdvanceWidth(int glyphId)
        {
            if (glyphId < LongHMetricRecords.Count)
            {
                return LongHMetricRecords[glyphId].AdvanceWidth;
            }
            else if (LongHMetricRecords.Count > 0)
            {
                // Glyphs beyond numberOfHMetrics share the advance width of the last metric
                return LongHMetricRecords[LongHMetricRecords.Count - 1].AdvanceWidth;
            }
            return 0;
        }

        /// <summary>
        /// Get the left side bearing for a specific glyph ID
        /// </summary>
        public short GetLeftSideBearing(int glyphId)
        {
            if (glyphId < LongHMetricRecords.Count)
            {
                return LongHMetricRecords[glyphId].LeftSideBearing;
            }
            else
            {
                int lsbIndex = glyphId - LongHMetricRecords.Count;
                if (lsbIndex >= 0 && lsbIndex < LeftSideBearings.Count)
                {
                    return LeftSideBearings[lsbIndex];
                }
            }
            return 0;
        }
    }

    /// <summary>
    /// Horizontal metric record containing advance width and left side bearing
    /// </summary>
    public class LongHMetricRecord
    {
        public static long RecordSize => 4;

        /// <summary>
        /// Advance width in font design units
        /// </summary>
        public ushort AdvanceWidth { get; }

        /// <summary>
        /// Left side bearing in font design units
        /// </summary>
        public short LeftSideBearing { get; }

        public LongHMetricRecord(BigEndianReader reader)
        {
            AdvanceWidth = reader.BytesRemaining > 0 ? reader.ReadUShort() : (ushort)0;
            LeftSideBearing = reader.BytesRemaining > 0 ? reader.ReadShort() : (short)0;
        }
    }
}
