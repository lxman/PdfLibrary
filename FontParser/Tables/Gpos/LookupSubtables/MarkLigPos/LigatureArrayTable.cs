using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Gpos.LookupSubtables.MarkLigPos
{
    public class LigatureArrayTable
    {
        public List<LigatureAttachTable> LigatureAttachTables { get; } = new List<LigatureAttachTable>();

        public LigatureArrayTable(BigEndianReader reader, ushort markClassCount)
        {
            long position = reader.Position;

            ushort ligatureCount = reader.ReadUShort();
            ushort[] ligatureAttachOffsets = reader.ReadUShortArray(ligatureCount);
            foreach (ushort lao in ligatureAttachOffsets)
            {
                reader.Seek(position + lao);
                LigatureAttachTables.Add(new LigatureAttachTable(reader, markClassCount));
            }
        }
    }
}