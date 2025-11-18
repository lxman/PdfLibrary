using FontParser.Reader;

namespace FontParser.Tables.Common
{
    public class FeatureVariationRecord
    {
        public ushort ConditionSetOffset { get; }

        public ushort FeatureTableSubstitutionOffset { get; }

        public FeatureVariationRecord(BigEndianReader reader)
        {
            ConditionSetOffset = reader.ReadUShort();
            FeatureTableSubstitutionOffset = reader.ReadUShort();
        }
    }
}