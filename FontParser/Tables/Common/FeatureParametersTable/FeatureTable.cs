using FontParser.Reader;

namespace FontParser.Tables.Common.FeatureParametersTable
{
    public class FeatureTable
    {
        public ushort[] LookupListIndexes { get; }

        public IFeatureParametersTable? FeatureParametersTable { get; }

        public FeatureTable(BigEndianReader reader, string tag)
        {
            long startOfTable = reader.Position;
            ushort featureParamsOffset = reader.ReadUShort();
            ushort lookupIndexCount = reader.ReadUShort();
            LookupListIndexes = reader.ReadUShortArray(lookupIndexCount);
            if (featureParamsOffset <= 0) return;
            long before = reader.Position;
            reader.Seek(startOfTable + featureParamsOffset);
            if (tag.Length > 3 && char.IsDigit(tag[2]) && char.IsDigit(tag[3]))
            {
                FeatureParametersTable = tag[..2] switch
                {
                    "cv" => new CvFeatureParametersTable(reader),
                    "ss" => new SsFeatureParametersTable(reader),
                    _ => FeatureParametersTable
                };
            }

            reader.Seek(before);
        }
    }
}