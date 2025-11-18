using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cff.Type2.FontDictSelect
{
    public class FdsFormat4 : IFdSelect
    {
        public byte Format { get; }

        public List<Range4Record> Ranges { get; } = new List<Range4Record>();

        public uint Sentinel { get; }

        public FdsFormat4(BigEndianReader reader)
        {
            Format = reader.ReadByte();
            uint numRanges = reader.ReadUInt32();
            for (var i = 0; i < numRanges; i++)
            {
                Ranges.Add(new Range4Record(reader));
            }
            Sentinel = reader.ReadUInt32();
        }
    }
}