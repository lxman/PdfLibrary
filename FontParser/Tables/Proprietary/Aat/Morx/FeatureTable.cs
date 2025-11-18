using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx
{
    public class FeatureTable
    {
        public ushort FeatureType { get; }

        public ushort FeatureSetting { get; }

        public uint EnableFlags { get; }

        public uint DisableFlags { get; }

        public FeatureTable(BigEndianReader reader)
        {
            FeatureType = reader.ReadUShort();
            FeatureSetting = reader.ReadUShort();
            EnableFlags = reader.ReadUInt32();
            DisableFlags = reader.ReadUInt32();
        }
    }
}