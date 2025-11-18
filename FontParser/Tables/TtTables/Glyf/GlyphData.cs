namespace FontParser.Tables.TtTables.Glyf
{
    public class GlyphData
    {
        public int Index { get; }

        public GlyphHeader Header { get; }

        public IGlyphSpec GlyphSpec { get; }

        public GlyphData(int index, GlyphHeader header, IGlyphSpec glyphSpec)
        {
            Index = index;
            Header = header;
            GlyphSpec = glyphSpec;
        }
    }
}