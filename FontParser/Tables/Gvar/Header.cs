using FontParser.Reader;

namespace FontParser.Tables.Gvar
{
    public class Header
    {
        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public ushort AxisCount { get; }

        public ushort SharedTupleCount { get; }

        public uint SharedTuplesOffset { get; }

        public ushort GlyphCount { get; }

        public ushort Flags { get; }

        public uint GlyphVariationDataArrayOffset { get; }

        public Header(BigEndianReader reader)
        {
            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            AxisCount = reader.ReadUShort();
            SharedTupleCount = reader.ReadUShort();
            SharedTuplesOffset = reader.ReadUInt32();
            GlyphCount = reader.ReadUShort();
            Flags = reader.ReadUShort();
            GlyphVariationDataArrayOffset = reader.ReadUInt32();
        }
    }
}