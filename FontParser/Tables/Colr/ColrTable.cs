using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common.ItemVariationStore;

namespace FontParser.Tables.Colr
{
    public class ColrTable : IFontTable
    {
        public static string Tag => "COLR";

        public ushort Version { get; }

        public List<BaseGlyphRecord> BaseGlyphRecords { get; } = new List<BaseGlyphRecord>();

        public List<LayerRecord> LayerRecords { get; } = new List<LayerRecord>();

        public BaseGlyphList? BaseGlyphList { get; }

        public LayerList? LayerList { get; }

        public ClipList? ClipList { get; }

        public DeltaSetIndexMap? DeltaSetIndexMap { get; }

        public ItemVariationStore? ItemVariationStore { get; }

        public ColrTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            Version = reader.ReadUShort();
            ushort baseGlyphRecordCount = reader.ReadUShort();
            uint baseGlyphRecordOffset = reader.ReadUInt32();
            uint layerRecordOffset = reader.ReadUInt32();
            ushort layerRecordCount = reader.ReadUShort();
            uint? baseGlyphListOffset = null;
            uint? layerListOffset = null;
            uint? clipListOffset = null;
            uint? deltaSetIndexMapOffset = null;
            uint? itemVariationStoreOffset = null;
            if (Version == 1)
            {
                baseGlyphListOffset = reader.ReadUInt32();
                layerListOffset = reader.ReadUInt32();
                clipListOffset = reader.ReadUInt32();
                deltaSetIndexMapOffset = reader.ReadUInt32();
                itemVariationStoreOffset = reader.ReadUInt32();
            }
            reader.Seek(baseGlyphRecordOffset);
            for (var i = 0; i < baseGlyphRecordCount; i++)
            {
                BaseGlyphRecords.Add(new BaseGlyphRecord(reader));
            }
            reader.Seek(layerRecordOffset);
            for (var i = 0; i < layerRecordCount; i++)
            {
                LayerRecords.Add(new LayerRecord(reader));
            }
            if (Version == 0) return;
            if (baseGlyphListOffset > 0)
            {
                reader.Seek(baseGlyphListOffset.Value);
                BaseGlyphList = new BaseGlyphList(reader);
            }
            if (layerListOffset > 0)
            {
                reader.Seek(layerListOffset.Value);
                LayerList = new LayerList(reader);
                LayerList.Process(reader);
            }
            if (clipListOffset > 0)
            {
                reader.Seek(clipListOffset.Value);
                ClipList = new ClipList(reader);
            }
            switch (deltaSetIndexMapOffset)
            {
                case null:
                case 0:
                    return;

                default:
                    reader.Seek(deltaSetIndexMapOffset.Value);
                    DeltaSetIndexMap = new DeltaSetIndexMap(reader);
                    break;
            }

            if (!itemVariationStoreOffset.HasValue || itemVariationStoreOffset == 0) return;
            reader.Seek(itemVariationStoreOffset.Value);
            ItemVariationStore = new ItemVariationStore(reader);
        }
    }
}