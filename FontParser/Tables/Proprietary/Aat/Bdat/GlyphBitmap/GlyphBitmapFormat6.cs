using FontParser.Reader;
using FontParser.Tables.Bitmap.Common;

namespace FontParser.Tables.Proprietary.Aat.Bdat.GlyphBitmap
{
    public class GlyphBitmapFormat6 : IGlyphBitmap
    {
        public BigGlyphMetricsRecord BigGlyphMetrics { get; }

        public byte[] ImageData { get; }

        public GlyphBitmapFormat6(BigEndianReader reader)
        {
            BigGlyphMetrics = new BigGlyphMetricsRecord(reader);
            ImageData = reader.ReadBytes(reader.BytesRemaining);
        }
    }
}