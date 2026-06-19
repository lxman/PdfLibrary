using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.Cff.Type1.Charsets
{
    public class CharsetsFormat0 : ICharset
    {
        public List<ushort> Glyphs { get; }

        public CharsetsFormat0(BigEndianReader reader, ushort numGlyphs)
        {
            // A format-0 charset stores numGlyphs-1 SIDs/CIDs: GID 0 (.notdef / CID 0) is implicit and
            // not encoded. Reading numGlyphs entries over-reads one phantom value from the next section.
            Glyphs = reader.ReadUShortArray(numGlyphs > 0 ? (uint)(numGlyphs - 1) : 0u).ToList();
        }
    }
}