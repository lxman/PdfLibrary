using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Common.FeatureParametersTable
{
    public class FeatureList
    {
        public List<FeatureRecord> FeatureRecords { get; }

        public FeatureList(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            ushort featureCount = reader.ReadUShort();
            FeatureRecords = new List<FeatureRecord>(featureCount);

            for (var i = 0; i < featureCount; i++)
            {
                FeatureRecords.Add(new FeatureRecord(reader, startOfTable));
            }
        }
    }
}