// 注意: この .cs ファイルは SolidWorks から直接実行できません。
// 実行する場合: macro/BendExportMacro.swb を ツール>マクロ>実行 から選ぶ
// または scripts/build-macro.ps1 で .dll をビルドして実行
//
// SolidWorks 2022 C# マクロ — bend.json エクスポート（DLL ビルド用ソース）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BendExportMacro
{
    /// <summary>
    /// SolidWorks 2022 C# マクロのエントリポイント。
    /// ツール > マクロ > 実行 から呼び出す。
    /// </summary>
    public partial class Macro
    {
        public SldWorks swApp;

        public void Main()
        {
            try
            {
                var exporter = new BendExporter(swApp);
                var package = exporter.ExportActiveDocument();
                var model = (ModelDoc2)swApp.ActiveDoc;
                var partPath = model?.GetPathName() ?? "";

                string outputPath = null;
                if (!string.IsNullOrWhiteSpace(partPath))
                    outputPath = exporter.WriteBendJson(package, partPath);

                ShowResult(package, outputPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Generic CadLink: 予期しないエラー\n\n" + ex.Message,
                    "Generic CadLink",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        public int HookSwShutdown()
        {
            return 0;
        }

        public swDocumentTypes_e GetDocumentType()
        {
            return swDocumentTypes_e.swDocPART;
        }

        private static void ShowResult(BendPackage package, string outputPath)
        {
            var lines = new StringBuilder();

            if (!string.IsNullOrEmpty(outputPath))
                lines.AppendLine("出力: " + outputPath);

            lines.AppendLine("品番: " + package.PartNumber);
            lines.AppendLine("曲げ数: " + package.Bends.Count);

            if (package.Thickness.HasValue)
                lines.AppendLine("板厚: " + package.Thickness.Value + " mm");

            if (!string.IsNullOrEmpty(package.Material))
                lines.AppendLine("材質: " + package.Material);

            foreach (var err in package.Errors)
                lines.AppendLine("[ERROR] " + err);

            foreach (var warn in package.Warnings)
                lines.AppendLine("[WARN] " + warn);

            var icon = package.Errors.Count > 0
                ? MessageBoxIcon.Error
                : package.Warnings.Count > 0
                    ? MessageBoxIcon.Warning
                    : MessageBoxIcon.Information;

            MessageBox.Show(
                lines.ToString(),
                "Generic CadLink — bend.json",
                MessageBoxButtons.OK,
                icon);
        }
    }

    /// <summary>
    /// SolidWorks 2022 板金パーツから bend.json を生成する。
    /// 加工順（sequence）は出力しない（純正 CadLink / M-BEND 分担に合わせる）。
    /// </summary>
    public sealed class BendExporter
    {
        private const double RadToDeg = 180.0 / Math.PI;
        private const string CadLinkVersion = "0.1.0";

        private static readonly HashSet<string> BendFeatureTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "SM3dBend",
            "EdgeFlange",
            "SketchBend",
        };

        private static readonly HashSet<string> ExcludedFeatureTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Hem",
            "Jog",
        };

        private readonly SldWorks _app;

        public BendExporter(SldWorks app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        public BendPackage ExportActiveDocument()
        {
            var model = (ModelDoc2)_app.ActiveDoc;
            return Export(model);
        }

        public BendPackage Export(ModelDoc2 model)
        {
            var package = InitPackage(model);

            if (model == null)
            {
                package.Errors.Add("NO_ACTIVE_DOCUMENT: 開いているドキュメントがありません。");
                return package;
            }

            if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                package.Errors.Add("NOT_PART: 板金パーツ（.sldprt）を開いてください。");
                return package;
            }

            var path = model.GetPathName();
            if (string.IsNullOrWhiteSpace(path))
            {
                package.Errors.Add("UNSAVED_DOCUMENT: ファイルを保存してから実行してください。");
                return package;
            }

            FillSourceAndPartNumber(package, model, path);

            if (!HasFlatPattern(model, package))
                return package;

            var part = (PartDoc)model;
            if (!TryFillThickness(part, model, package))
                return package;

            FillMaterial(part, package);
            FillFixedFace(model, package);
            CollectBends(model, package);

            if (package.Bends.Count == 0 && package.Errors.Count == 0)
                package.Warnings.Add("NO_BENDS: 曲げフィーチャが見つかりませんでした。");

            return package;
        }

        public string WriteBendJson(BendPackage package, string partFilePath)
        {
            var directory = Path.GetDirectoryName(partFilePath);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("出力先ディレクトリを特定できません。");

            var outputPath = Path.Combine(directory, "bend.json");
            BendPackageWriter.Write(package, outputPath);
            return outputPath;
        }

        private static BendPackage InitPackage(ModelDoc2 model)
        {
            return new BendPackage
            {
                ExportedAt = DateTimeOffset.Now.ToString("o"),
                Source = new SourceInfo { CadLinkVersion = CadLinkVersion },
            };
        }

        private void FillSourceAndPartNumber(BendPackage package, ModelDoc2 model, string path)
        {
            package.Source.FilePath = path;
            package.Source.FileName = Path.GetFileName(path);
            package.Source.CadVersion = _app.RevisionNumber();
            package.PartNumber = Path.GetFileNameWithoutExtension(path);
        }

        private static bool HasFlatPattern(ModelDoc2 model, BendPackage package)
        {
            var flatPattern = FindFeatureByType(model, "FlatPattern");
            if (flatPattern == null)
            {
                package.Errors.Add("NO_FLAT_PATTERN: フラットパターンがありません。板金フィーチャを確認してください。");
                return false;
            }

            if (flatPattern.IsSuppressed())
            {
                package.Warnings.Add("FLAT_PATTERN_SUPPRESSED: Flat pattern is suppressed (folded view). Bend export continues.");
            }

            return true;
        }

        private static bool TryFillThickness(PartDoc part, ModelDoc2 model, BendPackage package)
        {
            if (TryGetThicknessFromSheetMetalFeature(model, out var thickness) ||
                TryGetThicknessFromPart(part, out thickness))
            {
                package.Thickness = Round(thickness);
                return true;
            }

            package.Errors.Add("THICKNESS_UNKNOWN: 板厚を取得できません。");
            return false;
        }

        private static bool TryGetThicknessFromSheetMetalFeature(ModelDoc2 model, out double thickness)
        {
            thickness = 0;
            var feature = FindFeatureByType(model, "SheetMetal");
            if (feature == null)
                return false;

            var data = feature.GetDefinition() as ISheetMetalFeatureData;
            if (data == null)
                return false;

            data.AccessSelections(model, null);
            try
            {
                thickness = data.Thickness;
                return thickness > 0;
            }
            finally
            {
                data.ReleaseSelectionAccess();
            }
        }

        private static bool TryGetThicknessFromPart(PartDoc part, out double thickness)
        {
            thickness = 0;
            try
            {
                thickness = part.GetSheetMetalThickness();
                return thickness > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void FillMaterial(PartDoc part, BendPackage package)
        {
            try
            {
                string database;
                string materialName = part.GetMaterialPropertyName2("", out database);
                if (!string.IsNullOrWhiteSpace(materialName))
                    package.Material = materialName.Trim();
            }
            catch
            {
                package.Warnings.Add("MATERIAL_UNKNOWN: マテリアル名を取得できませんでした。");
            }
        }

        private static void FillFixedFace(ModelDoc2 model, BendPackage package)
        {
            var flatPattern = FindFeatureByType(model, "FlatPattern");
            if (flatPattern == null)
                return;

            var data = flatPattern.GetDefinition() as IFlatPatternFeatureData;
            if (data == null)
                return;

            data.AccessSelections(model, null);
            try
            {
                Face2 face = data.FixedFace2;
                if (face == null)
                    return;

                var normal = (double[])face.Normal;
                if (normal == null || normal.Length < 3)
                    return;

                // 参考情報として記録（社内固定ルールなし）
                package.FixedFace = normal[2] >= 0 ? "outer" : "inner";
            }
            catch
            {
                // fixedFace は任意
            }
            finally
            {
                data.ReleaseSelectionAccess();
            }
        }

        private void CollectBends(ModelDoc2 model, BendPackage package)
        {
            var bendIndex = 0;
            var feature = (IFeature)model.FirstFeature();

            while (feature != null)
            {
                var typeName = feature.GetTypeName2();

                if (ExcludedFeatureTypes.Contains(typeName))
                {
                    package.Warnings.Add(
                        $"SKIPPED_FEATURE: '{feature.Name}' ({typeName}) は Phase 1 対象外です。");
                }
                else if (BendFeatureTypes.Contains(typeName))
                {
                    bendIndex++;
                    if (TryCreateBendInfo(model, feature, bendIndex, out var bend, out var warning))
                        package.Bends.Add(bend);
                    else if (!string.IsNullOrEmpty(warning))
                        package.Warnings.Add(warning);
                }

                feature = (IFeature)feature.GetNextFeature();
            }
        }

        private bool TryCreateBendInfo(
            ModelDoc2 model,
            IFeature feature,
            int bendIndex,
            out BendInfo bend,
            out string warning)
        {
            bend = null;
            warning = null;

            var direction = TryGetDirection(model, feature, out var innerRadius, out var angleRad, out var dirWarning);
            if (direction == null)
            {
                warning = $"BEND_PARSE_FAILED: '{feature.Name}' — {dirWarning ?? "曲げ情報を読み取れません。"}";
                return false;
            }

            var lengthMm = TryGetBendLengthMm(model, feature);

            bend = new BendInfo
            {
                Id = "B" + bendIndex,
                Direction = direction,
                DxfLayer = direction == "up" ? "BEND_UP" : "BEND_DOWN",
                InnerRadius = Round(innerRadius),
                AngleDeg = Round(angleRad * RadToDeg),
                LengthMm = lengthMm.HasValue ? Round(lengthMm.Value) : (double?)null,
                SwFeatureName = feature.Name ?? "",
            };

            if (!lengthMm.HasValue)
                warning = $"BEND_LENGTH_UNKNOWN: '{feature.Name}' の曲げ線長を取得できませんでした。";

            return true;
        }

        private static string TryGetDirection(
            ModelDoc2 model,
            IFeature feature,
            out double innerRadius,
            out double angleRad,
            out string error)
        {
            innerRadius = 0;
            angleRad = 0;
            error = null;

            var definition = feature.GetDefinition();
            if (definition == null)
            {
                error = "GetDefinition が null を返しました。";
                return null;
            }

            if (definition is ISheetMetalBendFeatureData bendData)
                return ReadBendFeatureData(model, bendData, out innerRadius, out angleRad, out error);

            if (definition is IEdgeFlangeFeatureData edgeFlange)
                return ReadEdgeFlangeData(model, edgeFlange, out innerRadius, out angleRad, out error);

            if (definition is ISketchBendFeatureData sketchBend)
                return ReadSketchBendData(model, sketchBend, out innerRadius, out angleRad, out error);

            error = "未対応の曲げフィーチャ型です。";
            return null;
        }

        private static string ReadBendFeatureData(
            ModelDoc2 model,
            ISheetMetalBendFeatureData data,
            out double innerRadius,
            out double angleRad,
            out string error)
        {
            data.AccessSelections(model, null);
            try
            {
                innerRadius = data.BendRadius;
                angleRad = data.BendAngle;
                return MapDirection(data.BendDirection, out error);
            }
            finally
            {
                data.ReleaseSelectionAccess();
            }
        }

        private static string ReadEdgeFlangeData(
            ModelDoc2 model,
            IEdgeFlangeFeatureData data,
            out double innerRadius,
            out double angleRad,
            out string error)
        {
            data.AccessSelections(model, null);
            try
            {
                innerRadius = data.BendRadius;
                angleRad = data.BendAngle;
                var direction = MapDirection(data.BendDirection, out error);
                if (direction != null)
                    return direction;
                error = null;
                return data.ReverseDirection ? "down" : "up";
            }
            finally
            {
                data.ReleaseSelectionAccess();
            }
        }

        private static string ReadSketchBendData(
            ModelDoc2 model,
            ISketchBendFeatureData data,
            out double innerRadius,
            out double angleRad,
            out string error)
        {
            data.AccessSelections(model, null);
            try
            {
                innerRadius = data.BendRadius;
                angleRad = data.BendAngle;
                return MapDirection(data.BendDirection, out error);
            }
            finally
            {
                data.ReleaseSelectionAccess();
            }
        }

        private static string MapDirection(swBendDirection_e direction, out string error)
        {
            error = null;
            switch (direction)
            {
                case swBendDirection_e.swBendDirectionUp:
                    return "up";
                case swBendDirection_e.swBendDirectionDown:
                    return "down";
                default:
                    error = "曲げ方向（BendDirection）が未設定です。";
                    return null;
            }
        }

        private static double? TryGetBendLengthMm(ModelDoc2 model, IFeature feature)
        {
            try
            {
                var sketch = feature.GetSpecificFeature2() as Sketch;
                if (sketch == null)
                    return null;

                var segment = (SketchSegment)sketch.GetFirstSegment();
                double total = 0;
                var found = false;

                while (segment != null)
                {
                    if (segment.GetLength() > 0)
                    {
                        total += segment.GetLength();
                        found = true;
                    }
                    segment = (SketchSegment)segment.GetNext();
                }

                // API の GetLength はメートル単位
                return found ? total * 1000.0 : (double?)null;
            }
            catch
            {
                return null;
            }
        }

        private static IFeature FindFeatureByType(ModelDoc2 model, string typeName)
        {
            var feature = (IFeature)model.FirstFeature();
            while (feature != null)
            {
                if (string.Equals(feature.GetTypeName2(), typeName, StringComparison.Ordinal))
                    return feature;
                feature = (IFeature)feature.GetNextFeature();
            }
            return null;
        }

        private static double Round(double value) =>
            Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }

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

        private static void AppendStringArray(StringBuilder sb, string key, List<string> values, int indent, bool trailingComma)
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
