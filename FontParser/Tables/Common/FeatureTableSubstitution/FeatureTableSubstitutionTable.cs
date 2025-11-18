using FontParser.Reader;

namespace FontParser.Tables.Common.FeatureTableSubstitution
{
    public class FeatureTableSubstitutionTable
    {
        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public ushort SubstitutionCount { get; }

        public FeatureTableSubstitutionRecord[] FeatureTableSubstitutionRecords { get; }

        public FeatureTableSubstitutionTable(BigEndianReader reader)
        {
            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            SubstitutionCount = reader.ReadUShort();
            FeatureTableSubstitutionRecords = new FeatureTableSubstitutionRecord[SubstitutionCount];
            for (var i = 0; i < SubstitutionCount; i++)
            {
                FeatureTableSubstitutionRecords[i] = new FeatureTableSubstitutionRecord(reader);
            }
        }
    }
}