using FontParser.Tables.TtTables;

namespace FontParser.Tables.WOFF2.GlyfReconstruct
{
    public class Composite : IGlyphSpec
    {
        public CompositeGlyphFlags Flags { get; set; }

        public ushort GlyphIndex { get; set; }

        public int Argument1 { get; set; }

        public int Argument2 { get; set; }
    }
}