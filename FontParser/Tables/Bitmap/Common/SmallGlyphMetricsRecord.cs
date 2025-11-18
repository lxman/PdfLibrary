using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Common
{
    public class SmallGlyphMetricsRecord
    {
        public byte Height { get; }

        public byte Width { get; }

        public sbyte BearingX { get; }

        public sbyte BearingY { get; }

        public byte Advance { get; }

        public SmallGlyphMetricsRecord(BigEndianReader reader)
        {
            Height = reader.ReadByte();
            Width = reader.ReadByte();
            BearingX = reader.ReadSByte();
            BearingY = reader.ReadSByte();
            Advance = reader.ReadByte();
        }
    }
}