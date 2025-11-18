using System.Drawing;

namespace PdfLibrary.Fonts.Embedded.Tables.TtTables.Glyf
{
    /// <summary>
    /// Coordinate point in a simple glyph outline
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public struct SimpleGlyphCoordinate
    {
        public PointF Point { get; private set; }

        public bool OnCurve { get; private set; }

        public SimpleGlyphCoordinate(PointF point, bool onCurve)
        {
            Point = point;
            OnCurve = onCurve;
        }

        public void SetPoint(PointF newPoint, bool onCurve)
        {
            Point = newPoint;
            OnCurve = onCurve;
        }

        public override string ToString()
        {
            return $"Point: {Point.X}, {Point.Y} OnCurve: {OnCurve}";
        }
    }
}
