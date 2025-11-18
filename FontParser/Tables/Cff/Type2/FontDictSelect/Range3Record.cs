using FontParser.Reader;

namespace FontParser.Tables.Cff.Type2.FontDictSelect
{
    public class Range3Record
    {
        public ushort FirstGlyph { get; }

        public byte FdIndex { get; }

        public Range3Record(BigEndianReader reader)
        {
            FirstGlyph = reader.ReadUShort();
            FdIndex = reader.ReadByte();
        }
    }
}