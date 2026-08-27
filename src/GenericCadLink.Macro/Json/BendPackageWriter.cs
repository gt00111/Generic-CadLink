using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GenericCadLink.Macro.Models;

namespace GenericCadLink.Macro.Json
{
    internal static class BendPackageWriter
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public static void Write(BendPackage package, string filePath)
        {
            File.WriteAllText(filePath, Serialize(package), new UTF8Encoding(false));
        }

        internal static string Serialize(BendPackage p)
        {
            var s = new StringBuilder();
            s.AppendLine("{");
            String(s, "schemaVersion", p.SchemaVersion, 1, true);
            String(s, "exportedAt", p.ExportedAt, 1, true);
            s.AppendLine("  \"source\": {");
            String(s, "cad", p.Source.Cad, 2, true);
            String(s, "cadVersion", p.Source.CadVersion, 2, true);
            String(s, "cadLinkVersion", p.Source.CadLinkVersion, 2, true);
            String(s, "fileName", p.Source.FileName, 2, true);
            String(s, "filePath", p.Source.FilePath, 2, false);
            s.AppendLine("  },");
            String(s, "partNumber", p.PartNumber, 1, true);
            NullableString(s, "revision", p.Revision, 1, true);
            NullableNumber(s, "thickness", p.Thickness, 1, true);
            NullableString(s, "material", p.Material, 1, true);
            CoordinateSystem(s, p.CoordinateSystem);
            FixedFace(s, p.FixedFace);

            s.AppendLine("  \"bends\": [");
            for (var i = 0; i < p.Bends.Count; i++)
            {
                Bend(s, p.Bends[i]);
                s.AppendLine(i + 1 < p.Bends.Count ? "    }," : "    }");
            }
            s.AppendLine("  ],");
            StringArray(s, "errors", p.Errors, 1, true);
            StringArray(s, "warnings", p.Warnings, 1, false);
            s.AppendLine("}");
            return s.ToString();
        }

        private static void CoordinateSystem(StringBuilder s, CoordinateSystemInfo c)
        {
            s.AppendLine("  \"coordinateSystem\": {");
            String(s, "unit", c?.Unit, 2, true);
            String(s, "handedness", c?.Handedness, 2, true);
            String(s, "flatPlane", c?.FlatPlane, 2, true);
            Vector3(s, "origin", c?.Origin, 2, true);
            Vector3(s, "xAxis", c?.XAxis, 2, true);
            Vector3(s, "yAxis", c?.YAxis, 2, true);
            Vector3(s, "flatNormal", c?.FlatNormal, 2, false);
            s.AppendLine("  },");
        }

        private static void FixedFace(StringBuilder s, FixedFaceInfo f)
        {
            s.AppendLine("  \"fixedFace\": {");
            String(s, "side", f?.Side, 2, true);
            Vector3(s, "normal", f?.Normal, 2, true);
            Boolean(s, "resolved", f != null && f.Resolved, 2, true);
            NullableString(s, "persistentId", f?.PersistentId, 2, false);
            s.AppendLine("  },");
        }

        private static void Bend(StringBuilder s, BendInfo b)
        {
            s.AppendLine("    {");
            String(s, "id", b.Id, 3, true);
            Number(s, "innerRadius", b.InnerRadius, 3, true);
            Number(s, "angleDeg", b.AngleDeg, 3, true);
            Number(s, "signedAngleDeg", b.SignedAngleDeg, 3, true);
            String(s, "direction", b.Direction, 3, true);
            s.AppendLine("      \"axis\": {");
            Vector3(s, "start", b.Axis?.Start, 4, true);
            Vector3(s, "end", b.Axis?.End, 4, true);
            Vector3(s, "direction", b.Axis?.Direction, 4, false);
            s.AppendLine("      },");
            NullableString(s, "stationaryFaceId", b.StationaryFaceId, 3, true);
            NullableString(s, "movingFaceId", b.MovingFaceId, 3, true);
            Vector3(s, "movingSidePoint", b.MovingSidePoint, 3, true);
            s.AppendLine("      \"dxfLine\": {");
            Vector2(s, "start", b.DxfLine?.Start, 4, true);
            Vector2(s, "end", b.DxfLine?.End, 4, true);
            String(s, "layer", b.DxfLine?.Layer, 4, true);
            NullableString(s, "handle", b.DxfLine?.Handle, 4, false);
            s.AppendLine("      },");
            String(s, "dxfLayer", b.DxfLayer, 3, true);
            NullableNumber(s, "lengthMm", b.LengthMm, 3, true);
            String(s, "swFeatureName", b.SwFeatureName, 3, false);
        }

        private static void Vector3(StringBuilder s, string key, Vector3Info v, int indent, bool comma)
        {
            Prefix(s, key, indent);
            if (v == null) s.Append("null");
            else s.Append('[').Append(N(v.X)).Append(", ").Append(N(v.Y)).Append(", ").Append(N(v.Z)).Append(']');
            End(s, comma);
        }

        private static void Vector2(StringBuilder s, string key, Vector2Info v, int indent, bool comma)
        {
            Prefix(s, key, indent);
            if (v == null) s.Append("null");
            else s.Append('[').Append(N(v.X)).Append(", ").Append(N(v.Y)).Append(']');
            End(s, comma);
        }

        private static void String(StringBuilder s, string key, string value, int indent, bool comma)
        {
            Prefix(s, key, indent); s.Append('"').Append(Escape(value ?? "")).Append('"'); End(s, comma);
        }
        private static void NullableString(StringBuilder s, string key, string value, int indent, bool comma)
        {
            Prefix(s, key, indent); if (value == null) s.Append("null"); else s.Append('"').Append(Escape(value)).Append('"'); End(s, comma);
        }
        private static void Number(StringBuilder s, string key, double value, int indent, bool comma) { Prefix(s, key, indent); s.Append(N(value)); End(s, comma); }
        private static void NullableNumber(StringBuilder s, string key, double? value, int indent, bool comma) { Prefix(s, key, indent); s.Append(value.HasValue ? N(value.Value) : "null"); End(s, comma); }
        private static void Boolean(StringBuilder s, string key, bool value, int indent, bool comma) { Prefix(s, key, indent); s.Append(value ? "true" : "false"); End(s, comma); }
        private static void StringArray(StringBuilder s, string key, IList<string> values, int indent, bool comma)
        {
            Prefix(s, key, indent); s.Append('[');
            for (var i = 0; i < values.Count; i++) { if (i > 0) s.Append(", "); s.Append('"').Append(Escape(values[i])).Append('"'); }
            s.Append(']'); End(s, comma);
        }
        private static void Prefix(StringBuilder s, string key, int indent) { s.Append(' ', indent * 2).Append('"').Append(Escape(key)).Append("\": "); }
        private static void End(StringBuilder s, bool comma) { if (comma) s.Append(','); s.AppendLine(); }
        private static string N(double value) => value.ToString("0.######", Culture);
        private static string Escape(string value) => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }
}
