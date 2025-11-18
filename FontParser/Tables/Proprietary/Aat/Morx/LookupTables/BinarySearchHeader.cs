using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx.LookupTables
{
    public class BinarySearchHeader
    {
        public ushort UnitSize { get; }

        public ushort NUnits { get; }

        public ushort SearchRange { get; }

        public ushort EntrySelector { get; }

        public ushort RangeShift { get; }

        public BinarySearchHeader(BigEndianReader reader)
        {
            UnitSize = reader.ReadUShort();
            NUnits = reader.ReadUShort();
            SearchRange = reader.ReadUShort();
            EntrySelector = reader.ReadUShort();
            RangeShift = reader.ReadUShort();
        }
    }
}