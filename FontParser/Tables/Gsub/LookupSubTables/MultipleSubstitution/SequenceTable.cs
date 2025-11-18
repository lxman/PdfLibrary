using FontParser.Reader;

namespace FontParser.Tables.Gsub.LookupSubTables.MultipleSubstitution
{
    public class SequenceTable
    {
        public ushort[] SubstituteGlyphIds { get; }

        public SequenceTable(BigEndianReader reader)
        {
            ushort glyphCount = reader.ReadUShort();
            SubstituteGlyphIds = reader.ReadUShortArray(glyphCount);
        }
    }
}