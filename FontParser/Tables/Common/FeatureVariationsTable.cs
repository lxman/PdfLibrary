using FontParser.Reader;

namespace FontParser.Tables.Common
{
    public class FeatureVariationsTable
    {
        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public uint FeatureVariationRecordCount { get; }

        public FeatureVariationRecord[] FeatureVariationRecords { get; }

        public FeatureVariationsTable(BigEndianReader reader)
        {
            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            FeatureVariationRecordCount = reader.ReadUInt32();
            FeatureVariationRecords = new FeatureVariationRecord[FeatureVariationRecordCount];
            for (var i = 0; i < FeatureVariationRecordCount; i++)
            {
                FeatureVariationRecords[i] = new FeatureVariationRecord(reader);
            }
        }
    }
}