using FontParser.Reader;

namespace FontParser.Tables.Cff.Type2
{
    public class Type2Header : ICffHeader
    {
        public byte MajorVersion { get; }

        public byte MinorVersion { get; }

        public byte HeaderSize { get; }

        public ushort TopDictSize { get; }

        public Type2Header(BigEndianReader reader)
        {
            MajorVersion = reader.ReadByte();
            MinorVersion = reader.ReadByte();
            HeaderSize = reader.ReadByte();
            TopDictSize = reader.ReadUShort();
        }
    }
}