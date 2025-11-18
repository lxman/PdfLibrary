using FontParser.Reader;
using FontParser.Tables.Bitmap.Common;

namespace FontParser.Tables.Proprietary.Aat.Bloc.BitmapIndexSubtable
{
    public class BitmapIndexSubtableFormat2 : IBitmapIndexSubtable
    {
        public IndexFormat IndexFormat { get; }

        public ImageFormat ImageFormat { get; }

        public BigGlyphMetricsRecord BigGlyphMetrics { get; }

        public BitmapIndexSubtableFormat2(BigEndianReader reader)
        {
            IndexFormat = (IndexFormat)reader.ReadUShort();
            ImageFormat = (ImageFormat)reader.ReadUShort();
            uint imageDataOffset = reader.ReadUInt32();
            uint imageSize = reader.ReadUInt32();
            BigGlyphMetrics = new BigGlyphMetricsRecord(reader);
        }
    }
}