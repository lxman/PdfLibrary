using FontParser.Reader;

namespace FontParser.Tables.Common
{
    public class VariationIndexTable
    {
        public ushort DeltaSetOuterIndex { get; }

        public ushort DeltaSetInnerIndex { get; }

        public DeltaFormat DeltaFormat { get; }

        public VariationIndexTable(BigEndianReader reader)
        {
            DeltaSetOuterIndex = reader.ReadUShort();
            DeltaSetInnerIndex = reader.ReadUShort();
            DeltaFormat = (DeltaFormat)reader.ReadUShort();
        }
    }
}