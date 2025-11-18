using FontParser.Reader;

namespace FontParser.Tables.Math
{
    public class MathHeader
    {
        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public ushort MathConstantsOffset { get; }

        public ushort MathGlyphInfoOffset { get; }

        public ushort MathVariantsOffset { get; }

        public MathHeader(BigEndianReader reader)
        {
            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            MathConstantsOffset = reader.ReadUShort();
            MathGlyphInfoOffset = reader.ReadUShort();
            MathVariantsOffset = reader.ReadUShort();
        }
    }
}