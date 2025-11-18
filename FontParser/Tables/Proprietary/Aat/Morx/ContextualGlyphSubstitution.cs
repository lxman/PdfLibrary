using FontParser.Reader;
using FontParser.Tables.Proprietary.Aat.Morx.StateTables;

namespace FontParser.Tables.Proprietary.Aat.Morx
{
    public class ContextualGlyphSubstitution
    {
        public StxHeader Header { get; }

        public uint SubstitutionTable { get; }

        public ContextualGlyphSubstitution(BigEndianReader reader)
        {
            Header = new StxHeader(reader);
            SubstitutionTable = reader.ReadUInt32();
        }
    }
}