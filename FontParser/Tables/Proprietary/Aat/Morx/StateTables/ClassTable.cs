using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx.StateTables
{
    public class ClassTable
    {
        public ushort FirstGlyph { get; }

        public ushort GlyphCount { get; }

        public byte[] ClassArray { get; }

        public ClassTable(BigEndianReader reader)
        {
            FirstGlyph = reader.ReadUShort();
            GlyphCount = reader.ReadUShort();
            ClassArray = reader.ReadBytes(GlyphCount);
        }
    }
}