using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Common.GlyphBitmapData
{
    public class GlyphBitmapDataFormat6 : IGlyphBitmapData
    {
        public BigGlyphMetricsRecord BigMetrics { get; }

        public byte[] BitmapData { get; }

        public GlyphBitmapDataFormat6(BigEndianReader reader)
        {
            BigMetrics = new BigGlyphMetricsRecord(reader);
            BitmapData = reader.ReadBytes(BigMetrics.Height * BigMetrics.Width);
        }
    }
}