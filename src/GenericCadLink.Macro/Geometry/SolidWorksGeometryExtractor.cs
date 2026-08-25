using System;
using System.Collections.Generic;
using GenericCadLink.Macro.Models;
using SolidWorks.Interop.sldworks;

namespace GenericCadLink.Macro.Geometry
{
    internal sealed class SolidWorksGeometryExtractor
    {
        private readonly ModelDoc2 _model;

        public SolidWorksGeometryExtractor(ModelDoc2 model) { _model = model; }

        public bool TryCreateFixedFrame(IFeature flatPattern, out CoordinateFrame frame, out FixedFaceInfo fixedFace, out Face2 fixedFaceObject, out string error)
        {
            frame = null; fixedFace = null; fixedFaceObject = null; error = null;
            var data = flatPattern?.GetDefinition() as IFlatPatternFeatureData;
            if (data == null) { error = "FIXED_FACE_DEFINITION_MISSING"; return false; }

            data.AccessSelections(_model, null);
            try
            {
                fixedFaceObject = data.FixedFace2 as Face2;
                if (fixedFaceObject == null) { error = "FIXED_FACE_MISSING"; return false; }
                var normal = ReadVector(fixedFaceObject.Normal);
                normal = VectorMath.Normalize(normal);
                if (normal == null) { error = "FIXED_FACE_NORMAL_MISSING"; return false; }

                Vector3Info origin;
                Vector3Info xAxis;
                if (!TryGetDeterministicFaceAxis(fixedFaceObject, normal, out origin, out xAxis))
                {
                    origin = ReadFacePoint(fixedFaceObject);
                    xAxis = MakePerpendicular(normal);
                }
                var yAxis = VectorMath.Normalize(VectorMath.Cross(normal, xAxis));
                xAxis = VectorMath.Normalize(VectorMath.Cross(yAxis, normal));
                if (origin == null || xAxis == null || yAxis == null) { error = "FIXED_FACE_FRAME_INVALID"; return false; }

                var fixedFacePoint = ReadFacePoint(fixedFaceObject);
                if (fixedFacePoint == null) { error = "FIXED_FACE_POINT_MISSING"; return false; }
                frame = new CoordinateFrame
                {
                    OriginModelMeters = origin,
                    XAxisModel = xAxis,
                    YAxisModel = yAxis,
                    NormalModel = normal,
                    FixedFacePointModel = fixedFacePoint,
                };
                fixedFace = new FixedFaceInfo
                {
                    Side = "unknown",
                    Normal = new Vector3Info(0, 0, 1),
                    Resolved = false,
                    PersistentId = GetPersistentId(fixedFaceObject),
                };
                return true;
            }
            finally { data.ReleaseSelectionAccess(); }
        }

        public FoldedBendGeometry CaptureFoldedGeometry(IFeature feature, Face2 fixedFace, CoordinateFrame frame, double angleDeg)
        {
            var result = new FoldedBendGeometry();
            var faces = feature.GetFaces() as object[];
            if (faces == null) { result.Error = "FEATURE_FACES_MISSING"; return result; }

            var reachable = GetTangentReachableFaces(fixedFace);
            var candidates = new List<FaceCandidate>();
            foreach (var item in faces)
            {
                var face = item as Face2;
                if (face == null || !reachable.Contains(GetPersistentId(face))) continue;
                var normal = VectorMath.Normalize(ReadVector(face.Normal));
                if (normal == null) continue;
                var dot = Math.Max(-1.0, Math.Min(1.0, VectorMath.Dot(frame.NormalModel, normal)));
                var measured = Math.Acos(dot) * 180.0 / Math.PI;
                var error = Math.Min(Math.Abs(measured - angleDeg), Math.Abs((360.0 - measured) - angleDeg));
                if (error <= 1.0)
                    candidates.Add(new FaceCandidate { Face = face, Normal = normal, Point = ReadFacePoint(face), Area = face.GetArea() });
            }

            candidates.Sort((a, b) => b.Area.CompareTo(a.Area));
            if (candidates.Count == 0) { result.Error = "MOVING_FACE_NOT_FOUND"; return result; }
            if (candidates.Count > 1 && Math.Abs(candidates[0].Area - candidates[1].Area) <= 1e-12)
            {
                result.Error = "MOVING_FACE_AMBIGUOUS";
                return result;
            }
            result.BentFaceNormalModel = candidates[0].Normal;
            result.MovingFacePointModel = candidates[0].Point;
            result.MovingFaceId = GetPersistentId(candidates[0].Face);
            result.StationaryFaceId = GetPersistentId(fixedFace);
            return result;
        }

        public bool TryReadFlatAxis(IFeature feature, CoordinateFrame frame, out AxisInfo axis, out string error)
        {
            axis = null; error = null;
            var data = feature.GetDefinition() as IOneBendFeatureData;
            if (data == null) { error = "ONE_BEND_DEFINITION_MISSING"; return false; }
            data.AccessSelections(_model, null);
            try
            {
                var segments = data.FlatPatternSketchSegments2 as object[];
                if (segments == null) { error = "FLAT_BEND_SEGMENTS_MISSING"; return false; }
                SketchLine best = null; double bestLength = 0;
                foreach (var item in segments)
                {
                    var line = item as SketchLine;
                    var segment = item as SketchSegment;
                    if (line == null || segment == null) continue;
                    var length = segment.GetLength();
                    if (length > bestLength) { best = line; bestLength = length; }
                }
                if (best == null || bestLength <= 0) { error = "FLAT_BEND_AXIS_MISSING"; return false; }
                var start = frame.PointToExport(ReadSketchPoint(best.GetStartPoint2()));
                var end = frame.PointToExport(ReadSketchPoint(best.GetEndPoint2()));
                VectorMath.OrderAxisEndpoints(ref start, ref end);
                var direction = VectorMath.Normalize(VectorMath.Subtract(end, start));
                if (direction == null) { error = "BEND_AXIS_DEGENERATE"; return false; }
                axis = new AxisInfo { Start = start, End = end, Direction = direction };
                return true;
            }
            finally { data.ReleaseSelectionAccess(); }
        }

        public double ComputeSignedAngle(AxisInfo axis, CoordinateFrame frame, Vector3Info bentNormalModel, double angleDeg, out string error)
        {
            error = null;
            var bent = frame.VectorToExport(bentNormalModel);
            var triple = VectorMath.Dot(axis.Direction, VectorMath.Cross(new Vector3Info(0, 0, 1), bent));
            if (!VectorMath.IsFinite(triple) || Math.Abs(triple) <= 1e-9) { error = "SIGNED_ANGLE_UNRESOLVED"; return 0; }
            return Math.Sign(triple) * Math.Abs(angleDeg);
        }

        public Vector3Info CreateMovingSidePoint(AxisInfo axis, CoordinateFrame frame)
        {
            var fixedPoint = frame.PointToExport(frame.FixedFacePointModel);
            var side = new Vector3Info(-axis.Direction.Y, axis.Direction.X, 0);
            var midpoint = VectorMath.Scale(VectorMath.Add(axis.Start, axis.End), 0.5);
            var fixedSide = VectorMath.Dot(VectorMath.Subtract(fixedPoint, midpoint), side);
            if (Math.Abs(fixedSide) <= VectorMath.GeometryToleranceMm) return null;
            if (fixedSide > 0) side = VectorMath.Scale(side, -1);
            var candidate = VectorMath.Add(midpoint, VectorMath.Scale(side, Math.Max(1.0, VectorMath.Length(VectorMath.Subtract(axis.End, axis.Start)) * 0.01)));
            candidate.Z = 0;
            return candidate;
        }

        public string GetPersistentId(object entity)
        {
            try
            {
                var bytes = _model.Extension.GetPersistReference3(entity) as byte[];
                return bytes == null ? null : Convert.ToBase64String(bytes);
            }
            catch { return null; }
        }

        private HashSet<string> GetTangentReachableFaces(Face2 start)
        {
            var visited = new HashSet<string>();
            var queue = new Queue<Face2>(); queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var face = queue.Dequeue(); var id = GetPersistentId(face);
                if (id == null || !visited.Add(id)) continue;
                var edges = face.GetEdges() as object[]; if (edges == null) continue;
                foreach (var item in edges)
                {
                    var edge = item as Edge; if (edge == null) continue;
                    var adjacent = edge.GetTwoAdjacentFaces2() as object[]; if (adjacent == null) continue;
                    foreach (var otherItem in adjacent)
                    {
                        var other = otherItem as Face2;
                        if (other != null && AreTangentAcrossEdge(face, other, edge)) queue.Enqueue(other);
                    }
                }
            }
            return visited;
        }

        private static bool AreTangentAcrossEdge(Face2 a, Face2 b, Edge edge)
        {
            try
            {
                var p = ReadEdgeMidpoint(edge);
                var na = ReadFaceNormalAt(a, p); var nb = ReadFaceNormalAt(b, p);
                return na != null && nb != null && Math.Abs(VectorMath.Dot(na, nb)) >= 1.0 - 1e-5;
            }
            catch { return false; }
        }

        private static Vector3Info ReadFaceNormalAt(Face2 face, Vector3Info p)
        {
            var uv = face.ReverseEvaluate(p.X, p.Y, p.Z) as double[];
            var surface = face.GetSurface() as Surface;
            if (uv == null || uv.Length < 2 || surface == null) return null;
            var eval = surface.Evaluate(uv[0], uv[1], 0, 0) as double[];
            if (eval == null || eval.Length < 6) return null;
            var normal = new Vector3Info(eval[eval.Length - 3], eval[eval.Length - 2], eval[eval.Length - 1]);
            if (face.FaceInSurfaceSense()) normal = VectorMath.Scale(normal, -1);
            return VectorMath.Normalize(normal);
        }

        private static Vector3Info ReadFacePoint(Face2 face)
        {
            var uv = face.GetUVBounds() as double[]; var surface = face.GetSurface() as Surface;
            if (uv == null || uv.Length < 4 || surface == null) return null;
            var eval = surface.Evaluate((uv[0] + uv[1]) * 0.5, (uv[2] + uv[3]) * 0.5, 0, 0) as double[];
            return eval == null || eval.Length < 3 ? null : new Vector3Info(eval[0], eval[1], eval[2]);
        }

        private static bool TryGetDeterministicFaceAxis(Face2 face, Vector3Info normal, out Vector3Info origin, out Vector3Info xAxis)
        {
            origin = null; xAxis = null; double longest = 0;
            var edges = face.GetEdges() as object[]; if (edges == null) return false;
            foreach (var item in edges)
            {
                var edge = item as Edge; if (edge == null) continue;
                var startVertex = edge.GetStartVertex() as Vertex; var endVertex = edge.GetEndVertex() as Vertex;
                if (startVertex == null || endVertex == null) continue;
                var start = ReadVector(startVertex.GetPoint()); var end = ReadVector(endVertex.GetPoint());
                var vector = VectorMath.Subtract(end, start);
                vector = VectorMath.Subtract(vector, VectorMath.Scale(normal, VectorMath.Dot(vector, normal)));
                var length = VectorMath.Length(vector); if (length <= longest) continue;
                longest = length; origin = start; xAxis = VectorMath.Normalize(vector);
            }
            return origin != null && xAxis != null;
        }

        private static Vector3Info MakePerpendicular(Vector3Info normal)
        {
            var seed = Math.Abs(normal.X) < 0.9 ? new Vector3Info(1, 0, 0) : new Vector3Info(0, 1, 0);
            return VectorMath.Normalize(VectorMath.Cross(seed, normal));
        }
        private static Vector3Info ReadEdgeMidpoint(Edge edge)
        {
            var curve = edge.GetCurve() as Curve; var p = edge.GetCurveParams2() as double[];
            var value = curve?.Evaluate((p[6] + p[7]) * 0.5) as double[];
            return value == null ? null : new Vector3Info(value[0], value[1], value[2]);
        }
        private static Vector3Info ReadSketchPoint(object pointObject) { var p = pointObject as SketchPoint; return p == null ? null : new Vector3Info(p.X, p.Y, p.Z); }
        private static Vector3Info ReadVector(object value) { var p = value as double[]; return p == null || p.Length < 3 ? null : new Vector3Info(p[0], p[1], p[2]); }

        private sealed class FaceCandidate { public Face2 Face; public Vector3Info Normal; public Vector3Info Point; public double Area; }
    }

    internal sealed class FoldedBendGeometry
    {
        public Vector3Info BentFaceNormalModel { get; set; }
        public Vector3Info MovingFacePointModel { get; set; }
        public string StationaryFaceId { get; set; }
        public string MovingFaceId { get; set; }
        public string Error { get; set; }
    }
}
