using FontParser.Reader;

namespace FontParser.Tables.Common.FeatureTableSubstitution
{
    public class FeatureTableSubstitutionRecord
    {
        public ushort FeatureIndex { get; }

        public uint AlternateFeatureOffset { get; }

        public FeatureTableSubstitutionRecord(BigEndianReader reader)
        {
            FeatureIndex = reader.ReadUShort();
            AlternateFeatureOffset = reader.ReadUInt32();
        }
    }
}