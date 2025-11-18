using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Common.GlyphBitmapData
{
    public class GlyphBitmapDataFormat2 : IGlyphBitmapData
    {
        public SmallGlyphMetricsRecord SmallGlyphMetrics { get; }

        public byte[] BitmapData { get; }

        public GlyphBitmapDataFormat2(BigEndianReader reader)
        {
            SmallGlyphMetrics = new SmallGlyphMetricsRecord(reader);
            // TODO: Figure out how to read the bitmap data
            BitmapData = reader.ReadBytes(SmallGlyphMetrics.Width * SmallGlyphMetrics.Height);
        }
    }
}