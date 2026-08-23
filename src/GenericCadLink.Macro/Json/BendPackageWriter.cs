using System;
using System.Globalization;
using System.IO;
using System.Text;
using GenericCadLink.Macro.Models;

namespace GenericCadLink.Macro.Json
{
    internal static class BendPackageWriter
    {
        public static void Write(BendPackage package, string filePath)
        {
            var json = Serialize(package);
            File.WriteAllText(filePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        internal static string Serialize(BendPackage package)
        {
            var sb = new StringBuilder();
            var culture = CultureInfo.InvariantCulture;

            sb.AppendLine("{");
            AppendString(sb, "schemaVersion", package.SchemaVersion, indent: 1, trailingComma: true);
            AppendString(sb, "exportedAt", package.ExportedAt, indent: 1, trailingComma: true);

            sb.AppendLine("  \"source\": {");
            AppendString(sb, "cad", package.Source.Cad, indent: 2, trailingComma: true);
            AppendString(sb, "cadVersion", package.Source.CadVersion, indent: 2, trailingComma: true);
            AppendString(sb, "cadLinkVersion", package.Source.CadLinkVersion, indent: 2, trailingComma: true);
            AppendString(sb, "fileName", package.Source.FileName, indent: 2, trailingComma: true);
            AppendString(sb, "filePath", package.Source.FilePath, indent: 2, trailingComma: false);
            sb.AppendLine("  },");

            AppendString(sb, "partNumber", package.PartNumber, indent: 1, trailingComma: true);
            AppendNullableString(sb, "revision", package.Revision, indent: 1, trailingComma: true);
            AppendNullableNumber(sb, "thickness", package.Thickness, culture, indent: 1, trailingComma: true);
            AppendNullableString(sb, "material", package.Material, indent: 1, trailingComma: true);
            AppendNullableString(sb, "fixedFace", package.FixedFace, indent: 1, trailingComma: true);
            AppendString(sb, "units", package.Units, indent: 1, trailingComma: true);

            sb.AppendLine("  \"coordinateSystem\": {");
            AppendString(sb, "origin", package.CoordinateSystem.Origin, indent: 2, trailingComma: true);
            AppendString(sb, "description", package.CoordinateSystem.Description, indent: 2, trailingComma: false);
            sb.AppendLine("  },");

            sb.AppendLine("  \"bends\": [");
            for (var i = 0; i < package.Bends.Count; i++)
            {
                var bend = package.Bends[i];
                sb.AppendLine("    {");
                AppendString(sb, "id", bend.Id, indent: 3, trailingComma: true);
                AppendString(sb, "dxfLayer", bend.DxfLayer, indent: 3, trailingComma: true);
                AppendString(sb, "direction", bend.Direction, indent: 3, trailingComma: true);
                AppendNumber(sb, "innerRadius", bend.InnerRadius, culture, indent: 3, trailingComma: true);
                AppendNumber(sb, "angleDeg", bend.AngleDeg, culture, indent: 3, trailingComma: true);
                AppendNullableNumber(sb, "lengthMm", bend.LengthMm, culture, indent: 3, trailingComma: true);
                AppendString(sb, "swFeatureName", bend.SwFeatureName, indent: 3, trailingComma: false);
                sb.Append("    }");
                sb.AppendLine(i < package.Bends.Count - 1 ? "," : "");
            }
            sb.AppendLine("  ],");

            AppendStringArray(sb, "errors", package.Errors, indent: 1, trailingComma: true);
            AppendStringArray(sb, "warnings", package.Warnings, indent: 1, trailingComma: false);

            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void AppendString(StringBuilder sb, string key, string value, int indent, bool trailingComma)
        {
            sb.Append(new string(' ', indent * 2));
            sb.Append('"').Append(Escape(key)).Append("\": ");
            sb.Append('"').Append(Escape(value ?? "")).Append('"');
            if (trailingComma) sb.Append(',');
            sb.AppendLine();
        }

        private static void AppendNullableString(StringBuilder sb, string key, string value, int indent, bool trailingComma)
        {
            sb.Append(new string(' ', indent * 2));
            sb.Append('"').Append(Escape(key)).Append("\": ");
            if (value == null)
                sb.Append("null");
            else
                sb.Append('"').Append(Escape(value)).Append('"');
            if (trailingComma) sb.Append(',');
            sb.AppendLine();
        }

        private static void AppendNumber(StringBuilder sb, string key, double value, CultureInfo culture, int indent, bool trailingComma)
        {
            sb.Append(new string(' ', indent * 2));
            sb.Append('"').Append(Escape(key)).Append("\": ");
            sb.Append(value.ToString("0.####", culture));
            if (trailingComma) sb.Append(',');
            sb.AppendLine();
        }

        private static void AppendNullableNumber(StringBuilder sb, string key, double? value, CultureInfo culture, int indent, bool trailingComma)
        {
            sb.Append(new string(' ', indent * 2));
            sb.Append('"').Append(Escape(key)).Append("\": ");
            if (value.HasValue)
                sb.Append(value.Value.ToString("0.####", culture));
            else
                sb.Append("null");
            if (trailingComma) sb.Append(',');
            sb.AppendLine();
        }

        private static void AppendStringArray(StringBuilder sb, string key, System.Collections.Generic.List<string> values, int indent, bool trailingComma)
        {
            sb.Append(new string(' ', indent * 2));
            sb.Append('"').Append(Escape(key)).Append("\": [");
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(Escape(values[i])).Append('"');
            }
            sb.Append(']');
            if (trailingComma) sb.Append(',');
            sb.AppendLine();
        }

        private static string Escape(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }
    }
}
