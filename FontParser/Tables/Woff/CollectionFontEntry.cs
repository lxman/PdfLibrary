using FontParser.Reader;

namespace FontParser.Tables.Woff
{
    public class CollectionFontEntry
    {
        public ushort NumTables { get; }

        public uint Flavor { get; }

        public ushort[] TableIndexes { get; }

        public CollectionFontEntry(FileByteReader reader)
        {
            NumTables = reader.Read255UInt16();
            Flavor = reader.ReadUInt32();
            TableIndexes = new ushort[NumTables];
            for (var i = 0; i < NumTables; i++)
            {
                TableIndexes[i] = reader.Read255UInt16();
            }
        }
    }
}