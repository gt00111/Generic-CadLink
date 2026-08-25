using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace GenericCadLink.Macro
{
    internal static class ExporterHost
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                var active = Marshal.GetActiveObject("SldWorks.Application");
                var app = active as SldWorks;
                if (app == null) throw new InvalidOperationException("Could not connect to the active SolidWorks session.");
                var result = new BendExporter(app).ExportActiveDocument();
                ShowResult(result);
                return result.Package.Errors.Count == 0 ? 0 : 2;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Generic CadLink v0.3 failed.\n\n" + ex.Message,
                    "Generic CadLink schema v0.3", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void ShowResult(ExportResult result)
        {
            var package = result.Package;
            var text = new StringBuilder();
            if (!string.IsNullOrEmpty(result.JsonPath)) text.AppendLine("JSON: " + result.JsonPath);
            if (!string.IsNullOrEmpty(result.DxfPath)) text.AppendLine("DXF: " + result.DxfPath);
            text.AppendLine("Part number: " + package.PartNumber);
            text.AppendLine("Bends: " + package.Bends.Count);
            foreach (var error in package.Errors) text.AppendLine("[ERROR] " + error);
            foreach (var warning in package.Warnings) text.AppendLine("[WARN] " + warning);
            var icon = package.Errors.Count > 0 ? MessageBoxIcon.Error
                : package.Warnings.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information;
            MessageBox.Show(text.ToString(), "Generic CadLink schema v0.3", MessageBoxButtons.OK, icon);
        }
    }
}
