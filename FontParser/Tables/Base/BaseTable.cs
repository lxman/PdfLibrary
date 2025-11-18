using FontParser.Reader;
using FontParser.Tables.Common.ItemVariationStore;

namespace FontParser.Tables.Base
{
    public class BaseTable : IFontTable
    {
        public static string Tag => "BASE";

        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public AxisTable? HorizontalAxisTable { get; }

        public AxisTable? VerticalAxisTable { get; }

        public ItemVariationStore? ItemVariationStore { get; }

        public BaseTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            ushort horizontalTableOffset = reader.ReadUShort();
            ushort verticalTableOffset = reader.ReadUShort();
            ushort? itemVariationStoreOffset = null;
            if (MinorVersion > 0)
            {
                itemVariationStoreOffset = reader.ReadUShort();
            }
            if (horizontalTableOffset > 0)
            {
                reader.Seek(horizontalTableOffset);
                HorizontalAxisTable = new AxisTable(reader);
            }
            if (verticalTableOffset > 0)
            {
                reader.Seek(verticalTableOffset);
                VerticalAxisTable = new AxisTable(reader);
            }

            if (!(itemVariationStoreOffset > 0)) return;
            reader.Seek(itemVariationStoreOffset.Value);
            ItemVariationStore = new ItemVariationStore(reader);
        }
    }
}