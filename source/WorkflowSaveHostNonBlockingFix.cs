using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private const int WorkflowLargeSaveRuleScanBytes = 16 * 1024 * 1024;
        private const int WorkflowLargeSaveCopyBufferBytes = 1024 * 1024;

        private sealed class WorkflowLargeSaveRewriteResult
        {
            public bool Success;
            public string FailureReason = "";
            public readonly List<string> AppliedRules = new List<string>();
        }

        private sealed class WorkflowLargeSaveApplyResult
        {
            public bool Success;
            public string Reason = "";
        }

        private void ConfigureNonBlockingSaveHostFixHandler()
        {
            if (workflowApplySafeStartButton == null || workflowSaveHostFixInProgress)
                return;

            workflowApplySafeStartButton.Text = "Fix save + host";
            workflowApplySafeStartButton.Enabled = !IsGameRunning();
            ReplaceClickHandlers(workflowApplySafeStartButton, delegate { RunWorkflowSaveAndHostFixNonBlocking(); });
        }

        private async void RunWorkflowSaveAndHostFixNonBlocking()
        {
            if (workflowSaveHostFixInProgress)
            {
                MessageBox.Show("Fix save + host is already running.", "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (IsGameRunning())
            {
                MessageBox.Show("Close CK3 and Paradox Launcher before running Fix save + host.", "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!ValidateBeforeRun())
                return;

            string runId = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            WorkflowScenarioSnapshot beforeSnapshot = BuildWorkflowScenarioSnapshotCore(currentWorkflowScenario, CancellationToken.None);
            HostSuitabilityResult beforeHost = AnalyzeHostSuitability();
            HostSaveCandidateResult beforeSave = AnalyzeWorkflowHostSaveCandidate();
            List<string> actions = new List<string>();
            List<string> warnings = new List<string>();
            List<string> remainingBlockers = new List<string>();
            List<string> manualBlockers = new List<string>();
            string outcome = "Failed";

            DialogResult confirmation = MessageBox.Show(
                BuildWorkflowFixPreflightText(runId, beforeSnapshot, beforeHost, beforeSave),
                "Fix save + host",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (confirmation != DialogResult.OK)
            {
                Log("RESULT| Workflow Fix save + host | outcome=Cancelled | run=" + runId + " | host=" + ScoreText(beforeHost) + "->" + ScoreText(beforeHost) + " | save=" + ScoreText(beforeSave) + "->" + ScoreText(beforeSave) + " | fix=0 | manual=0 | warnings=0");
                PublishWorkflowPostFixSnapshot(beforeSnapshot, "Fix save + host cancelled before mutation.\r\n\r\n" + beforeSnapshot.Summary);
                return;
            }

            try
            {
                workflowSaveHostFixInProgress = true;
                CancelWorkflowScenarioRefresh();
                ClearWorkflowScenarioSnapshots();
                workflowApplySafeStartButton.Enabled = false;
                workflowApplySafeStartButton.Text = "Running...";
                workflowProgressBar.Style = ProgressBarStyle.Blocks;
                workflowProgressBar.Minimum = 0;
                workflowProgressBar.Maximum = 5;
                workflowProgressBar.Value = 0;
                workflowProgressBar.Visible = true;
                UseWaitCursor = true;
                Log("INFO Workflow Fix save + host PREFLIGHT run=" + runId);

                EnsureStabilizerRoot();
                if (String.IsNullOrEmpty(GetKnownQuarantine()))
                    CreateQuarantine();
                SetWorkflowFixProgress(1, "snapshot captured");

                bool saveRepairFailed = false;
                string saveRepairFailure = "";
                try
                {
                    HostSaveCandidateResult saveBeforeRepair = AnalyzeWorkflowHostSaveCandidate();
                    if (WorkflowSaveIsReady(saveBeforeRepair))
                    {
                        actions.Add("selected save was already verified ready");
                    }
                    else
                    {
                        SetWorkflowFixProgress(1, "repairing selected save rules");
                        IProgress<string> progress = new Progress<string>(delegate (string message)
                        {
                            if (String.IsNullOrWhiteSpace(message) || workflowVerdictLabel == null)
                                return;
                            workflowVerdictLabel.Text = "Status: Fix save + host: " + message;
                            ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
                        });

                        WorkflowLargeSaveApplyResult repairResult = await TryForceWorkflowHostSaveIntoLargeSafeBaselineNonBlockingAsync(progress);
                        if (repairResult != null && repairResult.Success)
                            actions.Add("save critical rules repaired into verified safe copy: " + repairResult.Reason);
                        else
                        {
                            saveRepairFailed = true;
                            string repairReason = repairResult == null ? "" : repairResult.Reason;
                            saveRepairFailure = String.IsNullOrWhiteSpace(repairReason) ? "selected save could not be repaired automatically" : repairReason;
                            warnings.Add("save repair: " + saveRepairFailure);
                        }
                    }
                }
                catch (Exception ex)
                {
                    saveRepairFailed = true;
                    saveRepairFailure = ex.Message;
                    warnings.Add("save repair: " + ex.Message);
                    Log("WARN Workflow Fix save + host SAVE_FIX run=" + runId + " | " + ex.Message);
                }
                SetWorkflowFixProgress(2, "save phase completed");

                try
                {
                    ApplyWorkflowHostReadinessFixes();
                    actions.Add("host profile/system readiness fixes applied and will be verified by fresh analysis");
                }
                catch (Exception ex)
                {
                    warnings.Add("host repair: " + ex.Message);
                    Log("WARN Workflow Fix save + host HOST_FIX run=" + runId + " | " + ex.Message);
                }
                SetWorkflowFixProgress(3, "host phase completed");

                WriteHostSavePreparationReport();
                WriteMultiplayerParityManifest();
                WriteHostSuitabilityReport();
                WriteRuntimeVerificationReport();
                InvalidateHostSuitabilityCache();
                InvalidateHostSaveAnalysisCache();
                ClearWorkflowScenarioSnapshots();
                SetWorkflowFixProgress(4, "reports refreshed");

                WorkflowScenarioSnapshot afterSnapshot = BuildWorkflowScenarioSnapshotCore(currentWorkflowScenario, CancellationToken.None);
                HostSuitabilityResult afterHost = AnalyzeHostSuitability();
                HostSaveCandidateResult afterSave = AnalyzeWorkflowHostSaveCandidate();
                remainingBlockers = CollectRemainingWorkflowAutoBlockers(afterSnapshot);
                bool ready = !saveRepairFailed && remainingBlockers.Count == 0 && warnings.Count == 0;
                if (ready)
                    outcome = actions.Count == 0 ? "AlreadyReady" : "Ready";
                else if (actions.Count > 0)
                    outcome = "PartiallyFixed";
                else
                    outcome = "Blocked";

                string report = BuildWorkflowFixResultText(runId, outcome, beforeSnapshot, afterSnapshot, beforeHost, afterHost, beforeSave, afterSave, actions, remainingBlockers, manualBlockers, warnings);
                PublishWorkflowPostFixSnapshot(afterSnapshot, report + "\r\n" + afterSnapshot.Summary);
                WriteWorkflowStatusReport();
                SetWorkflowFixProgress(5, "post-check published");

                Log("RESULT| Workflow Fix save + host | outcome=" + outcome + " | run=" + runId + " | host=" + ScoreText(beforeHost) + "->" + ScoreText(afterHost) + " | save=" + ScoreText(beforeSave) + "->" + ScoreText(afterSave) + " | fix=" + actions.Count + " | manual=0 | warnings=" + warnings.Count);
                SetStatusText("Fix save + host " + outcome + ". Host " + ScoreText(beforeHost) + " -> " + ScoreText(afterHost) + ", save " + ScoreText(beforeSave) + " -> " + ScoreText(afterSave) + ".");

                if (!ready || warnings.Count > 0)
                    MessageBox.Show(report, "CK3MPS workflow", MessageBoxButtons.OK, ready ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                else
                    MessageBox.Show("Fix save + host verified. Workflow is ready.", "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                outcome = "Failed";
                Log("RESULT| Workflow Fix save + host | outcome=Failed | run=" + runId + " | host=" + ScoreText(beforeHost) + "->n/a | save=" + ScoreText(beforeSave) + "->n/a | fix=" + actions.Count + " | manual=0 | warnings=" + (warnings.Count + 1));
                SetStatusText("Fix save + host failed: " + ex.Message);
                MessageBox.Show("Fix save + host failed.\r\n\r\n" + ex.Message, "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
                workflowProgressBar.Visible = false;
                workflowSaveHostFixInProgress = false;
                workflowApplySafeStartButton.Text = "Fix save + host";
                ConfigureNonBlockingSaveHostFixHandler();
                UpdateWorkflowActionAvailability();
            }
        }

        private async Task<WorkflowLargeSaveApplyResult> TryForceWorkflowHostSaveIntoLargeSafeBaselineNonBlockingAsync(IProgress<string> progress)
        {
            WorkflowLargeSaveApplyResult applyResult = new WorkflowLargeSaveApplyResult();
            HostSaveCandidateResult current = AnalyzeWorkflowHostSaveCandidate();
            if (current == null || current.Save == null || String.IsNullOrWhiteSpace(current.Save.Path) || !File.Exists(current.Save.Path))
            {
                applyResult.Reason = "selected save is missing";
                return applyResult;
            }
            if (!current.Save.Readable)
            {
                applyResult.Reason = "selected save is not safely readable";
                return applyResult;
            }
            if (!current.Save.VersionMatchesInstalled)
            {
                applyResult.Reason = "selected save version does not match installed CK3 version";
                return applyResult;
            }

            string sourcePath = current.Save.Path;
            string originalSelectedPath = workflowSelectedSavePath;
            string repairedPath = BuildAvailableWorkflowSafeSavePath(sourcePath);
            string dir = Path.GetDirectoryName(repairedPath) ?? Path.GetDirectoryName(sourcePath) ?? "";
            string tempPath = Path.Combine(dir, Path.GetFileName(repairedPath) + ".streaming-" + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                Directory.CreateDirectory(dir);
                WorkflowLargeSaveRewriteResult rewrite = await Task.Run(delegate
                {
                    return RewriteWorkflowLargeSaveStreaming(sourcePath, tempPath, progress);
                });

                if (rewrite == null || !rewrite.Success)
                {
                    applyResult.Reason = rewrite == null || String.IsNullOrWhiteSpace(rewrite.FailureReason)
                        ? "selected save could not be rewritten safely"
                        : rewrite.FailureReason;
                    return applyResult;
                }

                RecordCreatedFileForRestore(repairedPath, "CK3MPS streamed verified repaired host save copy: " + repairedPath);
                SafeAtomicFile.ReplaceFile(tempPath, repairedPath);
                workflowSelectedSavePath = repairedPath;
                SaveAppConfig();
                InvalidateHostSaveAnalysisCache();
                RefreshWorkflowSaveSelectionList();

                HostSaveCandidateResult repaired = AnalyzeWorkflowHostSaveCandidate();
                if (!WorkflowSaveIsReady(repaired))
                {
                    workflowSelectedSavePath = originalSelectedPath;
                    SaveAppConfig();
                    InvalidateHostSaveAnalysisCache();
                    RefreshWorkflowSaveSelectionList();
                    SafeAtomicFile.TryDeleteTempFile(repairedPath);
                    applyResult.Reason = "streamed repaired save was created but post-check still scores " + (repaired == null ? 0 : repaired.Score) + "/100; remaining rules: " + WorkflowSaveScoreRuleDiagnostics(repaired);
                    return applyResult;
                }

                applyResult.Success = true;
                applyResult.Reason = rewrite.AppliedRules == null || rewrite.AppliedRules.Count == 0
                    ? "safe game-rule profile normalized for score checks"
                    : "safe game-rule profile normalized for score checks: " + String.Join(", ", rewrite.AppliedRules.ToArray());
                return applyResult;
            }
            catch (Exception ex)
            {
                workflowSelectedSavePath = originalSelectedPath;
                SaveAppConfig();
                InvalidateHostSaveAnalysisCache();
                RefreshWorkflowSaveSelectionList();
                applyResult.Reason = ex.Message;
                return applyResult;
            }
            finally
            {
                SafeAtomicFile.TryDeleteTempFile(tempPath);
            }
        }

        private string BuildAvailableWorkflowSafeSavePath(string sourcePath)
        {
            string basePath = BuildVerifiedWorkflowSafeSavePath(sourcePath);
            if (!String.Equals(basePath, sourcePath, StringComparison.OrdinalIgnoreCase) && !File.Exists(basePath))
                return basePath;

            string dir = Path.GetDirectoryName(basePath) ?? Path.GetDirectoryName(sourcePath) ?? "";
            string name = Path.GetFileNameWithoutExtension(basePath) ?? "CK3MPS_host_save_ck3mps_safe";
            string ext = Path.GetExtension(basePath);
            for (int i = 2; i <= 999; i++)
            {
                string candidate = Path.Combine(dir, name + "_" + i + ext);
                if (!String.Equals(candidate, sourcePath, StringComparison.OrdinalIgnoreCase) && !File.Exists(candidate))
                    return candidate;
            }
            throw new IOException("Could not allocate a unique repaired save path.");
        }

        private WorkflowLargeSaveRewriteResult RewriteWorkflowLargeSaveStreaming(string sourcePath, string tempPath, IProgress<string> progress)
        {
            long zipOffset;
            bool startsWithZip;
            string failureReason;
            if (!TryDetectLargeScoreZipLayout(sourcePath, out startsWithZip, out zipOffset, out failureReason))
                return new WorkflowLargeSaveRewriteResult { FailureReason = failureReason };

            if (startsWithZip || zipOffset > 0)
                return RewriteWorkflowLargeZipStreaming(sourcePath, startsWithZip ? 0 : zipOffset, tempPath, progress);
            return RewriteWorkflowLargePlainTextStreaming(sourcePath, tempPath, progress);
        }

        private WorkflowLargeSaveRewriteResult RewriteWorkflowLargePlainTextStreaming(string sourcePath, string tempPath, IProgress<string> progress)
        {
            WorkflowLargeSaveRewriteResult result = new WorkflowLargeSaveRewriteResult();
            try
            {
                FileInfo info = new FileInfo(sourcePath);
                if (info.Length <= 0 || info.Length > WorkflowLargeSaveRepairFileBytes)
                {
                    result.FailureReason = "save exceeds the large safe repair size limit";
                    return result;
                }

                if (progress != null)
                    progress.Report("streaming plain-text save repair");
                using (FileStream input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    bool changed = RewriteWorkflowTextStreamPrefix(input, output, result.AppliedRules);
                    if (!changed)
                    {
                        result.FailureReason = "save text does not expose repairable score game_rules in the analysis window";
                        return result;
                    }
                    output.Flush(true);
                }
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.FailureReason = "plain-text save could not be rewritten safely: " + ex.Message;
                return result;
            }
        }

        private WorkflowLargeSaveRewriteResult RewriteWorkflowLargeZipStreaming(string sourcePath, long zipOffset, string tempPath, IProgress<string> progress)
        {
            WorkflowLargeSaveRewriteResult result = new WorkflowLargeSaveRewriteResult();
            string sourceZipPath = sourcePath;
            string isolatedZipPath = tempPath + ".source.zip";
            string repairedZipPath = tempPath + ".payload.zip";
            try
            {
                FileInfo info = new FileInfo(sourcePath);
                if (info.Length <= 0 || info.Length > WorkflowLargeSaveRepairFileBytes)
                {
                    result.FailureReason = "save exceeds the large safe repair size limit";
                    return result;
                }
                if (zipOffset < 0 || zipOffset >= info.Length)
                {
                    result.FailureReason = "embedded zip offset is invalid";
                    return result;
                }

                if (zipOffset > 0)
                {
                    if (progress != null)
                        progress.Report("isolating embedded CK3 zip payload");
                    using (FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (FileStream isolated = new FileStream(isolatedZipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        source.Position = zipOffset;
                        CopyWorkflowStreamBounded(source, isolated, info.Length - zipOffset);
                        isolated.Flush(true);
                    }
                    sourceZipPath = isolatedZipPath;
                }

                bool changed = false;
                HashSet<string> normalizedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long totalUncompressedBytes = 0;
                using (FileStream sourceZipStream = new FileStream(sourceZipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (ZipArchive sourceArchive = new ZipArchive(sourceZipStream, ZipArchiveMode.Read, false))
                using (FileStream repairedZipStream = new FileStream(repairedZipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (ZipArchive destinationArchive = new ZipArchive(repairedZipStream, ZipArchiveMode.Create, true))
                {
                    if (sourceArchive.Entries.Count > WorkflowLargeSaveRepairZipEntryCount)
                    {
                        result.FailureReason = "save zip contains too many entries";
                        return result;
                    }

                    foreach (ZipArchiveEntry sourceEntry in sourceArchive.Entries)
                    {
                        string normalizedName;
                        string validationFailure;
                        if (!TryValidateLargeScoreRepairZipEntry(sourceEntry, normalizedEntryNames, ref totalUncompressedBytes, out normalizedName, out validationFailure))
                        {
                            result.FailureReason = validationFailure;
                            return result;
                        }

                        if (progress != null)
                            progress.Report("streaming save entry " + normalizedName);
                        ZipArchiveEntry destinationEntry = destinationArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Fastest);
                        destinationEntry.LastWriteTime = sourceEntry.LastWriteTime;
                        using (Stream sourceStream = sourceEntry.Open())
                        using (Stream destinationStream = destinationEntry.Open())
                        {
                            if (String.Equals(normalizedName, "meta", StringComparison.OrdinalIgnoreCase)
                                || String.Equals(normalizedName, "gamestate", StringComparison.OrdinalIgnoreCase))
                            {
                                if (RewriteWorkflowTextStreamPrefix(sourceStream, destinationStream, result.AppliedRules))
                                    changed = true;
                            }
                            else
                            {
                                CopyWorkflowStreamBounded(sourceStream, destinationStream, sourceEntry.Length);
                            }
                        }
                    }
                }

                if (!changed)
                {
                    result.FailureReason = "save zip meta/gamestate entries did not expose repairable score game_rules in the analysis window";
                    return result;
                }

                if (progress != null)
                    progress.Report("publishing repaired save copy");
                using (FileStream output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (zipOffset > 0)
                    {
                        using (FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            CopyWorkflowStreamBounded(source, output, zipOffset);
                    }
                    using (FileStream repaired = new FileStream(repairedZipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        CopyWorkflowStreamBounded(repaired, output, repaired.Length);
                    output.Flush(true);
                }

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.FailureReason = "save zip could not be rewritten safely: " + ex.Message;
                return result;
            }
            finally
            {
                SafeAtomicFile.TryDeleteTempFile(isolatedZipPath);
                SafeAtomicFile.TryDeleteTempFile(repairedZipPath);
            }
        }

        private bool RewriteWorkflowTextStreamPrefix(Stream input, Stream output, List<string> appliedRules)
        {
            byte[] prefixBuffer = new byte[WorkflowLargeSaveRuleScanBytes];
            int totalRead = 0;
            while (totalRead < prefixBuffer.Length)
            {
                int read = input.Read(prefixBuffer, totalRead, prefixBuffer.Length - totalRead);
                if (read <= 0)
                    break;
                totalRead += read;
            }

            int transformLength = totalRead;
            if (totalRead == prefixBuffer.Length)
            {
                int lastNewLine = Array.LastIndexOf(prefixBuffer, (byte)'\n', totalRead - 1, totalRead);
                if (lastNewLine > 0)
                    transformLength = lastNewLine + 1;
            }

            string originalPrefix = Encoding.UTF8.GetString(prefixBuffer, 0, transformLength);
            List<string> entryRules;
            string updatedPrefix = ApplyScoreDrivenCriticalRuleRepairsToText(originalPrefix, out entryRules);
            bool changed = entryRules != null
                && entryRules.Count > 0
                && !String.Equals(originalPrefix, updatedPrefix, StringComparison.Ordinal);

            if (changed)
            {
                MergeAppliedRules(appliedRules, entryRules);
                byte[] updatedBytes = Encoding.UTF8.GetBytes(updatedPrefix);
                output.Write(updatedBytes, 0, updatedBytes.Length);
            }
            else if (transformLength > 0)
            {
                output.Write(prefixBuffer, 0, transformLength);
            }

            if (totalRead > transformLength)
                output.Write(prefixBuffer, transformLength, totalRead - transformLength);
            CopyWorkflowStreamToEnd(input, output);
            return changed;
        }

        private string ApplyScoreDrivenCriticalRuleRepairsToText(string text, out List<string> appliedRules)
        {
            string repaired = ApplyBroadCriticalRuleRepairsToText(text, out appliedRules);
            if (String.IsNullOrEmpty(repaired))
                return repaired;

            string normalized = NormalizeGameRuleSettingsForScore(repaired, appliedRules);
            return normalized;
        }

        private string NormalizeGameRuleSettingsForScore(string text, List<string> appliedRules)
        {
            string body;
            int settingsOpen;
            int settingsClose;
            if (!TryLocateGameRuleSettingsBody(text, out body, out settingsOpen, out settingsClose))
                return InjectSafeGameRuleSettingsBlock(text, out appliedRules);

            List<string> preserved = new List<string>();
            MatchCollection tokens = Regex.Matches(body ?? "", "\"[^\"]+\"|\\S+");
            foreach (Match tokenMatch in tokens)
            {
                string token = (tokenMatch.Value ?? "").Trim().Trim('"');
                if (String.IsNullOrWhiteSpace(token))
                    continue;
                if (IsManagedCriticalRuleSettingsToken(token))
                    continue;
                AddUniqueToken(preserved, token);
            }

            bool changed = false;
            foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
            {
                if (String.IsNullOrWhiteSpace(definition.SafeToken))
                    continue;
                if (!ContainsToken(preserved, definition.SafeToken))
                {
                    preserved.Add(definition.SafeToken);
                    changed = true;
                }
                AddAppliedRuleName(appliedRules, definition.DisplayName);
            }

            string replacement = " " + String.Join(" ", preserved.ToArray()) + " ";
            string updated = text.Substring(0, settingsOpen + 1) + replacement + text.Substring(settingsClose);
            if (!changed && String.Equals(updated, text, StringComparison.Ordinal))
                return text;
            return updated;
        }

        private bool IsManagedCriticalRuleSettingsToken(string token)
        {
            string normalized = (token ?? "").Trim().Trim('"');
            if (String.IsNullOrWhiteSpace(normalized))
                return false;
            foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
            {
                if (String.Equals(normalized, definition.SafeToken, StringComparison.OrdinalIgnoreCase))
                    return true;
                foreach (string option in definition.SettingsTokens ?? new string[0])
                    if (String.Equals(normalized, option, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
        }

        private bool ContainsToken(List<string> tokens, string value)
        {
            foreach (string token in tokens ?? new List<string>())
                if (String.Equals(token, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private void AddUniqueToken(List<string> tokens, string value)
        {
            if (tokens == null || String.IsNullOrWhiteSpace(value) || ContainsToken(tokens, value))
                return;
            tokens.Add(value);
        }

        private string WorkflowSaveScoreRuleDiagnostics(HostSaveCandidateResult result)
        {
            if (result == null || result.Save == null || result.Save.Rules == null)
                return "no post-check rule data";
            List<string> unsafeRules = new List<string>();
            foreach (SaveRuleCheckResult rule in result.Save.Rules)
            {
                if (rule == null)
                    continue;
                if (!rule.Found)
                    unsafeRules.Add(rule.DisplayName + " missing");
                else if (!rule.Safe)
                    unsafeRules.Add(rule.DisplayName + "=" + NullText(rule.Actual));
            }
            return unsafeRules.Count == 0 ? "no unsafe rules reported" : String.Join("; ", unsafeRules.ToArray());
        }

        private void CopyWorkflowStreamBounded(Stream input, Stream output, long byteCount)
        {
            if (byteCount < 0 || byteCount > WorkflowLargeSaveRepairTotalUncompressedBytes + WorkflowLargeSaveRepairFileBytes)
                throw new InvalidOperationException("stream copy exceeds the large save repair limit");

            byte[] buffer = new byte[WorkflowLargeSaveCopyBufferBytes];
            long remaining = byteCount;
            while (remaining > 0)
            {
                int requested = (int)Math.Min(buffer.Length, remaining);
                int read = input.Read(buffer, 0, requested);
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of stream while rewriting save.");
                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private void CopyWorkflowStreamToEnd(Stream input, Stream output)
        {
            byte[] buffer = new byte[WorkflowLargeSaveCopyBufferBytes];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                output.Write(buffer, 0, read);
        }
    }
}
