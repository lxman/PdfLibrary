namespace PdfLibrary.Fonts.Embedded.Tables.TtTables.Glyf
{
    /// <summary>
    /// Represents a single component of a composite glyph
    /// </summary>
    public class GlyphComponent
    {
        public ushort GlyphIndex { get; set; }
        public CompositeGlyphFlags Flags { get; set; }

        // Offset or point numbers (depending on ArgsAreXyValues flag)
        public short Argument1 { get; set; }
        public short Argument2 { get; set; }

        // Transformation matrix components (default to identity if not present)
        public float A { get; set; } = 1.0f;  // X scale
        public float B { get; set; } = 0.0f;  // XY skew
        public float C { get; set; } = 0.0f;  // YX skew
        public float D { get; set; } = 1.0f;  // Y scale
    }

    /// <summary>
    /// Composite glyph (references other glyphs with transformations)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class CompositeGlyph : IGlyphSpec
    {
        public List<GlyphComponent> Components { get; } = new();

        public CompositeGlyph(BigEndianReader reader, GlyphHeader glyphHeader)
        {
            CompositeGlyphFlags flags;

            // Read all components (loop until MoreComponents flag is clear)
            do
            {
                var component = new GlyphComponent();

                // Read flags and glyph index
                flags = (CompositeGlyphFlags)reader.ReadUShort();
                component.Flags = flags;
                component.GlyphIndex = reader.ReadUShort();

                // Read arguments (offset or point indices)
                bool arg1And2AreWords = flags.HasFlag(CompositeGlyphFlags.Arg1And2AreWords);
                if (arg1And2AreWords)
                {
                    component.Argument1 = reader.ReadShort();
                    component.Argument2 = reader.ReadShort();
                }
                else
                {
                    component.Argument1 = (sbyte)reader.ReadByte();
                    component.Argument2 = (sbyte)reader.ReadByte();
                }

                // Read transformation matrix based on flags
                if (flags.HasFlag(CompositeGlyphFlags.WeHaveAScale))
                {
                    // Simple scale (uniform scaling for both X and Y)
                    float scale = ReadF2Dot14(reader);
                    component.A = scale;
                    component.D = scale;
                }
                else if (flags.HasFlag(CompositeGlyphFlags.WeHaveAnXAndYScale))
                {
                    // Separate X and Y scales
                    component.A = ReadF2Dot14(reader);
                    component.D = ReadF2Dot14(reader);
                }
                else if (flags.HasFlag(CompositeGlyphFlags.WeHaveATwoByTwo))
                {
                    // Full 2x2 transformation matrix
                    component.A = ReadF2Dot14(reader);
                    component.B = ReadF2Dot14(reader);
                    component.C = ReadF2Dot14(reader);
                    component.D = ReadF2Dot14(reader);
                }
                // else: use identity matrix (already set as default)

                Components.Add(component);

            } while (flags.HasFlag(CompositeGlyphFlags.MoreComponents));

            // Note: We ignore instructions for now (WeHaveInstructions flag)
            // TrueType hinting instructions are not needed for outline extraction
        }

        /// <summary>
        /// Read F2DOT14 format (signed fixed point: 2 bits for integer, 14 for fraction)
        /// </summary>
        private static float ReadF2Dot14(BigEndianReader reader)
        {
            short value = reader.ReadShort();
            return value / 16384.0f;  // Divide by 2^14
        }
    }
}
