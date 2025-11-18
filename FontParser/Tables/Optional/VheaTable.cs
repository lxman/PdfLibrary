using FontParser.Reader;

namespace FontParser.Tables.Optional
{
    public class VheaTable : IFontTable
    {
        public static string Tag => "vhea";

        public ushort MajorVersion { get; set; }

        public ushort MinorVersion { get; set; }

        public short Ascender { get; set; }

        public short VertTypoAscender => Ascender;

        public short Descender { get; set; }

        public short VertTypoDescender => Descender;

        public short LineGap { get; set; }

        public short VertTypoLineGap => LineGap;

        public ushort AdvanceHeightMax { get; set; }

        public short MinTopSideBearing { get; set; }

        public short MinBottomSideBearing { get; set; }

        public short YMaxExtent { get; set; }

        public short CaretSlopeRise { get; set; }

        public short CaretSlopeRun { get; set; }

        public short CaretOffset { get; set; }

        public short Reserved1 { get; set; }

        public short Reserved2 { get; set; }

        public short Reserved3 { get; set; }

        public short Reserved4 { get; set; }

        public short MetricDataFormat { get; set; }

        public ushort NumberOfLongVerMetrics { get; set; }

        public VheaTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            Ascender = reader.ReadShort();
            Descender = reader.ReadShort();
            LineGap = reader.ReadShort();
            AdvanceHeightMax = reader.ReadUShort();
            MinTopSideBearing = reader.ReadShort();
            MinBottomSideBearing = reader.ReadShort();
            YMaxExtent = reader.ReadShort();
            CaretSlopeRise = reader.ReadShort();
            CaretSlopeRun = reader.ReadShort();
            CaretOffset = reader.ReadShort();
            Reserved1 = reader.ReadShort();
            Reserved2 = reader.ReadShort();
            Reserved3 = reader.ReadShort();
            Reserved4 = reader.ReadShort();
            MetricDataFormat = reader.ReadShort();
            NumberOfLongVerMetrics = reader.ReadUShort();
        }
    }
}