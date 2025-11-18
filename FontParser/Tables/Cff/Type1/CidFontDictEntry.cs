using System.Collections.Generic;

namespace FontParser.Tables.Cff.Type1
{
    public class CidFontDictEntry
    {
        public List<CffDictEntry> Entries { get; } = new List<CffDictEntry>();
    }
}
