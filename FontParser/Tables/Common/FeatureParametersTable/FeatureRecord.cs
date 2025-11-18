using System.Text;
using FontParser.Reader;

namespace FontParser.Tables.Common.FeatureParametersTable
{
    public class FeatureRecord
    {
        public string FeatureTag { get; }

        public FeatureTable FeatureTable { get; }

        public FeatureRecord(BigEndianReader reader, long startOfTable)
        {
            FeatureTag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            ushort offset = reader.ReadUShort();
            long before = reader.Position;
            reader.Seek(startOfTable + offset);
            FeatureTable = new FeatureTable(reader, FeatureTag);
            reader.Seek(before);
        }
    }
}