using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cff.Type1.FdSelect
{
    public class FdsFormat3
    {
        public List<Range3> Ranges { get; } = new List<Range3>();

        public ushort Sentinel { get; }

        public FdsFormat3(BigEndianReader reader)
        {
            ushort nRanges = reader.ReadUShort();
            for (var i = 0; i < nRanges; i++)
            {
                Ranges.Add(new Range3(reader));
            }
            Sentinel = reader.ReadUShort();
        }
    }
}
