using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx.StateTables
{
    public class MorxStateTable
    {
        public ClassTable ClassTable { get; }

        public EntryTable EntryTable { get; }

        public MorxStateTable(BigEndianReader reader)
        {
            var header = new StxHeader(reader);
            reader.Seek(header.ClassTableOffset);
            ClassTable = new ClassTable(reader);
            reader.Seek(header.EntryTableOffset);
            EntryTable = new EntryTable(reader);
        }
    }
}