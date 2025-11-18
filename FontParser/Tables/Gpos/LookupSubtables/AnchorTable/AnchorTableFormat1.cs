using FontParser.Reader;

namespace FontParser.Tables.Gpos.LookupSubtables.AnchorTable
{
    public class AnchorTableFormat1 : IAnchorTable
    {
        public short X { get; }

        public short Y { get; }

        public AnchorTableFormat1(BigEndianReader reader)
        {
            _ = reader.ReadUShort();
            X = reader.ReadShort();
            Y = reader.ReadShort();
        }
    }
}