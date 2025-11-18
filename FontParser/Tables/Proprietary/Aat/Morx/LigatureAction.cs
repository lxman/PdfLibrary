using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx
{
    public class LigatureAction
    {
        public ushort NextStateIndex { get; }

        public EntryFlags Flags { get; }

        public ushort LigActionIndex { get; }

        public LigatureAction(BigEndianReader reader)
        {
            NextStateIndex = reader.ReadUShort();
            Flags = (EntryFlags)reader.ReadUShort();
            LigActionIndex = reader.ReadUShort();
        }
    }
}