using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx
{
    public class NonContextualGlyphSubstitution
    {
        public LookupTable LookupTable { get; }

        public NonContextualGlyphSubstitution(BigEndianReader reader)
        {
            LookupTable = new LookupTable(reader);
        }
    }
}