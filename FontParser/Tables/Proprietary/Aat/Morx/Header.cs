using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx
{
    public class Header
    {
        public ushort Version { get; }

        public uint NChains { get; }

        public Header(BigEndianReader reader)
        {
            Version = reader.ReadUShort();
            _ = reader.ReadUShort();
            NChains = reader.ReadUInt32();
        }
    }
}