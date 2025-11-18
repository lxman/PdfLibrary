using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Common
{
    public class BigGlyphMetricsRecord
    {
        public byte Height { get; }

        public byte Width { get; }

        public sbyte HorizontalBearingX { get; }

        public sbyte HorizontalBearingY { get; }

        public byte HorizontalAdvance { get; }

        public sbyte VerticalBearingX { get; }

        public sbyte VerticalBearingY { get; }

        public byte VerticalAdvance { get; }

        public BigGlyphMetricsRecord(BigEndianReader reader)
        {
            Height = reader.ReadByte();
            Width = reader.ReadByte();
            HorizontalBearingX = reader.ReadSByte();
            HorizontalBearingY = reader.ReadSByte();
            HorizontalAdvance = reader.ReadByte();
            VerticalBearingX = reader.ReadSByte();
            VerticalBearingY = reader.ReadSByte();
            VerticalAdvance = reader.ReadByte();
        }
    }
}