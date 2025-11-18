using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx.StateTables
{
    public class StxHeader
    {
        public uint ClassCount { get; }

        public uint ClassTableOffset { get; }

        public uint StateArrayOffset { get; }

        public uint EntryTableOffset { get; }

        public StxHeader(BigEndianReader reader)
        {
            ClassCount = reader.ReadUInt32();
            ClassTableOffset = reader.ReadUInt32();
            StateArrayOffset = reader.ReadUInt32();
            EntryTableOffset = reader.ReadUInt32();
        }
    }
}