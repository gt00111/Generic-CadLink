using System;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GenericCadLink.Macro
{
    /// <summary>
    /// SolidWorks 2022 C# マクロのエントリポイント。
    /// ツール > マクロ > 実行 から呼び出す。
    /// </summary>
    public partial class BendExportMacro
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

        private static void ShowResult(Models.BendPackage package, string outputPath)
        {
            var lines = new System.Text.StringBuilder();

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
}
