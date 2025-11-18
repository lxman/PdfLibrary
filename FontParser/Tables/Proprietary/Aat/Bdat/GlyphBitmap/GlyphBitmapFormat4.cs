using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Bdat.GlyphBitmap
{
    public class GlyphBitmapFormat4 : IGlyphBitmap
    {
        public uint WhiteTreeOffset { get; }

        public uint BlackTreeOffset { get; }

        public uint GlyphDataOffset { get; }

        public GlyphBitmapFormat4(BigEndianReader reader)
        {
            WhiteTreeOffset = reader.ReadUInt32();
            BlackTreeOffset = reader.ReadUInt32();
            GlyphDataOffset = reader.ReadUInt32();
        }
    }
}