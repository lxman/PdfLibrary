using FontParser.Reader;

namespace FontParser.Tables.Optional.Dsig
{
    public class SigRecord
    {
        public SignatureBlock1 SignatureBlock { get; private set; }

        public uint Format { get; }

        private readonly uint _length;
        private readonly uint _offset;

        public SigRecord(BigEndianReader reader)
        {
            Format = reader.ReadUInt32();
            _length = reader.ReadUInt32();
            _offset = reader.ReadUInt32();
        }

        public void ReadSignature(BigEndianReader reader)
        {
            reader.Seek(_offset);
            SignatureBlock = new SignatureBlock1(reader);
        }
    }
}