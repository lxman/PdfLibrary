using FontParser.Reader;

namespace FontParser.Tables.Kern
{
    public class KernPair
    {
        public ushort Left { get; }

        public ushort Right { get; }

        public float Value { get; }

        public KernPair(BigEndianReader reader)
        {
            Left = reader.ReadUShort();
            Right = reader.ReadUShort();
            Value = reader.ReadF2Dot14();
        }
    }
}