using FontParser.Reader;
using FontParser.Tables.Bitmap.Common;

namespace FontParser.Tables.Proprietary.Aat.Bdat.GlyphBitmap
{
    public class GlyphBitmapFormat7 : IGlyphBitmap
    {
        public BigGlyphMetricsRecord BigGlyphMetrics { get; }

        public byte[] BitmapData { get; }

        public GlyphBitmapFormat7(BigEndianReader reader)
        {
            BigGlyphMetrics = new BigGlyphMetricsRecord(reader);
            BitmapData = reader.ReadBytes(reader.BytesRemaining);
        }
    }
}