using System.Collections.Generic;
using FontParser.Tables.TtTables;
using FontParser.Tables.TtTables.Glyf;

namespace FontParser.Tables.WOFF2.GlyfReconstruct
{
    public class Simple : IGlyphSpec
    {
        public List<SimpleGlyphCoordinate> Coordinates { get; } = new List<SimpleGlyphCoordinate>();

        public List<ushort> EndPtsOfContours { get; } = new List<ushort>();

        public List<byte> Instructions { get; } = new List<byte>();
    }
}