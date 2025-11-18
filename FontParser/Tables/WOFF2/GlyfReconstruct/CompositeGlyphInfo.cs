using System.Collections.Generic;

namespace FontParser.Tables.WOFF2.GlyfReconstruct
{
    public class CompositeGlyphInfo : IGlyphInfo
    {
        public short XMin { get; set; }

        public short YMin { get; set; }

        public short XMax { get; set; }

        public short YMax { get; set; }

        public List<CompositeGlyphElement> Elements { get; } = new List<CompositeGlyphElement>();

        public ushort InstructionCount { get; set; }

        public List<byte> Instructions { get; } = new List<byte>();
    }
}