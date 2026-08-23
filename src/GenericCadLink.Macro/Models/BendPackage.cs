using System;
using System.Collections.Generic;

namespace GenericCadLink.Macro.Models
{
    public sealed class BendPackage
    {
        public string SchemaVersion { get; set; } = "0.1";
        public string ExportedAt { get; set; } = "";
        public SourceInfo Source { get; set; } = new SourceInfo();
        public string PartNumber { get; set; } = "";
        public string Revision { get; set; }
        public double? Thickness { get; set; }
        public string Material { get; set; }
        public string FixedFace { get; set; }
        public string Units { get; set; } = "mm";
        public CoordinateSystemInfo CoordinateSystem { get; set; } = new CoordinateSystemInfo();
        public List<BendInfo> Bends { get; set; } = new List<BendInfo>();
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class SourceInfo
    {
        public string Cad { get; set; } = "SolidWorks";
        public string CadVersion { get; set; } = "";
        public string CadLinkVersion { get; set; } = "0.1.0";
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
    }

    public sealed class CoordinateSystemInfo
    {
        public string Origin { get; set; } = "flatPattern";
        public string Description { get; set; } =
            "Phase 1: flat pattern coordinate (detailed definition in Phase 2 with DXF export)";
    }

    public sealed class BendInfo
    {
        public string Id { get; set; } = "";
        public string DxfLayer { get; set; } = "";
        public string Direction { get; set; } = "";
        public double InnerRadius { get; set; }
        public double AngleDeg { get; set; }
        public double? LengthMm { get; set; }
        public string SwFeatureName { get; set; } = "";
    }
}
