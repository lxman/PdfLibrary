namespace PdfLibrary.Fonts.Embedded
{
    /// <summary>
    /// Glyph metrics for positioning and layout.
    /// All values are in font design units (typically 0-2048).
    /// </summary>
    public class GlyphMetrics
    {
        /// <summary>
        /// Horizontal advance width - distance to advance to next glyph
        /// </summary>
        public int AdvanceWidth { get; }

        /// <summary>
        /// Left side bearing - distance from origin to left edge of glyph
        /// </summary>
        public int LeftSideBearing { get; }

        /// <summary>
        /// Glyph bounding box minimum X coordinate
        /// </summary>
        public short XMin { get; }

        /// <summary>
        /// Glyph bounding box minimum Y coordinate
        /// </summary>
        public short YMin { get; }

        /// <summary>
        /// Glyph bounding box maximum X coordinate
        /// </summary>
        public short XMax { get; }

        /// <summary>
        /// Glyph bounding box maximum Y coordinate
        /// </summary>
        public short YMax { get; }

        public GlyphMetrics(
            int advanceWidth,
            int leftSideBearing,
            short xMin,
            short yMin,
            short xMax,
            short yMax)
        {
            AdvanceWidth = advanceWidth;
            LeftSideBearing = leftSideBearing;
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

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
            double scale = fontSize / unitsPerEm;
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
