using System;
using System.Collections.Generic;
using System.IO;
using GenericCadLink.Macro.Json;
using GenericCadLink.Macro.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GenericCadLink.Macro
{
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
            "SweepBend",
            "MiterFlange",
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

    If flatPattern.IsSuppressed()
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
                package.Thickness = Round(ToMm(thickness));
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
                package.Warnings.Add("MATERIAL_UNKNOWN: Material name not found.");
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
            var features = (object[])model.FeatureManager.GetFeatures(true);
            if (features == null)
                return;

            foreach (IFeature feature in features)
            {
                var typeName = feature.GetTypeName2();

                if (ExcludedFeatureTypes.Contains(typeName))
                {
                    package.Warnings.Add(
                        string.Format("SKIPPED_FEATURE: '{0}' ({1}) not supported in Phase 1.", feature.Name, typeName));
                }
                else if (BendFeatureTypes.Contains(typeName))
                {
                    bendIndex++;
                    BendInfo bend;
                    string warning;
                    if (TryCreateBendInfo(model, feature, bendIndex, out bend, out warning))
                        package.Bends.Add(bend);
                    else
                    {
                        bendIndex--;
                        if (!string.IsNullOrEmpty(warning))
                            package.Warnings.Add(warning);
                    }
                }
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
                InnerRadius = Round(ToMm(innerRadius)),
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

        private static double ToMm(double valueMeters) => valueMeters * 1000.0;

        private static double Round(double value) =>
            Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }
}
