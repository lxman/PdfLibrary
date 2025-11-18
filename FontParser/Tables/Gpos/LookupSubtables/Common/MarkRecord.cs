using FontParser.Reader;
using FontParser.Tables.Gpos.LookupSubtables.AnchorTable;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8601 // Possible null reference assignment.

namespace FontParser.Tables.Gpos.LookupSubtables.Common
{
    public class MarkRecord
    {
        public ushort MarkClass { get; }

        public IAnchorTable AnchorTable { get; }

        public MarkRecord(BigEndianReader reader, long startOfTable)
        {
            MarkClass = reader.ReadUShort();
            ushort markAnchorOffset = reader.ReadUShort();
            long before = reader.Position;
            reader.Seek(startOfTable + markAnchorOffset);
            byte format = reader.PeekBytes(2)[1];
            AnchorTable = format switch
            {
                1 => new AnchorTableFormat1(reader),
                2 => new AnchorTableFormat2(reader),
                3 => new AnchorTableFormat3(reader),
                _ => AnchorTable
            };
            reader.Seek(before);
        }
    }
}