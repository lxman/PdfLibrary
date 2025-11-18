using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Jstf
{
    public class JstfMax
    {
        public List<ushort> LookupOffsets { get; } = new List<ushort>();

        public JstfMax(BigEndianReader reader)
        {
            ushort lookupCount = reader.ReadUShort();
            LookupOffsets.AddRange(reader.ReadUShortArray(lookupCount));
        }
    }
}