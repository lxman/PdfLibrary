namespace FontParser.Tables.WOFF2.GlyfReconstruct
{
    public class CompositeGlyphElement
    {
        public ushort GlyphIndex { get; set; }

        public CompositeGlyphFlags Flags { get; set; }

        public int Arg1 { get; set; }

        public int Arg2 { get; set; }

        public float[] TransformData { get; } = new float[4];
    }
}