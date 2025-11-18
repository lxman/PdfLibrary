using FontParser.Reader;

namespace FontParser.Tables.Kern
{
    public class ClassTable
    {
        public ushort FirstGlyph { get; }

        public ushort[] ClassValues { get; }

        public ClassTable(BigEndianReader reader)
        {
            FirstGlyph = reader.ReadUShort();
            ushort nGlyphs = reader.ReadUShort();
            ClassValues = reader.ReadUShortArray(nGlyphs);
        }
    }
}