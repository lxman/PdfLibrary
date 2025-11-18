using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Merg
{
    public class MergeEntry
    {
        public List<MergeEntryRow> MergeEntryRows { get; } = new List<MergeEntryRow>();

        public MergeEntry(BigEndianReader reader, ushort mergeClassCount)
        {
            for (var i = 0; i < mergeClassCount; i++)
            {
                MergeEntryRows.Add(new MergeEntryRow(reader, mergeClassCount));
            }
        }
    }
}