using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cff.Type1.Charsets
{
    public class CharsetsFormat2 : ICharset
    {
        public List<Range2> Ranges { get; } = new List<Range2>();

        public CharsetsFormat2(BigEndianReader reader, ushort numGlyphs)
        {
            for (var i = 0; i < numGlyphs; i++)
            {
                Ranges.Add(new Range2(reader));
            }
        }
    }
}