using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cff.Type2.FontDictSelect
{
    public class FdsFormat3 : IFdSelect
    {
        public byte Format { get; }

        public List<Range3Record> Ranges { get; } = new List<Range3Record>();

        public ushort Sentinel { get; }

        public FdsFormat3(BigEndianReader reader)
        {
            Format = reader.ReadByte();
            ushort numRanges = reader.ReadUShort();
            for (var i = 0; i < numRanges; i++)
            {
                Ranges.Add(new Range3Record(reader));
            }
            Sentinel = reader.ReadUShort();
        }
    }
}