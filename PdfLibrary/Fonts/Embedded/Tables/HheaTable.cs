namespace PdfLibrary.Fonts.Embedded.Tables
{
    /// <summary>
    /// TrueType 'hhea' table parser - horizontal header with font metrics
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class HheaTable
    {
        public static string Tag => "hhea";

        public string Version => $"{_majorVersion}.{_minorVersion}";

        /// <summary>
        /// Typographic ascender - distance from baseline to top of font bounding box
        /// </summary>
        public short Ascender { get; }

        /// <summary>
        /// Typographic descender - distance from baseline to bottom of font bounding box (typically negative)
        /// </summary>
        public short Descender { get; }

        /// <summary>
        /// Typographic line gap - additional space between lines
        /// </summary>
        public short LineGap { get; }

        /// <summary>
        /// Maximum advance width for all glyphs in the font
        /// </summary>
        public ushort AdvanceWidthMax { get; }

        public short MinLeftSideBearing { get; }
        public short MinRightSideBearing { get; }
        public short XMaxExtent { get; }
        public short CaretSlopeRise { get; }
        public short CaretSlopeRun { get; }
        public short CaretOffset { get; }
        public short MetricDataFormat { get; }

        /// <summary>
        /// Number of horizontal metric entries in the 'hmtx' table
        /// This value is needed to properly parse the 'hmtx' table
        /// </summary>
        public ushort NumberOfHMetrics { get; }

        private readonly ushort _majorVersion;
        private readonly ushort _minorVersion;

        public HheaTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            _majorVersion = reader.ReadUShort();
            _minorVersion = reader.ReadUShort();
            Ascender = reader.ReadShort();
            Descender = reader.ReadShort();
            LineGap = reader.ReadShort();
            AdvanceWidthMax = reader.ReadUShort();
            MinLeftSideBearing = reader.ReadShort();
            MinRightSideBearing = reader.ReadShort();
            XMaxExtent = reader.ReadShort();
            CaretSlopeRise = reader.ReadShort();
            CaretSlopeRun = reader.ReadShort();
            CaretOffset = reader.ReadShort();

            // Skip 4 reserved shorts
            _ = reader.ReadShort();
            _ = reader.ReadShort();
            _ = reader.ReadShort();
            _ = reader.ReadShort();

            MetricDataFormat = reader.ReadShort();
            NumberOfHMetrics = reader.ReadUShort();
        }
    }
}
