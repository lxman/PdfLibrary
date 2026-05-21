using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cff.Type1.Charsets
{
    public class CharsetsFormat2 : ICharset
    {
        public List<Range2> Ranges { get; } = new List<Range2>();

        public CharsetsFormat2(BigEndianReader reader, ushort numGlyphs)
        {
            // Same shape as Format 1 but with uint16 NumberLeft (used when a single range may
            // cover more than 256 glyphs). Number of ranges is implicit.
            int remaining = numGlyphs - 1;
            while (remaining > 0)
            {
                var range = new Range2(reader);
                Ranges.Add(range);
                remaining -= range.NumberLeft + 1;
            }
        }
    }
}