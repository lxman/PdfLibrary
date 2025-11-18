using FontParser.Reader;
using FontParser.Tables.Common.ItemVariationStore;

namespace FontParser.Tables.Hvar
{
    public class HvarTable : IFontTable
    {
        public static string Tag => "HVAR";

        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public DeltaSetIndexMap AdvancedWidthMapping { get; }

        public DeltaSetIndexMap LsbMapping { get; }

        public DeltaSetIndexMap RsbMapping { get; }

        public ItemVariationStore ItemVariationStore { get; }

        public HvarTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            uint itemVariationStoreOffset = reader.ReadUInt32();
            uint advancedWidthMappingOffset = reader.ReadUInt32();
            uint lsbMappingOffset = reader.ReadUInt32();
            uint rsbMappingOffset = reader.ReadUInt32();
            reader.Seek(itemVariationStoreOffset);
            ItemVariationStore = new ItemVariationStore(reader);
            reader.Seek(advancedWidthMappingOffset);
            AdvancedWidthMapping = new DeltaSetIndexMap(reader);
            reader.Seek(lsbMappingOffset);
            LsbMapping = new DeltaSetIndexMap(reader);
            reader.Seek(rsbMappingOffset);
            RsbMapping = new DeltaSetIndexMap(reader);
        }
    }
}