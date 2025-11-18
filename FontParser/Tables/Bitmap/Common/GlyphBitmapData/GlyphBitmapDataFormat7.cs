using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Common.GlyphBitmapData
{
    public class GlyphBitmapDataFormat7 : IGlyphBitmapData
    {
        public BigGlyphMetricsRecord BigGlyphMetrics { get; }

        public byte[] BitmapData { get; }

        public GlyphBitmapDataFormat7(BigEndianReader reader, uint dataSize)
        {
            BigGlyphMetrics = new BigGlyphMetricsRecord(reader);
            BitmapData = reader.ReadBytes(dataSize);
        }
    }
}