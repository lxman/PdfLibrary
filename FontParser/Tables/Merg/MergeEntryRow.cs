using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Merg
{
    public class MergeEntryRow
    {
        public List<MergeEntryFlags> MergeEntryFlags { get; } = new List<MergeEntryFlags>();

        public MergeEntryRow(BigEndianReader reader, ushort mergeClassCount)
        {
            for (var i = 0; i < mergeClassCount; i++)
            {
                MergeEntryFlags.Add((MergeEntryFlags)reader.ReadByte());
            }
        }
    }
}