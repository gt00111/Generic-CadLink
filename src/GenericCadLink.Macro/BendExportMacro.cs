using System;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GenericCadLink.Macro
{
    /// <summary>SolidWorks 2022 C# macro entry point.</summary>
    public partial class BendExportMacro
    {
        public SldWorks swApp;

        public void Main()
        {
            try
            {
                ShowResult(new BendExporter(swApp).ExportActiveDocument());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Generic CadLink: unexpected error\n\n" + ex.Message,
                    "Generic CadLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public int HookSwShutdown() { return 0; }
        public swDocumentTypes_e GetDocumentType() { return swDocumentTypes_e.swDocPART; }

        private static void ShowResult(ExportResult result)
        {
            var package = result.Package;
            var lines = new StringBuilder();
            if (!string.IsNullOrEmpty(result.JsonPath)) lines.AppendLine("JSON: " + result.JsonPath);
            if (!string.IsNullOrEmpty(result.DxfPath)) lines.AppendLine("DXF: " + result.DxfPath);
            lines.AppendLine("Part number: " + package.PartNumber);
            lines.AppendLine("Bends: " + package.Bends.Count);
            if (package.Thickness.HasValue) lines.AppendLine("Thickness: " + package.Thickness.Value + " mm");
            if (!string.IsNullOrEmpty(package.Material)) lines.AppendLine("Material: " + package.Material);
            foreach (var error in package.Errors) lines.AppendLine("[ERROR] " + error);
            foreach (var warning in package.Warnings) lines.AppendLine("[WARN] " + warning);

            var icon = package.Errors.Count > 0 ? MessageBoxIcon.Error
                : package.Warnings.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information;
            MessageBox.Show(lines.ToString(), "Generic CadLink schema v0.3", MessageBoxButtons.OK, icon);
        }
    }
}
