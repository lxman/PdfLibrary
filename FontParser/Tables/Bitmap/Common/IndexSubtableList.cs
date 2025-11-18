using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Common
{
    public class IndexSubtableList
    {
        public List<IndexSubtableRecord> IndexSubtables { get; } = new List<IndexSubtableRecord>();

        public IndexSubtableList(BigEndianReader reader, uint numTables)
        {
            long start = reader.Position;
            for (var i = 0; i < numTables; i++)
            {
                IndexSubtables.Add(new IndexSubtableRecord(reader, start));
            }
            IndexSubtables.ForEach(s => s.ReadSubtable(reader));
        }
    }
}