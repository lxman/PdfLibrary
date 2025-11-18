using FontParser.Reader;

namespace FontParser.Tables.Mvar
{
    public class ValueRecord
    {
        public byte[] ValueTag { get; }

        public ushort DeltaSetOuterIndex { get; }

        public ushort DeltaSetInnerIndex { get; }

        public ValueRecord(BigEndianReader reader)
        {
            ValueTag = reader.ReadBytes(4);
            DeltaSetOuterIndex = reader.ReadUShort();
            DeltaSetInnerIndex = reader.ReadUShort();
        }
    }
}