using FontParser.Reader;

namespace FontParser.Tables.Common.ClassDefinition
{
    public class ClassRange
    {
        public ushort StartGlyphID { get; }

        public ushort EndGlyphID { get; }

        public ushort Class { get; }

        public ClassRange(BigEndianReader reader)
        {
            StartGlyphID = reader.ReadUShort();
            EndGlyphID = reader.ReadUShort();
            Class = reader.ReadUShort();
        }
    }
}