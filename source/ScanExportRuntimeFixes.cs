using System;
using System.Collections.Generic;
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
            bool previousLiveLogWritesEnabled = liveLogWritesEnabled;
            try
            {
                scanSettingsExportReportText = "";
                exportScanReportButton.Enabled = false;
                liveLogWritesEnabled = false;
                liveLogBuffer.Length = 0;
                await RunCheckOnlyAsync();
            }
            finally
            {
                liveLogWritesEnabled = previousLiveLogWritesEnabled;
                liveLogBuffer.Length = 0;
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

            int failLineFailures = CountImportantScanFailLines(runLogLines);
            int failures = Math.Max(Math.Max(0, lastReadinessFailures), failLineFailures);
            if (failures > lastReadinessFailures)
            {
                lastReadinessFailures = failures;
                SetStatusText("Not ready. Scan found FAIL lines: " + failures);
                Log("INFO Final readiness summary corrected from scan FAIL lines: " + failures);
                Log("RESULT NOT READY. Failed checks found in Scan Settings: " + failures);
                runLogLines = SnapshotRunLogLines();
            }

            scanSettingsExportReportText = BuildScanSettingsExportReportText(failures, runLogLines);
            lastCheckOnlyReportText = scanSettingsExportReportText;
        }

        private int CountImportantScanFailLines(string[] runLogLines)
        {
            HashSet<string> failures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in runLogLines ?? new string[0])
            {
                string trimmed = (line ?? "").Trim();
                if (!trimmed.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))
                    continue;
                failures.Add(NormalizeScanFailureKey(trimmed));
            }
            return failures.Count;
        }

        private string NormalizeScanFailureKey(string line)
        {
            string text = line ?? "";
            int pipe = text.IndexOf('|');
            if (pipe >= 0 && pipe + 1 < text.Length)
                text = text.Substring(pipe + 1);
            return CollapseScanExportWhitespace(text).Trim().TrimEnd('.');
        }

        private string CollapseScanExportWhitespace(string value)
        {
            StringBuilder sb = new StringBuilder();
            bool wasSpace = false;
            foreach (char c in value ?? "")
            {
                if (Char.IsWhiteSpace(c))
                {
                    if (!wasSpace)
                        sb.Append(' ');
                    wasSpace = true;
                }
                else
                {
                    sb.Append(c);
                    wasSpace = false;
                }
            }
            return sb.ToString().Trim();
        }

        private string BuildScanSettingsExportReportText(int readinessFailures, string[] runLogLines)
        {
            StringBuilder sb = new StringBuilder();
            HashSet<string> emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            sb.AppendLine("CK3MPS compact check");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Result: " + (readinessFailures == 0 ? "READY" : "NOT READY"));
            sb.AppendLine("Failed readiness checks: " + readinessFailures);
            sb.AppendLine();

            foreach (string line in runLogLines ?? new string[0])
            {
                string trimmed = (line ?? "").Trim();
                if (!IsImportantScanExportLine(trimmed))
                    continue;

                string normalized = NormalizeScanExportLine(trimmed, readinessFailures);
                if (String.IsNullOrWhiteSpace(normalized))
                    continue;

                string key = CollapseScanExportWhitespace(normalized);
                if (!emitted.Add(key))
                    continue;

                sb.AppendLine(normalized);
            }

            if (readinessFailures > 0)
            {
                string correctedResult = "RESULT| NOT READY. Failed checks found in Scan Settings: " + readinessFailures;
                if (emitted.Add(correctedResult))
                    sb.AppendLine(correctedResult);
            }

            return sb.ToString();
        }

        private bool IsImportantScanExportLine(string line)
        {
            if (String.IsNullOrWhiteSpace(line))
                return false;
            return line.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("WARN", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("RESULT", StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizeScanExportLine(string line, int readinessFailures)
        {
            if (line.StartsWith("RESULT", StringComparison.OrdinalIgnoreCase) && readinessFailures > 0)
                return "RESULT| NOT READY. Failed checks found in Scan Settings: " + readinessFailures;

            if (line.IndexOf("Host suitability report is up to date", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (readinessFailures == 0)
                    return "WARN  | Host suitability report can be regenerated by Apply Settings; this is advisory and not a final readiness blocker.";
                return line;
            }

            if (line.IndexOf("Critical host-save rules are confirmed safe", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "WARN  | Critical host-save rules are not confirmed safe for the current recommended save. Use Workflow > Fix save + host or choose a known clean manual local save before hosting.";
            }

            return line;
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
