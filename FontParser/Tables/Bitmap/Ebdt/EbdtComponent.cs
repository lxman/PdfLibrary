using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Ebdt
{
    public class EbdtComponent
    {
        public byte GlyphId { get; }

        public sbyte XOffset { get; }

        public sbyte YOffset { get; }

        public EbdtComponent(BigEndianReader reader)
        {
            GlyphId = reader.ReadByte();
            XOffset = reader.ReadSByte();
            YOffset = reader.ReadSByte();
        }
    }
}