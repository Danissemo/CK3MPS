using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private string scanSettingsExportReportText = "";

        private sealed class ScanConsistencyResult
        {
            public int FailureCount;
            public readonly HashSet<string> FailureKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> FailedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> CorrectedFinalLines = new List<string>();
        }

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

            ScanConsistencyResult consistency = AnalyzeScanConsistency(runLogLines);
            int failures = Math.Max(Math.Max(0, lastReadinessFailures), consistency.FailureCount);
            consistency.FailureCount = failures;

            if (failures > 0)
            {
                lastReadinessFailures = failures;
                SetStatusText("Not ready. Scan found FAIL lines: " + failures);
                runLogLines = ReplaceScanFinalSummaryLines(runLogLines, consistency);
                ReplaceRunLogLines(runLogLines);
                ReplaceVisibleFinalReadinessSummary(consistency);
            }

            scanSettingsExportReportText = BuildScanSettingsExportReportText(failures, runLogLines);
            lastCheckOnlyReportText = scanSettingsExportReportText;
        }

        private void ReplaceRunLogLines(string[] lines)
        {
            lock (runLogSync)
            {
                runLogLines.Clear();
                if (lines != null)
                    runLogLines.AddRange(lines);
            }
        }

        private ScanConsistencyResult AnalyzeScanConsistency(string[] runLogLines)
        {
            ScanConsistencyResult result = new ScanConsistencyResult();
            string currentSection = "";
            bool previousDivider = false;

            foreach (string raw in runLogLines ?? new string[0])
            {
                string line = raw ?? "";
                string trimmed = line.Trim();

                if (trimmed.IndexOf("Final readiness summary", StringComparison.OrdinalIgnoreCase) >= 0)
                    break;

                if (IsScanDividerLine(trimmed))
                {
                    previousDivider = true;
                    continue;
                }

                if (previousDivider && trimmed.Length > 0 && !IsScanStatusLine(trimmed))
                {
                    currentSection = NormalizeScanSectionName(trimmed);
                    previousDivider = false;
                    continue;
                }

                previousDivider = false;

                if (!trimmed.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))
                    continue;

                result.FailureKeys.Add(NormalizeScanFailureKey(trimmed));
                if (!String.IsNullOrWhiteSpace(currentSection))
                    result.FailedSections.Add(currentSection);
            }

            result.FailureCount = result.FailureKeys.Count;
            return result;
        }

        private string[] ReplaceScanFinalSummaryLines(string[] runLogLines, ScanConsistencyResult consistency)
        {
            List<string> output = new List<string>();
            bool inFinal = false;
            bool finalFound = false;
            bool resultWritten = false;

            foreach (string raw in runLogLines ?? new string[0])
            {
                string line = raw ?? "";
                string trimmed = line.Trim();

                if (trimmed.IndexOf("Final readiness summary", StringComparison.OrdinalIgnoreCase) >= 0 && !inFinal)
                {
                    inFinal = true;
                    finalFound = true;
                    output.Add(line);
                    continue;
                }

                if (!inFinal)
                {
                    output.Add(line);
                    continue;
                }

                if (trimmed.StartsWith("RESULT", StringComparison.OrdinalIgnoreCase))
                {
                    string resultLine = consistency.FailureCount == 0
                        ? "RESULT READY."
                        : "RESULT NOT READY. Failed checks found in Scan Settings: " + consistency.FailureCount;
                    output.Add(resultLine);
                    consistency.CorrectedFinalLines.Add(resultLine);
                    resultWritten = true;
                    inFinal = false;
                    continue;
                }

                string corrected = CorrectFinalSummaryLine(line, consistency);
                output.Add(corrected);
                consistency.CorrectedFinalLines.Add(corrected);
            }

            if (!finalFound)
            {
                output.Add("Final readiness summary");
                output.Add("INFO Final readiness summary | scan FAIL checks: " + consistency.FailureCount);
                output.Add("RESULT NOT READY. Failed checks found in Scan Settings: " + consistency.FailureCount);
                consistency.CorrectedFinalLines.Add("INFO Final readiness summary | scan FAIL checks: " + consistency.FailureCount);
                consistency.CorrectedFinalLines.Add("RESULT NOT READY. Failed checks found in Scan Settings: " + consistency.FailureCount);
            }
            else if (!resultWritten && consistency.FailureCount > 0)
            {
                output.Add("RESULT NOT READY. Failed checks found in Scan Settings: " + consistency.FailureCount);
                consistency.CorrectedFinalLines.Add("RESULT NOT READY. Failed checks found in Scan Settings: " + consistency.FailureCount);
            }

            return output.ToArray();
        }

        private string CorrectFinalSummaryLine(string line, ScanConsistencyResult consistency)
        {
            string trimmed = (line ?? "").Trim();

            if (trimmed.IndexOf("all checklist checks passed", StringComparison.OrdinalIgnoreCase) >= 0
                || trimmed.IndexOf("checklist failed checks", StringComparison.OrdinalIgnoreCase) >= 0
                || trimmed.IndexOf("scan FAIL checks", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return consistency.FailureCount == 0
                    ? "OK   Final readiness summary | all checklist checks passed"
                    : "INFO Final readiness summary | scan FAIL checks: " + consistency.FailureCount;
            }

            if (trimmed.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))
            {
                string message = NormalizeScanStatusMessage(trimmed);
                if (consistency.FailedSections.Contains(message))
                    return ReplaceScanLeadingStatus(line, "FAIL ");
            }

            return line;
        }

        private void ReplaceVisibleFinalReadinessSummary(ScanConsistencyResult consistency)
        {
            if (logBox == null || consistency == null || consistency.FailureCount <= 0)
                return;

            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate { ReplaceVisibleFinalReadinessSummary(consistency); });
                return;
            }

            try
            {
                string text = logBox.Text ?? "";
                int titleIndex = text.LastIndexOf("Final readiness summary", StringComparison.OrdinalIgnoreCase);
                if (titleIndex >= 0)
                {
                    int removeStart = text.LastIndexOf("------------------------------------------------------------", titleIndex, StringComparison.OrdinalIgnoreCase);
                    if (removeStart < 0)
                        removeStart = titleIndex;
                    int resultIndex = text.IndexOf("RESULT", titleIndex, StringComparison.OrdinalIgnoreCase);
                    int removeEnd = resultIndex >= 0 ? text.IndexOf('\n', resultIndex) : -1;
                    if (removeEnd < 0)
                        removeEnd = text.Length;
                    else
                        removeEnd++;

                    logBox.Select(removeStart, Math.Max(0, removeEnd - removeStart));
                    logBox.SelectedText = "";
                    logBox.SelectionStart = logBox.TextLength;
                }

                LogSection("Final readiness summary");
                foreach (string line in consistency.CorrectedFinalLines)
                {
                    string trimmed = (line ?? "").Trim();
                    if (trimmed.Length == 0 || trimmed.IndexOf("Final readiness summary", StringComparison.OrdinalIgnoreCase) >= 0 && !trimmed.StartsWith("INFO", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                        continue;
                    Log(line);
                }
            }
            catch
            {
            }
        }

        private int CountImportantScanFailLines(string[] runLogLines)
        {
            return AnalyzeScanConsistency(runLogLines).FailureCount;
        }

        private string NormalizeScanFailureKey(string line)
        {
            string text = line ?? "";
            int pipe = text.IndexOf('|');
            if (pipe >= 0 && pipe + 1 < text.Length)
                text = text.Substring(pipe + 1);
            return CollapseScanExportWhitespace(text).Trim().TrimEnd('.');
        }

        private string NormalizeScanStatusMessage(string line)
        {
            string text = line ?? "";
            int pipe = text.IndexOf('|');
            if (pipe >= 0 && pipe + 1 < text.Length)
                text = text.Substring(pipe + 1);
            else if (text.Length > 5 && (text.StartsWith("OK", StringComparison.OrdinalIgnoreCase) || text.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase) || text.StartsWith("WARN", StringComparison.OrdinalIgnoreCase) || text.StartsWith("INFO", StringComparison.OrdinalIgnoreCase)))
                text = text.Substring(5);
            return NormalizeScanSectionName(text);
        }

        private string NormalizeScanSectionName(string value)
        {
            return CollapseScanExportWhitespace(value ?? "").Trim().TrimEnd('.');
        }

        private string ReplaceScanLeadingStatus(string line, string replacementStatus)
        {
            string text = line ?? "";
            int okIndex = text.IndexOf("OK", StringComparison.OrdinalIgnoreCase);
            int failIndex = text.IndexOf("FAIL", StringComparison.OrdinalIgnoreCase);
            int index = okIndex >= 0 ? okIndex : failIndex;
            int length = okIndex >= 0 ? 2 : 4;
            if (index < 0)
                return text;
            return text.Substring(0, index) + replacementStatus + text.Substring(index + length);
        }

        private bool IsScanStatusLine(string line)
        {
            string text = (line ?? "").TrimStart();
            return text.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("WARN", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("INFO", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("RESULT", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("CMD", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsScanDividerLine(string line)
        {
            string text = (line ?? "").Trim();
            if (text.Length < 8)
                return false;
            foreach (char c in text)
                if (c != '-')
                    return false;
            return true;
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
                if (emitted.Add(CollapseScanExportWhitespace(correctedResult)))
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
                return "FAIL  | Critical host-save rules are not confirmed safe for the current recommended save. Use Workflow > Fix save + host or choose a known clean manual local save before hosting.";
            }

            if (line.IndexOf("Runtime verification report is up to date", StringComparison.OrdinalIgnoreCase) >= 0)
                return "FAIL  | Runtime verification report is missing or outdated";
            if (line.IndexOf("OOS evidence pack outputs are up to date", StringComparison.OrdinalIgnoreCase) >= 0)
                return "FAIL  | OOS evidence pack outputs are missing or outdated";
            if (line.IndexOf("Host suitability report is up to date", StringComparison.OrdinalIgnoreCase) >= 0)
                return "FAIL  | Host suitability report is missing or outdated";

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