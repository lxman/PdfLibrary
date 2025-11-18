using System;

namespace PdfLibrary.Fonts.Embedded.Tables.TtTables
{
    /// <summary>
    /// Simple glyph flags for TrueType outline data
    /// </summary>
    [Flags]
    public enum SimpleGlyphFlags : byte
    {
        OnCurve = 1 << 0,
        XShortVector = 1 << 1,
        YShortVector = 1 << 2,
        Repeat = 1 << 3,
        XIsSameOrPositiveXShortVector = 1 << 4,
        YIsSameOrPositiveYShortVector = 1 << 5,
        OverlapSimple = 1 << 6
    }

    /// <summary>
    /// Composite glyph flags for TrueType compound glyphs
    /// </summary>
    [Flags]
    public enum CompositeGlyphFlags : ushort
    {
        Arg1And2AreWords = 1 << 0,
        ArgsAreXyValues = 1 << 1,
        RoundXyToGrid = 1 << 2,
        WeHaveAScale = 1 << 3,
        MoreComponents = 1 << 5,
        WeHaveAnXAndYScale = 1 << 6,
        WeHaveATwoByTwo = 1 << 7,
        WeHaveInstructions = 1 << 8,
        UseMyMetrics = 1 << 9,
        OverlapCompound = 1 << 10,
        ScaledComponentOffset = 1 << 11,
        UnscaledComponentOffset = 1 << 12
    }
}
