using System;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Shown += delegate { ConfigureScanExportRuntimeFix(); };
        }

        private void ConfigureScanExportRuntimeFix()
        {
            checkButton.Text = "Scan Settings";
            exportScanReportButton.Text = "Scan Export";
            ReplaceClickHandlers(checkButton, delegate { RunCheckOnlyAndAlwaysUnlockExport(); });
            if (!String.IsNullOrWhiteSpace(lastCheckOnlyReportText))
                exportScanReportButton.Enabled = !busyUi;
        }

        private async void RunCheckOnlyAndAlwaysUnlockExport()
        {
            try
            {
                await RunCheckOnlyAsync();
            }
            finally
            {
                EnsureScanExportReportAvailable();
                exportScanReportButton.Enabled = !busyUi && !String.IsNullOrWhiteSpace(lastCheckOnlyReportText);
                if (exportScanReportButton.Enabled)
                    SetStatusText("Scan complete. Scan Export is now available.");
            }
        }

        private void EnsureScanExportReportAvailable()
        {
            if (!String.IsNullOrWhiteSpace(lastCheckOnlyReportText))
                return;

            string[] runLogLines = SnapshotRunLogLines();
            if (runLogLines == null || runLogLines.Length == 0)
                runLogLines = new[] { "ERROR Scan did not produce a log snapshot." };

            int failures = Math.Max(0, lastReadinessFailures);
            lastCheckOnlyReportText = BuildCheckOnlyReportText(failures, runLogLines);
        }
    }
}
