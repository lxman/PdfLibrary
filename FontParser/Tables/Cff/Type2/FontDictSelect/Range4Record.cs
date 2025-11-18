using FontParser.Reader;

namespace FontParser.Tables.Cff.Type2.FontDictSelect
{
    public class Range4Record
    {
        public uint FirstGlyph { get; }

        public ushort FdIndex { get; }

        public Range4Record(BigEndianReader reader)
        {
            FirstGlyph = reader.ReadUInt32();
            FdIndex = reader.ReadUShort();
        }
    }
}