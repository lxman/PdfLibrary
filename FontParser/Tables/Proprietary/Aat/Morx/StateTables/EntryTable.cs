using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx.StateTables
{
    public class EntryTable
    {
        public ushort NewState { get; }

        public ushort Flags { get; }

        public List<uint> GlyphOffsets { get; } = new List<uint>();

        public EntryTable(BigEndianReader reader)
        {
            NewState = reader.ReadUShort();
            Flags = reader.ReadUShort();
        }
    }
}