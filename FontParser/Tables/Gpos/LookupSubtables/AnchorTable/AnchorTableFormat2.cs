using FontParser.Reader;

namespace FontParser.Tables.Gpos.LookupSubtables.AnchorTable
{
    public class AnchorTableFormat2 : IAnchorTable
    {
        public short X { get; }

        public short Y { get; }

        public ushort AnchorPoint { get; }

        public AnchorTableFormat2(BigEndianReader reader)
        {
            _ = reader.ReadUShort();
            X = reader.ReadShort();
            Y = reader.ReadShort();
            AnchorPoint = reader.ReadUShort();
        }
    }
}