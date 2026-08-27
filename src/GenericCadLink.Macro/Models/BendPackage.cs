using System.Collections.Generic;

namespace GenericCadLink.Macro.Models
{
    public sealed class BendPackage
    {
        public string SchemaVersion { get; set; } = "0.3";
        public string ExportedAt { get; set; } = "";
        public SourceInfo Source { get; set; } = new SourceInfo();
        public string PartNumber { get; set; } = "";
        public string Revision { get; set; }
        public double? Thickness { get; set; }
        public string Material { get; set; }
        public CoordinateSystemInfo CoordinateSystem { get; set; }
        public FixedFaceInfo FixedFace { get; set; }
        public List<BendInfo> Bends { get; set; } = new List<BendInfo>();
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class SourceInfo
    {
        public string Cad { get; set; } = "SolidWorks";
        public string CadVersion { get; set; } = "";
        public string CadLinkVersion { get; set; } = "0.3.0";
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
    }

    public sealed class CoordinateSystemInfo
    {
        public string Unit { get; set; } = "mm";
        public string Handedness { get; set; } = "right";
        public string FlatPlane { get; set; } = "XY";
        public Vector3Info Origin { get; set; }
        public Vector3Info XAxis { get; set; }
        public Vector3Info YAxis { get; set; }
        public Vector3Info FlatNormal { get; set; }
    }

    public sealed class FixedFaceInfo
    {
        public string Side { get; set; } = "unknown";
        public Vector3Info Normal { get; set; }
        public bool Resolved { get; set; }
        public string PersistentId { get; set; }
    }

    public sealed class BendInfo
    {
        public string Id { get; set; } = "";
        public double InnerRadius { get; set; }
        public double AngleDeg { get; set; }
        public double SignedAngleDeg { get; set; }
        public string Direction { get; set; } = "";
        public AxisInfo Axis { get; set; }
        public string StationaryFaceId { get; set; }
        public string MovingFaceId { get; set; }
        public Vector3Info MovingSidePoint { get; set; }
        public DxfLineInfo DxfLine { get; set; }
        public string DxfLayer { get; set; } = "";
        public double? LengthMm { get; set; }
        public string SwFeatureName { get; set; } = "";
    }

    public sealed class AxisInfo
    {
        public Vector3Info Start { get; set; }
        public Vector3Info End { get; set; }
        public Vector3Info Direction { get; set; }
    }

    public sealed class DxfLineInfo
    {
        public Vector2Info Start { get; set; }
        public Vector2Info End { get; set; }
        public string Layer { get; set; } = "";
        public string Handle { get; set; }
    }

    public sealed class Vector2Info
    {
        public Vector2Info() { }
        public Vector2Info(double x, double y) { X = x; Y = y; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class Vector3Info
    {
        public Vector3Info() { }
        public Vector3Info(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}
