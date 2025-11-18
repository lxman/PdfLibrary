using FontParser.Reader;

namespace FontParser.Tables.Common.CoverageFormat
{
    public class CoverageFormat1 : ICoverageFormat
    {
        public ushort Format => 1;

        public ushort[] GlyphArray { get; }

        public CoverageFormat1(BigEndianReader reader)
        {
            _ = reader.ReadUShort(); // Skip format
            ushort glyphCount = reader.ReadUShort();
            GlyphArray = new ushort[glyphCount];
            for (var i = 0; i < glyphCount; i++)
            {
                GlyphArray[i] = reader.ReadUShort();
            }
        }
    }
}