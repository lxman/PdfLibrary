using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Avar
{
    public class SegmentMapsRecord
    {
        public List<AxisValueMap> AxisValueMaps { get; } = new List<AxisValueMap>();

        public SegmentMapsRecord(BigEndianReader reader)
        {
            ushort positionMapCount = reader.ReadUShort();
            for (var i = 0; i < positionMapCount; i++)
            {
                AxisValueMaps.Add(new AxisValueMap(reader));
            }
        }
    }
}