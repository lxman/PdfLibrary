namespace PdfLibrary.Fonts.Embedded
{
    /// <summary>
    /// Represents a single point in a glyph contour.
    /// TrueType uses quadratic Bezier curves with on-curve and off-curve control points.
    /// </summary>
    public readonly struct ContourPoint(double x, double y, bool onCurve)
    {
        /// <summary>
        /// X coordinate in font design units (typically 0-2048 for most fonts)
        /// </summary>
        public double X { get; } = x;

        /// <summary>
        /// Y coordinate in font design units
        /// </summary>
        public double Y { get; } = y;

        /// <summary>
        /// True if this point is on the curve (line endpoint or curve anchor).
        /// False if this is an off-curve control point for quadratic Bezier.
        /// </summary>
        public bool OnCurve { get; } = onCurve;

        /// <summary>
        /// Scale point by font size
        /// </summary>
        /// <param name="fontSize">Target font size in points</param>
        /// <param name="unitsPerEm">Font's units per em (typically 1000 or 2048)</param>
        /// <returns>Scaled point coordinates</returns>
        public (double x, double y) Scale(double fontSize, int unitsPerEm)
        {
            double scale = fontSize / unitsPerEm;
            return (X * scale, Y * scale);
        }

        /// <summary>
        /// Transform point by matrix
        /// </summary>
        public ContourPoint Transform(double a, double b, double c, double d, double e, double f)
        {
            double newX = a * X + c * Y + e;
            double newY = b * X + d * Y + f;
            return new ContourPoint(newX, newY, OnCurve);
        }

        public override string ToString()
        {
            return $"({X:F1}, {Y:F1}){(OnCurve ? "" : " [control]")}";
        }

        public override bool Equals(object? obj)
        {
            return obj is ContourPoint point &&
                   Math.Abs(X - point.X) < 0.001 &&
                   Math.Abs(Y - point.Y) < 0.001 &&
                   OnCurve == point.OnCurve;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, OnCurve);
        }

        public static bool operator ==(ContourPoint left, ContourPoint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ContourPoint left, ContourPoint right)
        {
            return !(left == right);
        }
    }
}
