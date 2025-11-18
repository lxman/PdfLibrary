using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Common.ItemVariationStore
{
    public class ItemVariationStore
    {
        public List<ItemVariationData> ItemVariationData { get; } = new List<ItemVariationData>();

        public ItemVariationStore(BigEndianReader reader)
        {
            long position = reader.Position;

            ushort format = reader.ReadUShort();
            uint variationRegionListOffset = reader.ReadUInt32();
            ushort itemVariationDataCount = reader.ReadUShort();
            uint[] itemVariationDataOffsets = reader.ReadUInt32Array(itemVariationDataCount);
            for (var i = 0; i < itemVariationDataCount; i++)
            {
                reader.Seek(position + itemVariationDataOffsets[i]);
                ItemVariationData.Add(new ItemVariationData(reader));
            }
        }
    }
}