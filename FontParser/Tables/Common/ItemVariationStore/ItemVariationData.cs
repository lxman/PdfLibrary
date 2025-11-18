using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.Common.ItemVariationStore
{
    public class ItemVariationData
    {
        public List<ushort> RegionIndexes { get; }

        public List<DeltaSetRecord> DeltaSets { get; } = new List<DeltaSetRecord>();

        public ItemVariationData(BigEndianReader reader)
        {
            ushort itemCount = reader.ReadUShort();
            ushort wordDeltaCount = reader.ReadUShort();
            bool longWords = (wordDeltaCount & 0x8000) == 1;
            int deltaCount = wordDeltaCount & 0x7FFF;
            ushort regionIndexCount = reader.ReadUShort();
            RegionIndexes = reader.ReadUShortArray(regionIndexCount).ToList();
            for (var i = 0; i < itemCount; i++)
            {
                DeltaSets.Add(new DeltaSetRecord(
                    reader,
                    regionIndexCount,
                    longWords,
                    deltaCount));
            }
        }
    }
}