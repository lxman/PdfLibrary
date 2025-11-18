using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintColrGlyph : IPaintTable
    {
        public byte Format => 11;

        public ushort GlyphId { get; }

        public PaintColrGlyph(BigEndianReader reader)
        {
            GlyphId = reader.ReadUShort();
        }
    }
}