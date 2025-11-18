using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Jstf
{
    public class JstfModList
    {
        public List<ushort> GsubLookupIndices { get; } = new List<ushort>();

        public JstfModList(BigEndianReader reader)
        {
            ushort lookupCount = reader.ReadUShort();
            GsubLookupIndices.AddRange(reader.ReadUShortArray(lookupCount));
        }
    }
}