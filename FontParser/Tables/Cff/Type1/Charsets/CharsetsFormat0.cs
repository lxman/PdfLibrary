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
            Glyphs = reader.ReadUShortArray(numGlyphs).ToList();
        }
    }
}