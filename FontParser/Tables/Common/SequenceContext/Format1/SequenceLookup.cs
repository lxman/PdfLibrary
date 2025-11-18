using FontParser.Reader;

namespace FontParser.Tables.Common.SequenceContext.Format1
{
    public class SequenceLookup
    {
        public ushort SequenceIndex { get; }

        public ushort LookupListIndex { get; }

        public SequenceLookup(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            SequenceIndex = reader.ReadUShort();
            LookupListIndex = reader.ReadUShort();
        }
    }
}