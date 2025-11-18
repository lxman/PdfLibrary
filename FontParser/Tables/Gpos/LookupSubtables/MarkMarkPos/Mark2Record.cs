using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;
using FontParser.Tables.Gpos.LookupSubtables.AnchorTable;

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

namespace FontParser.Tables.Gpos.LookupSubtables.MarkMarkPos
{
    public class Mark2Record
    {
        public List<IAnchorTable> AnchorTables { get; } = new List<IAnchorTable>();

        public Mark2Record(BigEndianReader reader, ushort markClassCount, long startOfTable)
        {
            List<ushort> mark2AnchorOffsets = reader.ReadUShortArray(markClassCount).ToList();
            long before = reader.Position;
            mark2AnchorOffsets.ForEach(o =>
            {
                reader.Seek(startOfTable + o);
                byte format = reader.PeekBytes(2)[1];
                IAnchorTable anchorTable = format switch
                {
                    1 => new AnchorTableFormat1(reader),
                    2 => new AnchorTableFormat2(reader),
                    3 => new AnchorTableFormat3(reader),
                    _ => null
                };
                if (!(anchorTable is null)) AnchorTables.Add(anchorTable);
            });
            reader.Seek(before);
        }
    }
}