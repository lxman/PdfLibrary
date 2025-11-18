using FontParser.Reader;

namespace FontParser.Tables.Colr
{
    public class LayerRecord
    {
        public ushort GlyphId { get; }

        public ushort PaletteIndex { get; }

        public LayerRecord(BigEndianReader reader)
        {
            GlyphId = reader.ReadUShort();
            PaletteIndex = reader.ReadUShort();
        }
    }
}