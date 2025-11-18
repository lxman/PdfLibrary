using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Meta
{
    public class MetaTable : IFontTable
    {
        public static string Tag => "meta";

        public uint Flags { get; }

        public List<DataMap> DataMaps { get; } = new List<DataMap>();

        public MetaTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            _ = reader.ReadUInt32();
            Flags = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            uint dataMapsCount = reader.ReadUInt32();
            for (var i = 0; i < dataMapsCount; i++)
            {
                DataMaps.Add(new DataMap(reader));
            }
            for (var i = 0; i < dataMapsCount; i++)
            {
                DataMaps[i].Process(reader);
            }
        }
    }
}