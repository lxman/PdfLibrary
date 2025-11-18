using FontParser.Reader;

namespace FontParser.Tables.Cff.Type1.Charsets
{
    public class Range1
    {
        public ushort First { get; }

        public byte NumberLeft { get; }

        public Range1(BigEndianReader reader)
        {
            First = reader.ReadUShort();
            NumberLeft = reader.ReadByte();
        }
    }
}