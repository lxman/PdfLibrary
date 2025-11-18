using FontParser.Reader;
using FontParser.Tables.Bitmap.Common;

namespace FontParser.Tables.Proprietary.Aat.Bdat.GlyphBitmap
{
    public class GlyphBitmapFormat1 : IGlyphBitmap
    {
        public SmallGlyphMetricsRecord SmallGlyphMetrics { get; }

        public byte[] ImageData { get; }

        public GlyphBitmapFormat1(BigEndianReader reader)
        {
            SmallGlyphMetrics = new SmallGlyphMetricsRecord(reader);
            ImageData = reader.ReadBytes(reader.BytesRemaining);
        }
    }
}