namespace PdfLibrary.Fonts.Embedded
{
    /// <summary>
    /// Glyph metrics for positioning and layout.
    /// All values are in font design units (typically 0-2048).
    /// </summary>
    public class GlyphMetrics(
        int advanceWidth,
        int leftSideBearing,
        short xMin,
        short yMin,
        short xMax,
        short yMax)
    {
        /// <summary>
        /// Horizontal advance width - distance to advance to next glyph
        /// </summary>
        public int AdvanceWidth { get; } = advanceWidth;

        /// <summary>
        /// Left side bearing - distance from origin to left edge of glyph
        /// </summary>
        public int LeftSideBearing { get; } = leftSideBearing;

        /// <summary>
        /// Glyph bounding box minimum X coordinate
        /// </summary>
        public short XMin { get; } = xMin;

        /// <summary>
        /// Glyph bounding box minimum Y coordinate
        /// </summary>
        public short YMin { get; } = yMin;

        /// <summary>
        /// Glyph bounding box maximum X coordinate
        /// </summary>
        public short XMax { get; } = xMax;

        /// <summary>
        /// Glyph bounding box maximum Y coordinate
        /// </summary>
        public short YMax { get; } = yMax;

        /// <summary>
        /// Width of the glyph bounding box
        /// </summary>
        public int Width => XMax - XMin;

        /// <summary>
        /// Height of the glyph bounding box
        /// </summary>
        public int Height => YMax - YMin;

        /// <summary>
        /// Right side bearing - distance from right edge of glyph to advance width
        /// </summary>
        public int RightSideBearing => AdvanceWidth - LeftSideBearing - Width;

        /// <summary>
        /// Scale metrics to target font size
        /// </summary>
        public (double advanceWidth, double lsb, double xMin, double yMin, double xMax, double yMax) Scale(
            double fontSize,
            int unitsPerEm)
        {
            var scale = fontSize / unitsPerEm;
            return (
                AdvanceWidth * scale,
                LeftSideBearing * scale,
                XMin * scale,
                YMin * scale,
                XMax * scale,
                YMax * scale
            );
        }

        public override string ToString()
        {
            return $"Advance={AdvanceWidth}, LSB={LeftSideBearing}, " +
                   $"Bounds=[{XMin},{YMin} - {XMax},{YMax}] ({Width}x{Height})";
        }
    }
}
