using FontParser.Reader;

namespace FontParser.Tables.Cff.Type1
{
    public class Type1Header : ICffHeader
    {
        public byte MajorVersion { get; }

        public byte MinorVersion { get; }

        public byte HeaderSize { get; }

        public byte OffSize { get; }

        public Type1Header(BigEndianReader reader)
        {
            MajorVersion = reader.ReadByte();
            MinorVersion = reader.ReadByte();
            HeaderSize = reader.ReadByte();
            OffSize = reader.ReadByte();
        }
    }
}