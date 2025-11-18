using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Bdat.GlyphBitmap
{
    internal class GlyphBitmapFormat5 : IGlyphBitmap
    {
        public byte[] ImageData { get; }

        public GlyphBitmapFormat5(BigEndianReader reader)
        {
            ImageData = reader.ReadBytes(reader.BytesRemaining);
        }
    }
}