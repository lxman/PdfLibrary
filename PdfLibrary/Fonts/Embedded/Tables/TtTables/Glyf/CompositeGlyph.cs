namespace PdfLibrary.Fonts.Embedded.Tables.TtTables.Glyf
{
    /// <summary>
    /// Composite glyph (references other glyphs with transformations)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class CompositeGlyph : IGlyphSpec
    {
        public CompositeGlyphFlags Flags { get; }

        public ushort GlyphIndex { get; }

        public int Argument1 { get; }

        public int Argument2 { get; }

        public CompositeGlyph(BigEndianReader reader, GlyphHeader glyphHeader)
        {
            Flags = (CompositeGlyphFlags)reader.ReadUShort();
            GlyphIndex = reader.ReadUShort();

            if (Flags.HasFlag(CompositeGlyphFlags.ArgsAreXyValues))
            {
                Argument1 = reader.ReadShort();
                Argument2 = reader.ReadShort();
            }
            else
            {
                Argument1 = 0;
                Argument2 = 0;
            }

            // TODO: Handle scale matrices and additional components if needed
            // For now, we have the basic structure
        }
    }
}
