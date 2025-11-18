using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Avar
{
    public class AvarTable : IFontTable
    {
        public static string Tag => "avar";

        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public List<SegmentMapsRecord> SegmentMaps { get; } = new List<SegmentMapsRecord>();

        public AvarTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            _ = reader.ReadUShort();
            ushort axisCount = reader.ReadUShort();
            for (var i = 0; i < axisCount; i++)
            {
                SegmentMaps.Add(new SegmentMapsRecord(reader));
            }
        }
    }
}