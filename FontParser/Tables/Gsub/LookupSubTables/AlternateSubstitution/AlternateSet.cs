using FontParser.Reader;

namespace FontParser.Tables.Gsub.LookupSubTables.AlternateSubstitution
{
    public class AlternateSet
    {
        public ushort[] AlternateGlyphIds { get; }

        public AlternateSet(BigEndianReader reader)
        {
            ushort glyphCount = reader.ReadUShort();
            AlternateGlyphIds = reader.ReadUShortArray(glyphCount);
        }
    }
}