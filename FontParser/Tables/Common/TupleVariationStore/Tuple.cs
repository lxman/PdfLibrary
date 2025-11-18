using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Common.TupleVariationStore
{
    public class Tuple
    {
        public List<float> Coordinates { get; } = new List<float>();

        public Tuple(BigEndianReader reader, ushort axisCount)
        {
            for (var i = 0; i < axisCount; i++)
            {
                Coordinates.Add(reader.ReadF2Dot14());
            }
        }
    }
}