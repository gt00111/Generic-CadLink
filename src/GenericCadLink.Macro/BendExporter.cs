using System;
using System.Collections.Generic;
using System.IO;
using GenericCadLink.Macro.Dxf;
using GenericCadLink.Macro.Geometry;
using GenericCadLink.Macro.Json;
using GenericCadLink.Macro.Models;
using GenericCadLink.Macro.Validation;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GenericCadLink.Macro
{
    public sealed class BendExporter
    {
        private const double RadToDeg = 180.0 / Math.PI;
        private const string CadLinkVersion = "0.3.0";
        private readonly SldWorks _app;

        public BendExporter(SldWorks app) { _app = app ?? throw new ArgumentNullException(nameof(app)); }

        public ExportResult ExportActiveDocument()
        {
            return Export((ModelDoc2)_app.ActiveDoc);
        }

        public ExportResult Export(ModelDoc2 model)
        {
            var package = NewPackage();
            if (!ValidateDocument(model, package)) return new ExportResult(package, null, null);

            var path = model.GetPathName();
            FillIdentity(package, path);
            var part = (PartDoc)model;
            if (!FillThickness(part, model, package)) return WriteFailure(package, path);
            FillMaterial(part, package);

            var flatPattern = FindFeatureByType(model, "FlatPattern");
            var geometry = new SolidWorksGeometryExtractor(model);
            CoordinateFrame frame; FixedFaceInfo fixedFace; Face2 fixedFaceObject; string frameError;
            if (!geometry.TryCreateFixedFrame(flatPattern, out frame, out fixedFace, out fixedFaceObject, out frameError))
            {
                package.Errors.Add(frameError + ": cannot create deterministic flat coordinate system.");
                return WriteFailure(package, path);
            }
            package.CoordinateSystem = frame.ToInfo();
            package.FixedFace = fixedFace;

            var candidates = CollectOneBends(model);
            var folded = new List<BendDraft>();
            for (var i = 0; i < candidates.Count; i++)
            {
                var draft = CaptureFoldedBend(model, geometry, candidates[i], fixedFaceObject, frame, i + 1);
                folded.Add(draft);
                if (draft.Error != null) package.Errors.Add("BEND_GEOMETRY_FAILED[" + draft.Id + "]: " + draft.Error);
            }

            var wasSuppressed = flatPattern.IsSuppressed();
            try
            {
                if (wasSuppressed)
                {
                    flatPattern.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature, (int)swInConfigurationOpts_e.swThisConfiguration, null);
                    model.EditRebuild3();
                }

                foreach (var draft in folded)
                {
                    if (draft.Error != null) continue;
                    AxisInfo axis; string axisError;
                    if (!geometry.TryReadFlatAxis(draft.Feature, frame, out axis, out axisError))
                    {
                        package.Errors.Add("BEND_AXIS_FAILED[" + draft.Id + "]: " + axisError);
                        continue;
                    }
                    string signError;
                    var signedAngle = geometry.ComputeSignedAngle(axis, frame, draft.Folded.BentFaceNormalModel, draft.AngleDeg, out signError);
                    if (signError != null)
                    {
                        package.Errors.Add("SIGNED_ANGLE_FAILED[" + draft.Id + "]: " + signError);
                        continue;
                    }
                    var direction = signedAngle > 0 ? "up" : "down";
                    var layer = signedAngle > 0 ? "BEND_UP" : "BEND_DOWN";
                    package.Bends.Add(new BendInfo
                    {
                        Id = draft.Id,
                        InnerRadius = Round(draft.InnerRadiusMm),
                        AngleDeg = Round(draft.AngleDeg),
                        SignedAngleDeg = Round(signedAngle),
                        Direction = direction,
                        Axis = axis,
                        StationaryFaceId = draft.Folded.StationaryFaceId,
                        MovingFaceId = draft.Folded.MovingFaceId,
                        MovingSidePoint = geometry.CreateMovingSidePoint(axis, frame),
                        DxfLayer = layer,
                        LengthMm = Round(VectorMath.Length(VectorMath.Subtract(axis.End, axis.Start))),
                        SwFeatureName = draft.Feature.Name ?? "",
                    });
                }
            }
            finally
            {
                if (wasSuppressed)
                {
                    flatPattern.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature, (int)swInConfigurationOpts_e.swThisConfiguration, null);
                    model.EditRebuild3();
                }
                model.ClearSelection2(true);
            }

            var outputDir = ResolveOutputDirectory(path, package.PartNumber);
            Directory.CreateDirectory(outputDir);
            var dxfPath = Path.Combine(outputDir, "flat.dxf");
            if (!ExportDxf(part, path, dxfPath, frame))
                package.Errors.Add("DXF_EXPORT_FAILED: SolidWorks ExportToDWG2 returned false.");
            else
            {
                try { DxfBendLineMatcher.MatchAndRewrite(dxfPath, package.Bends, package.Errors); }
                catch (Exception ex) { package.Errors.Add("DXF_POSTPROCESS_FAILED: " + ex.Message); }
            }

            BendPackageValidator.Validate(package);
            var jsonPath = Path.Combine(outputDir, "bend.json");
            BendPackageWriter.Write(package, jsonPath);
            return new ExportResult(package, jsonPath, File.Exists(dxfPath) ? dxfPath : null);
        }

        private BendDraft CaptureFoldedBend(ModelDoc2 model, SolidWorksGeometryExtractor geometry, IFeature feature, Face2 fixedFace, CoordinateFrame frame, int index)
        {
            var draft = new BendDraft { Id = "B" + index, Feature = feature };
            var data = feature.GetDefinition() as IOneBendFeatureData;
            if (data == null) { draft.Error = "ONE_BEND_DEFINITION_MISSING"; return draft; }
            data.AccessSelections(model, null);
            try
            {
                draft.AngleDeg = Math.Abs(data.BendAngle * RadToDeg);
                draft.InnerRadiusMm = data.BendRadius * 1000.0;
            }
            finally { data.ReleaseSelectionAccess(); }
            draft.Folded = geometry.CaptureFoldedGeometry(feature, fixedFace, frame, draft.AngleDeg);
            draft.Error = draft.Folded.Error;
            return draft;
        }

        private static List<IFeature> CollectOneBends(ModelDoc2 model)
        {
            var result = new List<IFeature>();
            var feature = (IFeature)model.FirstFeature();
            while (feature != null)
            {
                CollectOneBends(feature, false, result);
                feature = (IFeature)feature.GetNextFeature();
            }
            return result;
        }

        private static void CollectOneBends(IFeature feature, bool underFlatPattern, List<IFeature> result)
        {
            var type = feature.GetTypeName2();
            var inFlat = underFlatPattern || type == "FlatPattern";
            if (type == "OneBend" && !inFlat) result.Add(feature);
            var child = (IFeature)feature.GetFirstSubFeature();
            while (child != null)
            {
                CollectOneBends(child, inFlat, result);
                child = (IFeature)child.GetNextSubFeature();
            }
        }

        private static bool ExportDxf(PartDoc part, string modelPath, string dxfPath, CoordinateFrame frame)
        {
            const int exportSheetMetal = 1;
            const int geometryAndBendLines = 1 + 4;
            return part.ExportToDWG2(dxfPath, modelPath, exportSheetMetal, true, frame.ToDxfAlignment(), false, false, geometryAndBendLines, null);
        }

        private static bool ValidateDocument(ModelDoc2 model, BendPackage package)
        {
            if (model == null) { package.Errors.Add("NO_ACTIVE_DOCUMENT"); return false; }
            if (model.GetType() != (int)swDocumentTypes_e.swDocPART) { package.Errors.Add("NOT_PART"); return false; }
            if (string.IsNullOrWhiteSpace(model.GetPathName())) { package.Errors.Add("UNSAVED_DOCUMENT"); return false; }
            if (FindFeatureByType(model, "FlatPattern") == null) { package.Errors.Add("NO_FLAT_PATTERN"); return false; }
            return true;
        }

        private BendPackage NewPackage() => new BendPackage { ExportedAt = DateTimeOffset.Now.ToString("o"), Source = new SourceInfo { CadLinkVersion = CadLinkVersion } };
        private void FillIdentity(BendPackage p, string path) { p.Source.FilePath = path; p.Source.FileName = Path.GetFileName(path); p.Source.CadVersion = _app.RevisionNumber(); p.PartNumber = Path.GetFileNameWithoutExtension(path); }

        private static bool FillThickness(PartDoc part, ModelDoc2 model, BendPackage package)
        {
            var feature = FindFeatureByType(model, "SheetMetal");
            var data = feature == null ? null : feature.GetDefinition() as ISheetMetalFeatureData;
            if (data == null) { package.Errors.Add("THICKNESS_UNKNOWN"); return false; }
            data.AccessSelections(model, null);
            try
            {
                package.Thickness = Round(data.Thickness * 1000.0);
                if (package.Thickness > 0) return true;
                package.Errors.Add("THICKNESS_UNKNOWN");
                return false;
            }
            finally { data.ReleaseSelectionAccess(); }
        }

        private static void FillMaterial(PartDoc part, BendPackage package)
        {
            try { string database; package.Material = part.GetMaterialPropertyName2("", out database); }
            catch { package.Warnings.Add("MATERIAL_UNKNOWN"); }
        }

        private static string ResolveOutputDirectory(string partPath, string partNumber)
        {
            var root = Path.Combine(Path.GetDirectoryName(partPath), "CadLinkExport");
            var config = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cadlink.config.json");
            if (File.Exists(config))
            {
                var text = File.ReadAllText(config);
                var marker = "\"exportRoot\""; var at = text.IndexOf(marker, StringComparison.Ordinal);
                if (at >= 0) { var colon = text.IndexOf(':', at); var first = text.IndexOf('"', colon + 1); var last = first < 0 ? -1 : text.IndexOf('"', first + 1); if (last > first + 1) root = text.Substring(first + 1, last - first - 1).Replace("\\\\", "\\"); }
            }
            return Path.Combine(root, Sanitize(partNumber));
        }

        private static string Sanitize(string value) { foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-'); return value; }
        private ExportResult WriteFailure(BendPackage p, string partPath) { var dir = ResolveOutputDirectory(partPath, p.PartNumber); Directory.CreateDirectory(dir); var json = Path.Combine(dir, "bend.json"); BendPackageValidator.Validate(p); BendPackageWriter.Write(p, json); return new ExportResult(p, json, null); }
        private static IFeature FindFeatureByType(ModelDoc2 model, string type) { var f = (IFeature)model.FirstFeature(); while (f != null) { if (f.GetTypeName2() == type) return f; f = (IFeature)f.GetNextFeature(); } return null; }
        private static double Round(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

        private sealed class BendDraft { public string Id; public IFeature Feature; public double AngleDeg; public double InnerRadiusMm; public FoldedBendGeometry Folded; public string Error; }
    }

    public sealed class ExportResult
    {
        public ExportResult(BendPackage package, string jsonPath, string dxfPath) { Package = package; JsonPath = jsonPath; DxfPath = dxfPath; }
        public BendPackage Package { get; }
        public string JsonPath { get; }
        public string DxfPath { get; }
    }
}
