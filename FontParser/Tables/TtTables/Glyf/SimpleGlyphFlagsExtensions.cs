namespace FontParser.Tables.TtTables.Glyf
{
    public static class SimpleGlyphFlagsExtensions
    {
        public static bool HasRepeat(this SimpleGlyphFlags glyphFlags)
        {
            return (glyphFlags & SimpleGlyphFlags.Repeat) == SimpleGlyphFlags.Repeat;
        }

        public static bool HasXShortVector(this SimpleGlyphFlags glyphFlags)
        {
            return (glyphFlags & SimpleGlyphFlags.XShortVector) == SimpleGlyphFlags.XShortVector;
        }

        public static bool HasYShortVector(this SimpleGlyphFlags glyphFlags)
        {
            return (glyphFlags & SimpleGlyphFlags.YShortVector) == SimpleGlyphFlags.YShortVector;
        }

        public static bool HasXIsSameOrPositiveXShortVector(this SimpleGlyphFlags glyphFlags)
        {
            return (glyphFlags & SimpleGlyphFlags.XIsSameOrPositiveXShortVector) == SimpleGlyphFlags.XIsSameOrPositiveXShortVector;
        }

        public static bool HasYIsSameOrPositiveYShortVector(this SimpleGlyphFlags glyphFlags)
        {
            return (glyphFlags & SimpleGlyphFlags.YIsSameOrPositiveYShortVector) == SimpleGlyphFlags.YIsSameOrPositiveYShortVector;
        }

        public static bool HasOnCurve(this SimpleGlyphFlags glyphFlags)
        {
            return (glyphFlags & SimpleGlyphFlags.OnCurve) == SimpleGlyphFlags.OnCurve;
        }
    }
}