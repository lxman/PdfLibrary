using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Common.GlyphBitmapData
{
    public class GlyphBitmapDataFormat1 : IGlyphBitmapData
    {
        public SmallGlyphMetricsRecord SmallGlyphMetrics { get; }

        public byte[] BitmapData { get; }

        public GlyphBitmapDataFormat1(BigEndianReader reader, uint dataSize)
        {
            SmallGlyphMetrics = new SmallGlyphMetricsRecord(reader);
            BitmapData = reader.ReadBytes(dataSize);
        }
    }
}