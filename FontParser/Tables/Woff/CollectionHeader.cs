using FontParser.Reader;

namespace FontParser.Tables.Woff
{
    public class CollectionHeader
    {
        public uint Version { get; }

        public ushort NumFonts { get; }

        public CollectionHeader(FileByteReader reader)
        {
            Version = reader.ReadUInt32();
            NumFonts = reader.Read255UInt16();
        }
    }
}