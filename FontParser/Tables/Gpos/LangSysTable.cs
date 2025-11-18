using FontParser.Reader;

namespace FontParser.Tables.Gpos
{
    public class LangSysTable
    {
        public ushort LookupOrder { get; }

        public ushort RequiredFeatureIndex { get; }

        public ushort[] FeatureIndices { get; }

        public LangSysTable(BigEndianReader reader)
        {
            LookupOrder = reader.ReadUShort();
            RequiredFeatureIndex = reader.ReadUShort();
            ushort featureIndexCount = reader.ReadUShort();
            FeatureIndices = reader.ReadUShortArray(featureIndexCount);
        }
    }
}