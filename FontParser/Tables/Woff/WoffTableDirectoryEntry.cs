using FontParser.Reader;

namespace FontParser.Tables.Woff
{
    public class WoffTableDirectoryEntry : IDirectoryEntry
    {
        public string Tag { get; }

        public uint Offset { get; }

        public uint CompressedLength { get; }

        public uint OriginalLength { get; }

        public uint Checksum { get; }

        public WoffTableDirectoryEntry(FileByteReader reader)
        {
            Tag = reader.ReadString(4);
            Offset = reader.ReadUInt32();
            CompressedLength = reader.ReadUInt32();
            OriginalLength = reader.ReadUInt32();
            Checksum = reader.ReadUInt32();
        }

#if DEBUG

        public new string ToString() => $"{Tag} Offset={Offset} Compressed Length={CompressedLength} OriginalLength={OriginalLength}";

#endif
    }
}