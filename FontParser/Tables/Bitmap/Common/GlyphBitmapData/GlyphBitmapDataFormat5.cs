using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Common.GlyphBitmapData
{
    public class GlyphBitmapDataFormat5 : IGlyphBitmapData
    {
        public byte[] Data { get; set; }

        public GlyphBitmapDataFormat5(BigEndianReader reader)
        {
            // TODO: Figure out how to read the data
            Data = reader.ReadBytes(0);
        }
    }
}