using FontParser.Reader;

namespace FontParser.Tables.Optional.Dsig
{
    public class SignatureBlock1
    {
        public byte[] Signature { get; set; }

        public SignatureBlock1(BigEndianReader reader)
        {
            _ = reader.ReadUShort();
            _ = reader.ReadUShort();
            uint signatureLength = reader.ReadUInt32();
            Signature = reader.ReadBytes(signatureLength);
        }
    }
}