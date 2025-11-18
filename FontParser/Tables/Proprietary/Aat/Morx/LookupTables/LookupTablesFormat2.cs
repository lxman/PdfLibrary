using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx.LookupTables
{
    public class LookupTablesFormat2 : IFsHeader
    {
        public BinarySearchHeader Header { get; }

        public List<LookupSegment2> Segments { get; } = new List<LookupSegment2>();

        public LookupTablesFormat2(BigEndianReader reader)
        {
            Header = new BinarySearchHeader(reader);
            for (var i = 0; i < Header.NUnits; i++)
            {
                Segments.Add(new LookupSegment2(reader, Header.UnitSize));
            }
        }
    }
}