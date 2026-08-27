using GenericCadLink.Macro.Models;

namespace GenericCadLink.Macro.Geometry
{
    internal sealed class CoordinateFrame
    {
        public Vector3Info OriginModelMeters { get; set; }
        public Vector3Info XAxisModel { get; set; }
        public Vector3Info YAxisModel { get; set; }
        public Vector3Info NormalModel { get; set; }
        public Vector3Info FixedFacePointModel { get; set; }

        public Vector3Info PointToExport(Vector3Info modelMeters)
        {
            var delta = VectorMath.Subtract(modelMeters, OriginModelMeters);
            return new Vector3Info(
                VectorMath.Dot(delta, XAxisModel) * 1000.0,
                VectorMath.Dot(delta, YAxisModel) * 1000.0,
                VectorMath.Dot(delta, NormalModel) * 1000.0);
        }

        public Vector3Info VectorToExport(Vector3Info modelVector)
        {
            return VectorMath.Normalize(new Vector3Info(
                VectorMath.Dot(modelVector, XAxisModel),
                VectorMath.Dot(modelVector, YAxisModel),
                VectorMath.Dot(modelVector, NormalModel)));
        }

        public object ToDxfAlignment()
        {
            return new[]
            {
                OriginModelMeters.X, OriginModelMeters.Y, OriginModelMeters.Z,
                XAxisModel.X, XAxisModel.Y, XAxisModel.Z,
                YAxisModel.X, YAxisModel.Y, YAxisModel.Z,
                NormalModel.X, NormalModel.Y, NormalModel.Z,
            };
        }

        public CoordinateSystemInfo ToInfo()
        {
            return new CoordinateSystemInfo
            {
                Origin = new Vector3Info(0, 0, 0),
                XAxis = new Vector3Info(1, 0, 0),
                YAxis = new Vector3Info(0, 1, 0),
                FlatNormal = new Vector3Info(0, 0, 1),
            };
        }
    }
}
