using FontParser.Reader;

namespace FontParser.Tables.Kern
{
    public class KernSubtableFormat2 : IKernSubtable
    {
        public ushort Version { get; }

        public KernCoverage Coverage { get; }

        public KernSubtableFormat2(BigEndianReader reader)
        {
            Version = reader.ReadUShort();
            _ = reader.ReadUShort();
            Coverage = (KernCoverage)reader.ReadUShort();
            ushort rowWidth = reader.ReadUShort();
            ushort leftClassOffset = reader.ReadUShort();
            ushort rightClassOffset = reader.ReadUShort();
            ushort kerningArrayOffset = reader.ReadUShort();
        }
    }
}