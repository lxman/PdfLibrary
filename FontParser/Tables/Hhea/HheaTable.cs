using FontParser.Reader;

namespace FontParser.Tables.Hhea
{
    public class HheaTable : IFontTable
    {
        public static string Tag => "hhea";

        public string Version => $"{_majorVersion}.{_minorVersion}";

        public short Ascender { get; }

        public short Descender { get; }

        public short LineGap { get; }

        public ushort AdvanceWidthMax { get; }

        public short MinLeftSideBearing { get; }

        public short MinRightSideBearing { get; }

        public short XMaxExtent { get; }

        public short CaretSlopeRise { get; }

        public short CaretSlopeRun { get; }

        public short CaretOffset { get; }

        public short MetricDataFormat { get; }

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
            _ = reader.ReadShort();
            _ = reader.ReadShort();
            _ = reader.ReadShort();
            _ = reader.ReadShort();
            MetricDataFormat = reader.ReadShort();
            NumberOfHMetrics = reader.ReadUShort();
        }
    }
}