using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx.LookupTables
{
    public class LookupTablesFormat4 : IFsHeader
    {
        public BinarySearchHeader BinarySearchHeader { get; }

        public List<LookupSegment> Segments { get; } = new List<LookupSegment>();

        public LookupTablesFormat4(BigEndianReader reader)
        {
            BinarySearchHeader = new BinarySearchHeader(reader);
            for (var i = 0; i < BinarySearchHeader.NUnits; i++)
            {
                Segments.Add(new LookupSegment(reader));
            }
        }
    }
}