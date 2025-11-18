using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cff.Type1.Charsets
{
    public class CharsetsFormat1 : ICharset
    {
        public List<Range1> Ranges { get; } = new List<Range1>();

        public CharsetsFormat1(BigEndianReader reader, ushort numGlyphs)
        {
            ushort nLeft = numGlyphs;
            while (nLeft > 0)
            {
                Ranges.Add(new Range1(reader));
                nLeft--;
            }
        }
    }
}