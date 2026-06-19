using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cff.Type1.Charsets
{
    public class CharsetsFormat1 : ICharset
    {
        public List<Range1> Ranges { get; } = new();

        public CharsetsFormat1(BigEndianReader reader, ushort numGlyphs)
        {
            // Charset covers all glyphs except GID 0 (.notdef). Each Range1 covers (NumberLeft + 1)
            // glyphs; the number of ranges is implicit, deduced by accumulating coverage.
            int remaining = numGlyphs - 1;
            while (remaining > 0)
            {
                var range = new Range1(reader);
                Ranges.Add(range);
                remaining -= range.NumberLeft + 1;
            }
        }
    }
}