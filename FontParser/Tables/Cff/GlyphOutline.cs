using System.Collections.Generic;
using System.Drawing;

namespace FontParser.Tables.Cff
{
    /// <summary>
    /// Represents a parsed CFF glyph outline with structured path commands
    /// </summary>
    public class GlyphOutline
    {
        /// <summary>
        /// The glyph width (if specified in the charstring)
        /// </summary>
        public float? Width { get; set; }

        /// <summary>
        /// List of path commands that make up the glyph
        /// </summary>
        public List<PathCommand> Commands { get; } = new List<PathCommand>();

        /// <summary>
        /// Bounding box minimum X
        /// </summary>
        public float MinX { get; set; }

        /// <summary>
        /// Bounding box minimum Y
        /// </summary>
        public float MinY { get; set; }

        /// <summary>
        /// Bounding box maximum X
        /// </summary>
        public float MaxX { get; set; }

        /// <summary>
        /// Bounding box maximum Y
        /// </summary>
        public float MaxY { get; set; }
    }

    /// <summary>
    /// Base class for path drawing commands
    /// </summary>
    public abstract class PathCommand
    {
        public abstract PathCommandType Type { get; }
    }

    /// <summary>
    /// Types of path commands
    /// </summary>
    public enum PathCommandType
    {
        MoveTo,
        LineTo,
        CubicBezierTo,
        ClosePath
    }

    /// <summary>
    /// Move to a new position (starts a new contour)
    /// </summary>
    public class MoveToCommand : PathCommand
    {
        public override PathCommandType Type => PathCommandType.MoveTo;

        public PointF Point { get; }

        public MoveToCommand(float x, float y)
        {
            Point = new PointF(x, y);
        }

        public MoveToCommand(PointF point)
        {
            Point = point;
        }

        public override string ToString() => $"MoveTo({Point.X}, {Point.Y})";
    }

    /// <summary>
    /// Draw a straight line to a point
    /// </summary>
    public class LineToCommand : PathCommand
    {
        public override PathCommandType Type => PathCommandType.LineTo;

        public PointF Point { get; }

        public LineToCommand(float x, float y)
        {
            Point = new PointF(x, y);
        }

        public LineToCommand(PointF point)
        {
            Point = point;
        }

        public override string ToString() => $"LineTo({Point.X}, {Point.Y})";
    }

    /// <summary>
    /// Draw a cubic Bezier curve (used by CFF/Type2 charstrings)
    /// </summary>
    public class CubicBezierCommand : PathCommand
    {
        public override PathCommandType Type => PathCommandType.CubicBezierTo;

        /// <summary>
        /// First control point
        /// </summary>
        public PointF Control1 { get; }

        /// <summary>
        /// Second control point
        /// </summary>
        public PointF Control2 { get; }

        /// <summary>
        /// End point of the curve
        /// </summary>
        public PointF EndPoint { get; }

        public CubicBezierCommand(PointF control1, PointF control2, PointF endPoint)
        {
            Control1 = control1;
            Control2 = control2;
            EndPoint = endPoint;
        }

        public CubicBezierCommand(float c1x, float c1y, float c2x, float c2y, float ex, float ey)
        {
            Control1 = new PointF(c1x, c1y);
            Control2 = new PointF(c2x, c2y);
            EndPoint = new PointF(ex, ey);
        }

        public override string ToString() =>
            $"CubicBezierTo(C1:{Control1.X},{Control1.Y} C2:{Control2.X},{Control2.Y} E:{EndPoint.X},{EndPoint.Y})";
    }

    /// <summary>
    /// Close the current path/contour
    /// </summary>
    public class ClosePathCommand : PathCommand
    {
        public override PathCommandType Type => PathCommandType.ClosePath;

        public override string ToString() => "ClosePath";
    }
}
