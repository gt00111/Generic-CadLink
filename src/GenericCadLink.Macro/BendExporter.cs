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

            string discoveryError;
            var candidates = CollectBendCandidates(model, out discoveryError);
            if (discoveryError != null) package.Errors.Add(discoveryError);
            var folded = new List<BendDraft>();
            for (var i = 0; i < candidates.Count; i++)
            {
                var draft = CreateBendDraft(model, candidates[i], i + 1);
                folded.Add(draft);
                if (draft.Error != null) package.Errors.Add("BEND_DEFINITION_FAILED[" + draft.Id + "]: " + draft.Error);
            }

            var outputDir = ResolveOutputDirectory(path, package.PartNumber);
            Directory.CreateDirectory(outputDir);
            var dxfPath = Path.Combine(outputDir, "flat.dxf");
            var dxfExported = ExportDxf(part, path, dxfPath, frame);
            if (!dxfExported) package.Errors.Add("DXF_EXPORT_FAILED: SolidWorks ExportToDWG2 returned false.");
            var rawDxfAxes = dxfExported ? DxfBendLineMatcher.ReadRawBendAxes(dxfPath) : new List<AxisInfo>();

            var wasSuppressed = flatPattern.IsSuppressed();
            try
            {
                if (wasSuppressed)
                {
                    flatPattern.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature, (int)swInConfigurationOpts_e.swThisConfiguration, null);
                    model.EditRebuild3();
                }

                for (var draftIndex = 0; draftIndex < folded.Count; draftIndex++)
                {
                    var draft = folded[draftIndex];
                    if (draft.Error != null) continue;
                    string axisError;
                    if (!geometry.TryReadFlatAxis(draft.FlatFeature, frame, out draft.Axis, out axisError))
                    {
                        if (rawDxfAxes.Count == folded.Count)
                            draft.Axis = rawDxfAxes[draftIndex];
                        else
                        {
                            package.Errors.Add("BEND_AXIS_FAILED[" + draft.Id + "]: " + axisError +
                                "; DXF bend axes=" + rawDxfAxes.Count + ", expected=" + folded.Count + ".");
                            draft.Error = axisError;
                        }
                    }
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

            foreach (var draft in folded)
            {
                if (draft.Error != null || draft.Axis == null) continue;
                draft.Folded = geometry.CaptureFoldedGeometry(draft.Feature, fixedFaceObject, frame, draft.Axis, draft.AngleDeg);
                if (draft.Folded.Error != null)
                {
                    package.Errors.Add("BEND_GEOMETRY_FAILED[" + draft.Id + "]: " + draft.Folded.Error);
                    continue;
                }
                var movingSidePoint = geometry.CreateMovingSidePoint(draft.Axis, frame);
                if (!geometry.OrientAxisToMovingSide(draft.Axis, movingSidePoint))
                {
                    package.Errors.Add("BEND_AXIS_ORIENTATION_FAILED[" + draft.Id + "]: moving side is unresolved.");
                    continue;
                }
                movingSidePoint = geometry.CreateMovingSidePoint(draft.Axis, frame);
                string signError;
                var signedAngle = geometry.ComputeSignedAngle(draft.Axis, frame, draft.Folded.BentFaceNormalModel, draft.AngleDeg, out signError);
                if (signError != null) { package.Errors.Add("SIGNED_ANGLE_FAILED[" + draft.Id + "]: " + signError); continue; }
                var direction = signedAngle > 0 ? "up" : "down";
                var layer = signedAngle > 0 ? "BEND_UP" : "BEND_DOWN";
                package.Bends.Add(new BendInfo
                {
                    Id = draft.Id, InnerRadius = Round(draft.InnerRadiusMm), AngleDeg = Round(draft.AngleDeg),
                    SignedAngleDeg = Round(signedAngle), Direction = direction, Axis = draft.Axis,
                    StationaryFaceId = draft.Folded.StationaryFaceId, MovingFaceId = draft.Folded.MovingFaceId,
                    MovingSidePoint = movingSidePoint, DxfLayer = layer,
                    LengthMm = Round(VectorMath.Length(VectorMath.Subtract(draft.Axis.End, draft.Axis.Start))),
                    SwFeatureName = draft.Feature.Name ?? "",
                });
            }

            if (dxfExported)
            {
                try { DxfBendLineMatcher.MatchAndRewrite(dxfPath, package.Bends, package.Errors); }
                catch (Exception ex) { package.Errors.Add("DXF_POSTPROCESS_FAILED: " + ex.Message); }
            }

            BendPackageValidator.Validate(package);
            var jsonPath = Path.Combine(outputDir, "bend.json");
            BendPackageWriter.Write(package, jsonPath);
            return new ExportResult(package, jsonPath, File.Exists(dxfPath) ? dxfPath : null);
        }

        private BendDraft CreateBendDraft(ModelDoc2 model, BendCandidate candidate, int index)
        {
            var feature = candidate.FoldedFeature;
            var draft = new BendDraft { Id = "B" + index, Feature = feature, FlatFeature = candidate.FlatFeature };
            var definition = feature.GetDefinition();
            var oneBend = definition as IOneBendFeatureData;
            var sketchedBend = definition as ISketchedBendFeatureData;
            if (oneBend != null)
            {
                oneBend.AccessSelections(model, null);
                try
                {
                    draft.AngleDeg = Math.Abs(oneBend.BendAngle * RadToDeg);
                    draft.InnerRadiusMm = oneBend.BendRadius * 1000.0;
                }
                finally { oneBend.ReleaseSelectionAccess(); }
            }
            else if (sketchedBend != null)
            {
                sketchedBend.AccessSelections(model, null);
                try
                {
                    draft.AngleDeg = Math.Abs(sketchedBend.BendAngle * RadToDeg);
                    draft.InnerRadiusMm = sketchedBend.BendRadius * 1000.0;
                }
                finally { sketchedBend.ReleaseSelectionAccess(); }
            }
            else { draft.Error = "BEND_DEFINITION_UNSUPPORTED: " + feature.GetTypeName2(); return draft; }
            return draft;
        }

        private static List<BendCandidate> CollectBendCandidates(ModelDoc2 model, out string error)
        {
            var oneBends = new List<IFeature>();
            var userBends = new List<IFeature>();
            var flatPattern = new List<IFeature>();
            var feature = (IFeature)model.FirstFeature();
            while (feature != null)
            {
                CollectBendFeatures(feature, false, oneBends, userBends, flatPattern);
                feature = (IFeature)feature.GetNextFeature();
            }
            error = null;
            var result = new List<BendCandidate>();
            var folded = oneBends.Count > 0 ? oneBends : userBends;
            if (folded.Count == 0 && flatPattern.Count == 0) return result;
            if (folded.Count != flatPattern.Count)
            {
                error = "BEND_DISCOVERY_COUNT_MISMATCH: folded=" + folded.Count + ", flatPattern=" + flatPattern.Count + ".";
                return result;
            }
            for (var i = 0; i < folded.Count; i++)
                result.Add(new BendCandidate { FoldedFeature = folded[i], FlatFeature = flatPattern[i] });
            return result;
        }

        private static void CollectBendFeatures(IFeature feature, bool underFlatPattern,
            List<IFeature> oneBends, List<IFeature> userBends, List<IFeature> flatPattern)
        {
            var type = feature.GetTypeName2();
            var inFlat = underFlatPattern || type == "FlatPattern";
            if (inFlat && (type == "OneBend" || type == "UiBend"))
            {
                if (!ContainsFeature(flatPattern, feature)) flatPattern.Add(feature);
            }
            else if (!inFlat && type == "OneBend")
            {
                if (!ContainsFeature(oneBends, feature)) oneBends.Add(feature);
            }
            else if (!inFlat && (type == "SketchBend" || type == "SM3dBend" || type == "EdgeFlange" || type == "MiterFlange"))
            {
                if (!ContainsFeature(userBends, feature)) userBends.Add(feature);
            }
            var child = (IFeature)feature.GetFirstSubFeature();
            while (child != null)
            {
                CollectBendFeatures(child, inFlat, oneBends, userBends, flatPattern);
                child = (IFeature)child.GetNextSubFeature();
            }
        }

        private static bool ContainsFeature(List<IFeature> features, IFeature candidate)
        {
            var name = candidate.Name ?? "";
            var type = candidate.GetTypeName2() ?? "";
            foreach (var feature in features)
                if ((feature.Name ?? "") == name && (feature.GetTypeName2() ?? "") == type) return true;
            return false;
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

        private sealed class BendCandidate { public IFeature FoldedFeature; public IFeature FlatFeature; }
        private sealed class BendDraft { public string Id; public IFeature Feature; public IFeature FlatFeature; public double AngleDeg; public double InnerRadiusMm; public AxisInfo Axis; public FoldedBendGeometry Folded; public string Error; }
    }

    public sealed class ExportResult
    {
        public ExportResult(BendPackage package, string jsonPath, string dxfPath) { Package = package; JsonPath = jsonPath; DxfPath = dxfPath; }
        public BendPackage Package { get; }
        public string JsonPath { get; }
        public string DxfPath { get; }
    }
}
