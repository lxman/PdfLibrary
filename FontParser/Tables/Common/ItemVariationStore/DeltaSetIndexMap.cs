using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Common.ItemVariationStore
{
    public class DeltaSetIndexMap
    {
        public byte Format { get; }

        public List<MapData> Maps { get; } = new List<MapData>();

        public DeltaSetIndexMap(BigEndianReader reader)
        {
            Format = reader.ReadByte();
            byte entryFormat = reader.ReadByte();
            uint mapCount = Format == 0 ? reader.ReadUShort() : reader.ReadUInt32();
            //int entrySize = ((entryFormat & 0x30) >> 4) + 1;
            int outerFactor = (entryFormat & 0x0F) + 1;
            int innerFactor = (1 << ((entryFormat & 0x0F) + 1)) - 1;
            byte[] mapData = reader.ReadBytes(mapCount);
            for (var i = 0; i < mapCount; i++)
            {
                Maps.Add(new MapData(mapData[i], innerFactor, outerFactor));
            }
        }
    }
}