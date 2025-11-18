using FontParser.Reader;

namespace FontParser.Tables.Cff.Type1.Charsets
{
    public class Range2
    {
        public ushort First { get; }

        public ushort NumberLeft { get; }

        public Range2(BigEndianReader reader)
        {
            First = reader.ReadUShort();
            NumberLeft = reader.ReadUShort();
        }
    }
}