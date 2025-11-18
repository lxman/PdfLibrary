using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Fvar
{
    public class UserTuple
    {
        public List<float> Coordinates { get; } = new List<float>();

        public UserTuple(BigEndianReader reader, ushort axisCount)
        {
            for (var i = 0; i < axisCount; i++)
            {
                Coordinates.Add(reader.ReadF16Dot16());
            }
        }
    }
}