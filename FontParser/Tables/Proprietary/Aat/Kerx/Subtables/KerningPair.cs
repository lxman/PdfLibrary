using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Kerx.Subtables
{
    public class KerningPair
    {
        public ushort Left { get; }

        public ushort Right { get; }

        public short Value { get; }

        public KerningPair(BigEndianReader reader)
        {
            Left = reader.ReadUShort();
            Right = reader.ReadUShort();
            Value = reader.ReadShort();
        }
    }
}