using FontParser.Reader;
using FontParser.Tables.Gpos.LookupSubtables.AnchorTable;

namespace FontParser.Tables.Gpos.LookupSubtables.CursivePos
{
    public class EntryExitRecord
    {
        public IAnchorTable? EntryAnchorTable { get; }

        public IAnchorTable? ExitAnchorTable { get; }

        public EntryExitRecord(BigEndianReader reader, long startOfTable)
        {
            ushort entryAnchorOffset = reader.ReadUShort();
            ushort exitAnchorOffset = reader.ReadUShort();
            long before = reader.Position;
            if (entryAnchorOffset != 0)
            {
                reader.Seek(startOfTable + entryAnchorOffset);
                ushort entryFormat = reader.PeekBytes(2)[1];
                EntryAnchorTable = entryFormat switch
                {
                    1 => new AnchorTableFormat1(reader),
                    2 => new AnchorTableFormat2(reader),
                    3 => new AnchorTableFormat3(reader),
                    _ => EntryAnchorTable
                };
            }

            if (exitAnchorOffset == 0)
            {
                reader.Seek(before);
                return;
            }
            reader.Seek(startOfTable + exitAnchorOffset);
            ushort exitFormat = reader.PeekBytes(2)[1];
            ExitAnchorTable = exitFormat switch
            {
                1 => new AnchorTableFormat1(reader),
                2 => new AnchorTableFormat2(reader),
                3 => new AnchorTableFormat3(reader),
                _ => ExitAnchorTable
            };
            reader.Seek(before);
        }
    }
}