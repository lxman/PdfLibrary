namespace PdfLibrary.Fonts.Embedded
{
    /// <summary>
    /// Represents a single contour (closed path) in a glyph outline.
    /// TrueType glyphs are composed of one or more contours.
    /// </summary>
    public class GlyphContour(List<ContourPoint> points, bool isClosed = true)
    {
        /// <summary>
        /// Points that define this contour (on-curve and off-curve control points)
        /// </summary>
        public List<ContourPoint> Points { get; } = points;

        /// <summary>
        /// True if this contour is closed (forms a complete shape)
        /// Most TrueType contours are closed.
        /// </summary>
        public bool IsClosed { get; } = isClosed;

        /// <summary>
        /// Number of on-curve points in this contour
        /// </summary>
        public int OnCurvePointCount => Points.Count(p => p.OnCurve);

        /// <summary>
        /// Number of off-curve control points in this contour
        /// </summary>
        public int OffCurvePointCount => Points.Count(p => !p.OnCurve);

        /// <summary>
        /// Get bounding box of this contour
        /// </summary>
        public (double minX, double minY, double maxX, double maxY) GetBounds()
        {
            if (Points.Count == 0)
                return (0, 0, 0, 0);

            double minX = Points.Min(p => p.X);
            double minY = Points.Min(p => p.Y);
            double maxX = Points.Max(p => p.X);
            double maxY = Points.Max(p => p.Y);

            return (minX, minY, maxX, maxY);
        }

        public override string ToString()
        {
            return $"Contour: {Points.Count} points ({OnCurvePointCount} on-curve, {OffCurvePointCount} control), " +
                   $"{(IsClosed ? "closed" : "open")}";
        }
    }
}
