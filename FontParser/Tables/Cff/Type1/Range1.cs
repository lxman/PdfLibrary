using FontParser.Reader;

namespace FontParser.Tables.Cff.Type1
{
    public class Range1
    {
        public byte First { get; }

        public byte NumberLeft { get; }

        public Range1(BigEndianReader reader)
        {
            First = reader.ReadByte();
            NumberLeft = reader.ReadByte();
        }
    }
}