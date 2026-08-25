using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GenericCadLink.Macro.Geometry;
using GenericCadLink.Macro.Models;

namespace GenericCadLink.Macro.Dxf
{
    internal static class DxfBendLineMatcher
    {
        private const double MatchToleranceMm = 0.05;

        public static void MatchAndRewrite(string path, IList<BendInfo> bends, IList<string> errors)
        {
            var document = DxfDocument.Read(path);
            document.AssignCutLayer();
            var lines = document.Lines;
            var used = new HashSet<int>();

            foreach (var bend in bends)
            {
                var index = FindBest(lines, bend.Axis, used);
                if (index < 0)
                {
                    errors.Add("DXF_BEND_MATCH_FAILED[" + bend.Id + "]: no one-to-one DXF line within tolerance.");
                    continue;
                }
                used.Add(index);
                var line = lines[index];
                line.Layer = bend.DxfLayer;
                bend.DxfLine = new DxfLineInfo
                {
                    Start = new Vector2Info(line.X1, line.Y1),
                    End = new Vector2Info(line.X2, line.Y2),
                    Layer = bend.DxfLayer,
                    Handle = line.Handle,
                };
            }

            if (used.Count != bends.Count)
                errors.Add("DXF_BEND_COUNT_MISMATCH: JSON bends and DXF bend lines are not one-to-one.");
            document.EnsureLayer("CUT", 7);
            document.EnsureLayer("BEND_UP", 1);
            document.EnsureLayer("BEND_DOWN", 5);
            document.Write(path);
        }

        public static List<AxisInfo> ReadRawBendAxes(string path)
        {
            var result = new List<AxisInfo>();
            var document = DxfDocument.Read(path);
            foreach (var line in document.Lines)
            {
                var lineType = (line.LineType ?? "").ToUpperInvariant();
                var layer = (line.Layer ?? "").ToUpperInvariant();
                if (lineType != "CENTER" && lineType != "PHANTOM" &&
                    layer != "BEND_UP" && layer != "BEND_DOWN") continue;
                var start = new Vector3Info(line.X1, line.Y1, 0);
                var end = new Vector3Info(line.X2, line.Y2, 0);
                VectorMath.OrderAxisEndpoints(ref start, ref end);
                var direction = VectorMath.Normalize(VectorMath.Subtract(end, start));
                if (direction != null) result.Add(new AxisInfo { Start = start, End = end, Direction = direction });
            }
            return result;
        }

        private static int FindBest(IList<DxfLine> lines, AxisInfo axis, ISet<int> used)
        {
            var best = -1; var bestError = double.MaxValue;
            for (var i = 0; i < lines.Count; i++)
            {
                if (used.Contains(i)) continue;
                var direct = Distance(lines[i].X1, lines[i].Y1, axis.Start.X, axis.Start.Y) + Distance(lines[i].X2, lines[i].Y2, axis.End.X, axis.End.Y);
                var reverse = Distance(lines[i].X1, lines[i].Y1, axis.End.X, axis.End.Y) + Distance(lines[i].X2, lines[i].Y2, axis.Start.X, axis.Start.Y);
                var error = Math.Min(direct, reverse);
                if (error < bestError) { best = i; bestError = error; }
            }
            return bestError <= MatchToleranceMm * 2.0 ? best : -1;
        }

        private static double Distance(double x1, double y1, double x2, double y2)
        {
            var dx = x1 - x2; var dy = y1 - y2; return Math.Sqrt(dx * dx + dy * dy);
        }

        private sealed class DxfDocument
        {
            public readonly List<string> Raw = new List<string>();
            public readonly List<DxfLine> Lines = new List<DxfLine>();
            private readonly List<int> _entityLayerValueIndexes = new List<int>();

            public static DxfDocument Read(string path)
            {
                var d = new DxfDocument(); d.Raw.AddRange(File.ReadAllLines(path));
                var inEntities = false;
                for (var i = 0; i + 1 < d.Raw.Count; i += 2)
                {
                    var codeAtI = d.Raw[i].Trim();
                    var valueAtI = d.Raw[i + 1].Trim();
                    if (codeAtI == "2" && valueAtI == "ENTITIES") { inEntities = true; continue; }
                    if (inEntities && codeAtI == "0" && valueAtI == "ENDSEC") { inEntities = false; continue; }
                    if (!inEntities || codeAtI != "0") continue;
                    var isLine = valueAtI == "LINE";
                    var line = new DxfLine { EntityStart = i };
                    for (var j = i + 2; j + 1 < d.Raw.Count; j += 2)
                    {
                        var code = d.Raw[j].Trim(); if (code == "0") { line.EntityEnd = j; break; }
                        var value = d.Raw[j + 1].Trim();
                        if (code == "5") line.Handle = value;
                        else if (code == "6") line.LineType = value;
                        else if (code == "8")
                        {
                            d._entityLayerValueIndexes.Add(j + 1);
                            if (isLine) { line.Layer = value; line.LayerValueIndex = j + 1; }
                        }
                        else if (code == "10") line.X1 = Number(value);
                        else if (code == "20") line.Y1 = Number(value);
                        else if (code == "11") line.X2 = Number(value);
                        else if (code == "21") line.Y2 = Number(value);
                    }
                    if (isLine) d.Lines.Add(line);
                }
                return d;
            }

            public void AssignCutLayer()
            {
                foreach (var index in _entityLayerValueIndexes) Raw[index] = "CUT";
                foreach (var line in Lines) line.Layer = "CUT";
            }

            public void EnsureLayer(string name, int color)
            {
                for (var i = 0; i + 1 < Raw.Count; i += 2)
                    if (Raw[i].Trim() == "2" && Raw[i + 1].Trim() == name) return;

                var tableEnd = -1;
                var inLayerTable = false;
                for (var i = 0; i + 1 < Raw.Count; i += 2)
                {
                    if (Raw[i].Trim() == "2" && Raw[i + 1].Trim() == "LAYER") inLayerTable = true;
                    if (inLayerTable && Raw[i].Trim() == "0" && Raw[i + 1].Trim() == "ENDTAB") { tableEnd = i; break; }
                }
                if (tableEnd < 0) throw new InvalidDataException("DXF_LAYER_TABLE_MISSING");
                var record = new[]
                {
                    "0", "LAYER", "2", name, "70", "0", "62", color.ToString(CultureInfo.InvariantCulture), "6", "CONTINUOUS"
                };
                Raw.InsertRange(tableEnd, record);
                foreach (var line in Lines)
                    if (line.LayerValueIndex >= tableEnd) line.LayerValueIndex += record.Length;
            }

            public void Write(string path)
            {
                foreach (var line in Lines) if (line.LayerValueIndex >= 0) Raw[line.LayerValueIndex] = line.Layer;
                File.WriteAllLines(path, Raw);
            }
            private static double Number(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private sealed class DxfLine
        {
            public int EntityStart, EntityEnd, LayerValueIndex = -1;
            public string Handle, Layer, LineType;
            public double X1, Y1, X2, Y2;
        }
    }
}
