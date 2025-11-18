using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Feat
{
    public class Header
    {
        public uint Version { get; }

        public ushort FeatureCount { get; }

        public Header(BigEndianReader reader)
        {
            Version = reader.ReadUInt32();
            FeatureCount = reader.ReadUShort();
            _ = reader.ReadUShort();
            _ = reader.ReadUInt32();
        }
    }
}