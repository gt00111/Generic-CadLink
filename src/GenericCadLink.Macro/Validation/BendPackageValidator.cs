using System;
using GenericCadLink.Macro.Geometry;
using GenericCadLink.Macro.Models;

namespace GenericCadLink.Macro.Validation
{
    internal static class BendPackageValidator
    {
        public static void Validate(BendPackage package)
        {
            var cs = package.CoordinateSystem;
            if (cs == null || cs.Unit != "mm" || cs.Handedness != "right" || cs.FlatPlane != "XY" || !IsUnit(cs.XAxis) || !IsUnit(cs.YAxis) || !IsUnit(cs.FlatNormal))
                AddError(package, "COORDINATE_SYSTEM_INVALID: Right-handed XY/mm coordinate system is required.");
            else
            {
                var cross = VectorMath.Normalize(VectorMath.Cross(cs.XAxis, cs.YAxis));
                if (cross == null || VectorMath.Dot(cross, cs.FlatNormal) < 1.0 - 1e-6)
                    AddError(package, "COORDINATE_SYSTEM_NOT_RIGHT_HANDED: xAxis cross yAxis must equal flatNormal.");
            }

            if (package.FixedFace == null || !IsUnit(package.FixedFace.Normal))
                AddError(package, "FIXED_FACE_NORMAL_MISSING: fixedFace.normal is mandatory.");

            if (package.Bends.Count == 0)
                AddError(package, "NO_BENDS: schema v0.3 requires at least one resolved bend.");

            foreach (var bend in package.Bends) ValidateBend(package, bend);
        }

        private static void ValidateBend(BendPackage package, BendInfo bend)
        {
            var prefix = "BEND_GEOMETRY_INVALID[" + bend.Id + "]: ";
            if (bend.Axis == null || !VectorMath.IsFinite(bend.Axis.Start) || !VectorMath.IsFinite(bend.Axis.End) || !IsUnit(bend.Axis.Direction) || VectorMath.Length(VectorMath.Subtract(bend.Axis.End, bend.Axis.Start)) <= VectorMath.GeometryToleranceMm)
                AddError(package, prefix + "axis is missing or degenerate.");
            if (!VectorMath.IsFinite(bend.SignedAngleDeg) || Math.Abs(bend.SignedAngleDeg) <= 1e-9)
                AddError(package, prefix + "signedAngleDeg is missing or zero.");

            var direction = bend.SignedAngleDeg > 0 ? "up" : "down";
            var layer = bend.SignedAngleDeg > 0 ? "BEND_UP" : "BEND_DOWN";
            if (bend.Direction != direction) AddError(package, prefix + "direction disagrees with signedAngleDeg.");
            if (bend.DxfLayer != layer) AddError(package, prefix + "dxfLayer disagrees with signedAngleDeg.");
            if (bend.MovingSidePoint == null || bend.Axis == null || !VectorMath.IsFinite(bend.MovingSidePoint) || VectorMath.DistancePointToLine(bend.MovingSidePoint, bend.Axis) <= VectorMath.GeometryToleranceMm)
                AddError(package, prefix + "movingSidePoint is missing or lies on the bend axis.");
            if (bend.DxfLine == null || bend.DxfLine.Start == null || bend.DxfLine.End == null || bend.DxfLine.Layer != layer)
                AddError(package, prefix + "DXF bend-line mapping is missing or inconsistent.");
            else if (bend.Axis != null)
            {
                var direct = Distance(bend.DxfLine.Start, bend.Axis.Start) + Distance(bend.DxfLine.End, bend.Axis.End);
                var reverse = Distance(bend.DxfLine.Start, bend.Axis.End) + Distance(bend.DxfLine.End, bend.Axis.Start);
                if (Math.Min(direct, reverse) > 0.1)
                    AddError(package, prefix + "DXF line endpoints disagree with axis endpoints.");
            }
        }

        private static double Distance(Vector2Info a, Vector3Info b)
        {
            var dx = a.X - b.X; var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool IsUnit(Vector3Info v) => VectorMath.IsFinite(v) && Math.Abs(VectorMath.Length(v) - 1.0) <= 1e-6;
        private static void AddError(BendPackage p, string error) { if (!p.Errors.Contains(error)) p.Errors.Add(error); }
    }
}
