using System;
using GenericCadLink.Macro.Models;

namespace GenericCadLink.Macro.Geometry
{
    internal static class VectorMath
    {
        public const double GeometryToleranceMm = 1e-6;
        public static Vector3Info Add(Vector3Info a, Vector3Info b) => new Vector3Info(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3Info Subtract(Vector3Info a, Vector3Info b) => new Vector3Info(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3Info Scale(Vector3Info v, double s) => new Vector3Info(v.X * s, v.Y * s, v.Z * s);
        public static double Dot(Vector3Info a, Vector3Info b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vector3Info Cross(Vector3Info a, Vector3Info b) => new Vector3Info(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public static double Length(Vector3Info v) => Math.Sqrt(Dot(v, v));
        public static Vector3Info Normalize(Vector3Info v) { var n = Length(v); return !IsFinite(n) || n <= GeometryToleranceMm ? null : Scale(v, 1.0 / n); }
        public static bool IsFinite(Vector3Info v) => v != null && IsFinite(v.X) && IsFinite(v.Y) && IsFinite(v.Z);
        public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public static double DistancePointToLine(Vector3Info p, AxisInfo a) => Length(Cross(Subtract(p, a.Start), a.Direction));

        public static void OrderAxisEndpoints(ref Vector3Info start, ref Vector3Info end)
        {
            if (Compare(start, end) <= 0) return;
            var swap = start; start = end; end = swap;
        }

        private static int Compare(Vector3Info a, Vector3Info b)
        {
            var r = CompareCoordinate(a.X, b.X); if (r != 0) return r;
            r = CompareCoordinate(a.Y, b.Y); return r != 0 ? r : CompareCoordinate(a.Z, b.Z);
        }

        private static int CompareCoordinate(double a, double b) => Math.Abs(a - b) <= GeometryToleranceMm ? 0 : (a < b ? -1 : 1);
    }
}
