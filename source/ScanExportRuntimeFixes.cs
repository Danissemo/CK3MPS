using System;
using System.Text;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private string scanSettingsExportReportText = "";

        private void ConfigureScanExportRuntimeFix()
        {
            checkButton.Text = "Scan Settings";
            exportScanReportButton.Text = "Scan Export";
            ReplaceClickHandlers(checkButton, delegate { RunScanSettingsAndCaptureExport(); });
            ReplaceClickHandlers(exportScanReportButton, delegate { ExportCapturedScanSettingsReport(); });
            exportScanReportButton.Enabled = !busyUi && !String.IsNullOrWhiteSpace(scanSettingsExportReportText);
        }

        private async void RunScanSettingsAndCaptureExport()
        {
            try
            {
                scanSettingsExportReportText = "";
                exportScanReportButton.Enabled = false;
                await RunCheckOnlyAsync();
            }
            finally
            {
                CaptureScanSettingsExportReport();
                exportScanReportButton.Enabled = !busyUi && !String.IsNullOrWhiteSpace(scanSettingsExportReportText);
                if (exportScanReportButton.Enabled)
                    SetStatusText("Scan complete. Scan Export is now available.");
            }
        }

        private void CaptureScanSettingsExportReport()
        {
            string[] runLogLines = SnapshotRunLogLines();
            if (runLogLines == null || runLogLines.Length == 0)
                runLogLines = new[] { "ERROR Scan did not produce a log snapshot." };

            int failures = Math.Max(0, lastReadinessFailures);
            scanSettingsExportReportText = BuildCheckOnlyReportText(failures, runLogLines);
            lastCheckOnlyReportText = scanSettingsExportReportText;
        }

        private void ExportCapturedScanSettingsReport()
        {
            if (String.IsNullOrWhiteSpace(scanSettingsExportReportText))
            {
                MessageBox.Show("Run Scan Settings before exporting its report.", "Scan Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Scan Export";
                dialog.Filter = "Text report (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.FileName = "CK3MPS_scan_settings_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                SafeAtomicFile.WriteAllText(dialog.FileName, scanSettingsExportReportText, Encoding.UTF8);
                SetStatusText("Scan report exported: " + dialog.FileName);
            }
        }
    }
}
