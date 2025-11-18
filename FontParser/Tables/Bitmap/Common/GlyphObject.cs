using FontParser.Tables.Bitmap.Common.GlyphBitmapData;

namespace FontParser.Tables.Bitmap.Common
{
    public class GlyphObject
    {
        public ushort GlyphId { get; }

        public IGlyphBitmapData BitmapData { get; }

        public GlyphObject(ushort glyphId, IGlyphBitmapData bitmapData)
        {
            GlyphId = glyphId;
            BitmapData = bitmapData;
        }
    }
}