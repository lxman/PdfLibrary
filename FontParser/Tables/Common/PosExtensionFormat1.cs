using FontParser.Reader;

namespace FontParser.Tables.Common
{
    public class PosExtensionFormat1 : ILookupSubTable
    {
        public ushort Format { get; }

        public ushort ExtensionLookupType { get; }

        public uint ExtensionOffset { get; }

        public PosExtensionFormat1(BigEndianReader reader)
        {
            Format = reader.ReadUShort();
            ExtensionLookupType = reader.ReadUShort();
            ExtensionOffset = reader.ReadUInt32();
        }
    }
}