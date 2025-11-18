namespace PdfLibrary.Fonts.Embedded.Tables.TtTables.Glyf
{
    /// <summary>
    /// Container for glyph data (header + outline specification)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
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
