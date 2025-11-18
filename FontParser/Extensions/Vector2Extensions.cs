using System;
using System.Drawing;
using System.Numerics;

namespace FontParser.Extensions
{
    public static class Vector2Extensions
    {
        public static Vector2 Rotate(this Vector2 v, double degrees)
        {
            double radians = degrees * (Math.PI / 180);
            return new Vector2(
                (float)(v.X * Math.Cos(radians) - v.Y * Math.Sin(radians)),
                (float)(v.X * Math.Sin(radians) + v.Y * Math.Cos(radians))
            );
        }

        public static double Angle(this Vector2 v)
        {
            return Math.Atan2(v.Y, v.X);
        }

        public static double RelativeAngle(this Vector2 v, Vector2 other)
        {
            return Math.Atan2(other.Y - v.Y, other.X - v.X);
        }

        public static double ScalarProjection(this Vector2 v, Vector2 other)
        {
            return Vector2.Dot(v, other) / v.Length();
        }

        public static PointF ToPointF(this Vector2 v)
        {
            return new PointF(v.X, v.Y);
        }
    }
}