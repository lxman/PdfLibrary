using FontParser.Reader;

namespace FontParser.Tables.Cff.Type1.FdSelect
{
    public class Range3
    {
        public ushort First { get; set; }

        public byte FontDictIndex { get; set; }

        public Range3(BigEndianReader reader)
        {
            First = reader.ReadUShort();
            FontDictIndex = reader.ReadByte();
        }
    }
}
