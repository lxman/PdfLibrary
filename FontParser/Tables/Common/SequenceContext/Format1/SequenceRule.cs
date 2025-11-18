using FontParser.Reader;

namespace FontParser.Tables.Common.SequenceContext.Format1
{
    public class SequenceRule
    {
        public ushort[] GlyphIds { get; }

        public SequenceRule(BigEndianReader reader)
        {
            ushort glyphCount = reader.ReadUShort();
            GlyphIds = reader.ReadUShortArray(glyphCount);
        }
    }
}