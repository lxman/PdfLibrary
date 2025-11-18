using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx.LookupTables
{
    public class Format0 : IFsHeader
    {
        public List<byte[]> Values { get; } = new List<byte[]>();

        public Format0(BigEndianReader reader)
        {
            var header = new BinarySearchHeader(reader);
            for (var i = 0; i < header.NUnits; i++)
            {
                Values.Add(reader.ReadBytes(header.UnitSize));
            }
        }
    }
}