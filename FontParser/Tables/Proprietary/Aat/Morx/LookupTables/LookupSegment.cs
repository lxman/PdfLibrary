using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx.LookupTables
{
    public class LookupSegment
    {
        public ushort LastGlyph { get; }

        public ushort FirstGlyph { get; }

        public ushort ValueOffset { get; }

        public LookupSegment(BigEndianReader reader)
        {
            FirstGlyph = reader.ReadUShort();
            LastGlyph = reader.ReadUShort();
            ValueOffset = reader.ReadUShort();
        }
    }
}