using System.Drawing;
using System.Numerics;

namespace FontParser.Extensions
{
    public static class PointFExtensions
    {
        public static Vector2 ToVector2(this PointF point)
        {
            return new Vector2(point.X, point.Y);
        }
    }
}