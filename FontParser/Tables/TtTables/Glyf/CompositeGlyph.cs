using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.WOFF2.GlyfReconstruct;

namespace FontParser.Tables.TtTables.Glyf
{
    /// <summary>
    /// Represents a single component within a composite glyph
    /// </summary>
    public class CompositeGlyphComponent
    {
        public CompositeGlyphFlags Flags { get; }
        public ushort GlyphIndex { get; }
        public int Argument1 { get; }
        public int Argument2 { get; }

        // Transformation matrix components (default to identity)
        public double A { get; } = 1.0;
        public double B { get; } = 0.0;
        public double C { get; } = 0.0;
        public double D { get; } = 1.0;

        public CompositeGlyphComponent(BigEndianReader reader)
        {
            Flags = (CompositeGlyphFlags)reader.ReadUShort();
            GlyphIndex = reader.ReadUShort();

            // Read arguments based on flags
            if (Flags.HasFlag(CompositeGlyphFlags.Arg1And2AreWords))
            {
                if (Flags.HasFlag(CompositeGlyphFlags.ArgsAreXyValues))
                {
                    Argument1 = reader.ReadShort();
                    Argument2 = reader.ReadShort();
                }
                else
                {
                    Argument1 = reader.ReadUShort();
                    Argument2 = reader.ReadUShort();
                }
            }
            else
            {
                if (Flags.HasFlag(CompositeGlyphFlags.ArgsAreXyValues))
                {
                    Argument1 = reader.ReadSByte();
                    Argument2 = reader.ReadSByte();
                }
                else
                {
                    Argument1 = reader.ReadByte();
                    Argument2 = reader.ReadByte();
                }
            }

            // Read transformation matrix
            if (Flags.HasFlag(CompositeGlyphFlags.WeHaveAScale))
            {
                A = D = reader.ReadF2Dot14();
            }
            else if (Flags.HasFlag(CompositeGlyphFlags.WeHaveAnXAndYScale))
            {
                A = reader.ReadF2Dot14();
                D = reader.ReadF2Dot14();
            }
            else if (Flags.HasFlag(CompositeGlyphFlags.WeHaveATwoByTwo))
            {
                A = reader.ReadF2Dot14();
                B = reader.ReadF2Dot14();
                C = reader.ReadF2Dot14();
                D = reader.ReadF2Dot14();
            }
        }
    }

    public class CompositeGlyph : IGlyphSpec
    {
        public List<CompositeGlyphComponent> Components { get; } = new List<CompositeGlyphComponent>();

        public CompositeGlyph(
            BigEndianReader reader,
            GlyphHeader glyphHeader,
            bool woff2Reconstruct = false)
        {
            if (woff2Reconstruct) return;

            // Read all components
            CompositeGlyphFlags flags;
            do
            {
                var component = new CompositeGlyphComponent(reader);
                Components.Add(component);
                flags = component.Flags;
            }
            while (flags.HasFlag(CompositeGlyphFlags.MoreComponents));
        }

        public void Woff2Reconstruct(CompositeGlyphInfo compositeGlyphInfo)
        {
            // Transfer information from CompositeGlyphInfo to this instance
        }
    }
}