using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Feat
{
    public class FeatureName
    {
        public ushort Feature { get; }

        public short NameIndex { get; }

        public ushort SettingsCount { get; }

        public uint SettingsTableOffset { get; }

        public ushort FeatureFlags { get; }

        public FeatureName(BigEndianReader reader)
        {
            Feature = reader.ReadUShort();
            SettingsCount = reader.ReadUShort();
            SettingsTableOffset = reader.ReadUInt32();
            FeatureFlags = reader.ReadUShort();
            NameIndex = reader.ReadShort();
        }
    }
}