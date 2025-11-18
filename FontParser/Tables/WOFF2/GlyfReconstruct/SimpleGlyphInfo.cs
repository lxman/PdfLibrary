using System.Collections.Generic;
using FontParser.Tables.TtTables.Glyf;

namespace FontParser.Tables.WOFF2.GlyfReconstruct
{
    public class SimpleGlyphInfo : IGlyphInfo
    {
        public short XMin { get; set; }

        public short YMin { get; set; }

        public short XMax { get; set; }

        public short YMax { get; set; }

        public List<SimpleGlyphCoordinate> Coordinates { get; } = new List<SimpleGlyphCoordinate>();

        public List<ushort> EndPointsOfContours { get; } = new List<ushort>();

        public ushort InstructionCount { get; set; }

        public List<byte> Instructions { get; } = new List<byte>();
    }
}