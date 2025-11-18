using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cff.Type1
{
    public class Encoding1 : IEncoding
    {
        public byte Format => 1;

        public List<Range1> Ranges { get; } = new List<Range1>();

        public Encoding1(BigEndianReader reader)
        {
            byte nRanges = reader.ReadByte();
            for (var i = 0; i < nRanges; i++)
            {
                Ranges.Add(new Range1(reader));
            }
        }
    }
}