using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx.LookupTables
{
    public class LookupTablesFormat6 : IFsHeader
    {
        public BinarySearchHeader BinarySearchHeader { get; }

        public List<LookupSingle> Entries { get; } = new List<LookupSingle>();

        public LookupTablesFormat6(BigEndianReader reader)
        {
            BinarySearchHeader = new BinarySearchHeader(reader);
            for (var i = 0; i < BinarySearchHeader.NUnits; i++)
            {
                Entries.Add(new LookupSingle(reader, BinarySearchHeader.UnitSize));
            }
        }
    }
}