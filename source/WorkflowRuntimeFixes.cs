using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private bool workflowRuntimeFixesConfigured;
        private bool workflowSaveHostFixInProgress;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ConfigureWorkflowRuntimeFixes();
        }

        private void ConfigureWorkflowRuntimeFixes()
        {
            if (workflowRuntimeFixesConfigured)
                return;

            workflowRuntimeFixesConfigured = true;
            ConfigureMainButtonLabels();
            ConfigureChecklistLabels();
            ConfigureCombinedWorkflowFixButton();
            ConfigureWorkflowParityControls();
            UpdateWorkflowRuntimeFixLayout();

            workflowModeBox.SelectedIndexChanged += delegate
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    ConfigureCombinedWorkflowFixButton();
                    ConfigureWorkflowParityControls();
                    UpdateWorkflowRuntimeFixLayout();
                });
            };
        }

        private void ConfigureMainButtonLabels()
        {
            checkButton.Text = "Scan Settings";
            checkButton.Size = new Size(130, checkButton.Height);
            exportScanReportButton.Text = "Scan Export";
            exportScanReportButton.Size = new Size(130, exportScanReportButton.Height);
            previewButton.Text = "Review Settings";
            previewButton.Size = new Size(130, previewButton.Height);
            ReplaceClickHandlers(checkButton, delegate { RunCheckOnlyAndUnlockExport(); });
            if (!String.IsNullOrWhiteSpace(lastCheckOnlyReportText))
                exportScanReportButton.Enabled = true;
            LayoutMainTabControls();
        }

        private void ConfigureChecklistLabels()
        {
            foreach (StepGroupUi group in stepGroups)
            {
                if (String.Equals(group.Title, "Safety Options", StringComparison.OrdinalIgnoreCase))
                {
                    group.Title = "Backup";
                    group.TitleLabel.Text = "Backup";
                }
            }
        }

        private async void RunCheckOnlyAndUnlockExport()
        {
            await RunCheckOnlyAsync();
            if (!String.IsNullOrWhiteSpace(lastCheckOnlyReportText))
                exportScanReportButton.Enabled = true;
        }

        private void ConfigureCombinedWorkflowFixButton()
        {
            if (workflowApplySafeStartButton == null)
                return;

            workflowApplySafeStartButton.Text = "Fix save + host";
            workflowApplySafeStartButton.Size = new Size(132, workflowApplySafeStartButton.Height);
            workflowApplySafeStartButton.Enabled = !workflowSaveHostFixInProgress && !IsGameRunning();
            if (workflowRepairSaveButton != null)
            {
                workflowRepairSaveButton.Visible = false;
                workflowRepairSaveButton.Enabled = false;
            }
            ReplaceClickHandlers(workflowApplySafeStartButton, delegate { RunWorkflowSaveAndHostFix(); });
            stepToolTip.SetToolTip(workflowApplySafeStartButton, BuildWorkflowFixSaveAndHostHintText());
        }

        private void ConfigureWorkflowParityControls()
        {
            workflowParityRoomButton.Text = "Parity room";
            ReplaceClickHandlers(workflowParityRoomButton, delegate { OpenParityRoomWithOnlineDashboardText(); });
            stepToolTip.SetToolTip(workflowParityRoomButton, "Online-capable parity room dashboard. Host creates a TCP room and shares host/IP, port, code and secret with other players.");

            workflowCompareParityButton.Visible = false;
            workflowCompareParityButton.Enabled = false;
            AddCompareParityToMoreMenu();
        }

        private void AddCompareParityToMoreMenu()
        {
            foreach (ToolStripItem item in workflowMoreMenu.Items)
            {
                if (String.Equals(item.Text, "Compare parity", StringComparison.OrdinalIgnoreCase))
                    return;
            }

            workflowMoreMenu.Items.Insert(0, new ToolStripMenuItem("Compare parity", null, delegate { CompareWorkflowParity(); }));
        }

        private void UpdateWorkflowRuntimeFixLayout()
        {
            const int actionGap = 8;
            workflowParityRoomButton.Location = new Point(workflowApplySafeStartButton.Right + actionGap, workflowParityRoomButton.Top);
            workflowMoreButton.Location = new Point(workflowParityRoomButton.Right + actionGap, workflowMoreButton.Top);
            workflowSaveBox.Width = Math.Max(160, workflowHeaderPanel.ClientSize.Width - workflowSaveBox.Left - workflowSaveBrowseButton.Width - 28);
            workflowSaveBrowseButton.Location = new Point(workflowSaveBox.Right + 10, workflowSaveBrowseButton.Top);
        }

        private void ReplaceClickHandlers(Button button, EventHandler replacement)
        {
            if (button == null || replacement == null)
                return;

            try
            {
                PropertyInfo eventsProperty = typeof(Component).GetProperty("Events", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo clickField = typeof(Control).GetField("EventClick", BindingFlags.Static | BindingFlags.NonPublic);
                if (eventsProperty != null && clickField != null)
                {
                    EventHandlerList events = eventsProperty.GetValue(button, null) as EventHandlerList;
                    object clickKey = clickField.GetValue(null);
                    Delegate existing = events == null || clickKey == null ? null : events[clickKey];
                    if (existing != null)
                    {
                        foreach (Delegate handler in existing.GetInvocationList())
                            button.Click -= (EventHandler)handler;
                    }
                }
            }
            catch
            {
            }

            button.Click += replacement;
        }

        private string BuildWorkflowFixSaveAndHostHintText()
        {
            return "Runs one verified workflow: selected save repair/copy first, then host profile fixes, reports, parity manifest and a fresh post-check snapshot. Close CK3 before using it.";
        }

        private void RunWorkflowSaveAndHostFix()
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

                try
                {
                    bool repaired = EnsureSafeWorkflowHostSave();
                    InvalidateHostSaveAnalysisCache();
                    HostSaveCandidateResult afterBasicRepair = AnalyzeWorkflowHostSaveCandidate();
                    if (!WorkflowSaveIsReady(afterBasicRepair))
                    {
                        string enhancedReason;
                        bool enhanced = TryForceWorkflowHostSaveIntoSafeBaseline(out enhancedReason);
                        repaired = repaired || enhanced;
                        if (enhanced)
                            actions.Add("save critical rules repaired into verified safe copy: " + enhancedReason);
                        else if (!String.IsNullOrWhiteSpace(enhancedReason))
                            warnings.Add("save repair: " + enhancedReason);
                    }
                    else if (repaired)
                    {
                        actions.Add("selected save is verified ready after repair/copy");
                    }
                    else
                    {
                        actions.Add("selected save was already verified ready");
                    }
                    PrepareWorkflowSaveSurgeryBaseline();
                }
                catch (Exception ex)
                {
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
                bool ready = remainingBlockers.Count == 0;
                if (ready && warnings.Count == 0)
                    outcome = actions.Count == 0 ? "AlreadyReady" : "Ready";
                else if (actions.Count > 0)
                    outcome = "PartiallyFixed";
                else
                    outcome = "Blocked";

                string report = BuildWorkflowFixResultText(runId, outcome, beforeSnapshot, afterSnapshot, beforeHost, afterHost, beforeSave, afterSave, actions, remainingBlockers, warnings);
                PublishWorkflowPostFixSnapshot(afterSnapshot, report + "\r\n" + afterSnapshot.Summary);
                WriteWorkflowStatusReport();
                SetWorkflowFixProgress(5, "post-check published");

                Log("RESULT| Workflow Fix save + host | outcome=" + outcome + " | run=" + runId + " | host=" + ScoreText(beforeHost) + "->" + ScoreText(afterHost) + " | save=" + ScoreText(beforeSave) + "->" + ScoreText(afterSave) + " | fix=" + actions.Count + " | manual=" + remainingBlockers.Count + " | warnings=" + warnings.Count);
                SetStatusText("Fix save + host " + outcome + ". Host " + ScoreText(beforeHost) + " -> " + ScoreText(afterHost) + ", save " + ScoreText(beforeSave) + " -> " + ScoreText(afterSave) + ".");

                if (!ready || warnings.Count > 0)
                    MessageBox.Show(report, "CK3MPS workflow", MessageBoxButtons.OK, ready ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                else
                    MessageBox.Show("Fix save + host verified. Workflow is ready.", "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                outcome = "Failed";
                Log("RESULT| Workflow Fix save + host | outcome=Failed | run=" + runId + " | host=" + ScoreText(beforeHost) + "->n/a | save=" + ScoreText(beforeSave) + "->n/a | fix=" + actions.Count + " | manual=" + remainingBlockers.Count + " | warnings=" + (warnings.Count + 1));
                SetStatusText("Fix save + host failed: " + ex.Message);
                MessageBox.Show("Fix save + host failed.\r\n\r\n" + ex.Message, "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
                workflowProgressBar.Visible = false;
                workflowSaveHostFixInProgress = false;
                workflowApplySafeStartButton.Text = "Fix save + host";
                ConfigureCombinedWorkflowFixButton();
                UpdateWorkflowActionAvailability();
            }
        }

        private void SetWorkflowFixProgress(int value, string message)
        {
            workflowProgressBar.Value = Math.Min(workflowProgressBar.Maximum, Math.Max(workflowProgressBar.Minimum, value));
            workflowVerdictLabel.Text = "Status: Fix save + host: " + message;
            ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
            Application.DoEvents();
        }

        private void PublishWorkflowPostFixSnapshot(WorkflowScenarioSnapshot snapshot, string detailsText)
        {
            if (snapshot == null)
                return;

            workflowRenderTimer.Stop();
            StoreWorkflowScenarioSnapshot(snapshot);
            workflowRenderStates = snapshot.States ?? new List<WorkflowStepState>();
            workflowRenderVerdict = snapshot.Verdict ?? "";
            workflowRenderSummary = detailsText ?? snapshot.Summary ?? "";
            workflowRenderIndex = workflowRenderStates.Count;
            workflowStepStates.Clear();
            workflowStepStates.AddRange(workflowRenderStates);

            updatingWorkflowUi = true;
            try
            {
                workflowStepsListBox.Items.Clear();
                foreach (WorkflowStepState state in workflowStepStates)
                    workflowStepsListBox.Items.Add(FormatWorkflowStepText(state));
            }
            finally
            {
                updatingWorkflowUi = false;
            }

            workflowVerdictLabel.Text = String.IsNullOrEmpty(workflowRenderVerdict) ? "Status: waiting for automatic checks." : workflowRenderVerdict;
            ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
            workflowSummaryBox.Text = workflowRenderSummary;
            ApplyWorkflowSummaryStyling();
            UpdateWorkflowActionAvailability();
        }

        private string BuildWorkflowFixPreflightText(string runId, WorkflowScenarioSnapshot snapshot, HostSuitabilityResult host, HostSaveCandidateResult save)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Fix save + host preflight");
            sb.AppendLine("Run: " + runId);
            sb.AppendLine();
            sb.AppendLine("Current state");
            sb.AppendLine("- Host: " + ScoreText(host));
            sb.AppendLine("- Save: " + ScoreText(save));
            sb.AppendLine("- Status: " + (snapshot == null ? "n/a" : snapshot.Verdict));
            sb.AppendLine();
            sb.AppendLine("The operation will work on the selected save, create/refresh a safe copy when possible, apply host profile fixes, then publish a fresh post-check snapshot to What to do.");
            sb.AppendLine();
            sb.AppendLine("Press OK to start. Cancel is safe because no mutation has started yet.");
            return sb.ToString();
        }

        private string BuildWorkflowFixResultText(string runId, string outcome, WorkflowScenarioSnapshot beforeSnapshot, WorkflowScenarioSnapshot afterSnapshot, HostSuitabilityResult beforeHost, HostSuitabilityResult afterHost, HostSaveCandidateResult beforeSave, HostSaveCandidateResult afterSave, List<string> actions, List<string> blockers, List<string> warnings)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Fix save + host result");
            sb.AppendLine("Run: " + runId);
            sb.AppendLine("Outcome: " + outcome);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Readiness before/after");
            sb.AppendLine("- Host: " + ScoreText(beforeHost) + " -> " + ScoreText(afterHost));
            sb.AppendLine("- Save: " + ScoreText(beforeSave) + " -> " + ScoreText(afterSave));
            sb.AppendLine("- Before status: " + (beforeSnapshot == null ? "n/a" : NullText(beforeSnapshot.Verdict)));
            sb.AppendLine("- After status: " + (afterSnapshot == null ? "n/a" : NullText(afterSnapshot.Verdict)));
            sb.AppendLine();
            sb.AppendLine("Verified actions");
            AppendListOrNone(sb, actions, "- none");
            sb.AppendLine();
            sb.AppendLine("Remaining automatic blockers");
            AppendListOrNone(sb, blockers, "- none");
            sb.AppendLine();
            sb.AppendLine("Warnings / manual attention");
            AppendListOrNone(sb, warnings, "- none");
            sb.AppendLine();
            sb.AppendLine("Post-check evidence");
            sb.AppendLine("- Selected save: " + (afterSave == null || afterSave.Save == null ? "n/a" : NullText(afterSave.Save.Path)));
            sb.AppendLine("- Selected save hash: " + (afterSave == null || afterSave.Save == null ? "n/a" : FileHashOrMissing(afterSave.Save.Path)));
            sb.AppendLine("- Host profile was re-read from current settings; no synthetic readiness cache is used.");
            sb.AppendLine();
            return sb.ToString();
        }

        private void AppendListOrNone(StringBuilder sb, List<string> items, string noneLine)
        {
            if (items == null || items.Count == 0)
            {
                sb.AppendLine(noneLine);
                return;
            }
            foreach (string item in items)
                sb.AppendLine("- " + item);
        }

        private string ScoreText(HostSuitabilityResult result)
        {
            return result == null ? "n/a" : result.Score + "/100";
        }

        private string ScoreText(HostSaveCandidateResult result)
        {
            return result == null ? "n/a" : result.Score + "/100";
        }

        private void ApplyWorkflowHostReadinessFixes()
        {
            if (!ValidateBeforeRun())
                throw new InvalidOperationException("Configured game/settings paths are not valid.");

            BackupSteamAndLauncherSettings();
            StabilizeSteamSettings();
            ForceNoMods();
            StabilizePdxSettings();
            WriteStableGameRuleProfile();
            FlushDnsCache();
            EnsureFirewallRules();
            ApplyWindowsGameNetworkProfile();
            ApplyPowerAdapterProfile();
            CheckOnlineServices();
        }

        private bool WorkflowSaveIsReady(HostSaveCandidateResult save)
        {
            return save != null
                && save.Score >= 70
                && save.Save != null
                && save.Save.Readable
                && save.Save.VersionMatchesInstalled
                && !save.Save.SuspiciousName
                && AllCriticalRulesSafe(save.Save.Rules);
        }

        private bool TryForceWorkflowHostSaveIntoSafeBaseline(out string reason)
        {
            reason = "";
            HostSaveCandidateResult current = AnalyzeWorkflowHostSaveCandidate();
            if (current == null || current.Save == null || String.IsNullOrWhiteSpace(current.Save.Path) || !File.Exists(current.Save.Path))
            {
                reason = "selected save is missing.";
                return false;
            }

            string sourcePath = current.Save.Path;
            string repairedPath = BuildRepairedHostSavePath(sourcePath);
            string dir = Path.GetDirectoryName(repairedPath) ?? Path.GetDirectoryName(sourcePath) ?? "";
            string tempPath = Path.Combine(dir, Path.GetFileName(repairedPath) + ".forced-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Directory.CreateDirectory(dir);
                List<string> appliedRules;
                string failureReason;
                if (!TryRewriteSaveWithBroadRuleRepair(sourcePath, tempPath, out appliedRules, out failureReason))
                {
                    reason = String.IsNullOrWhiteSpace(failureReason) ? "no repairable game-rule area was found." : failureReason;
                    return false;
                }

                if (File.Exists(repairedPath))
                    BackupForRestore(repairedPath, "Before CK3MPS refreshes verified repaired host save copy: " + repairedPath);
                else
                    RecordCreatedFileForRestore(repairedPath, "CK3MPS verified repaired host save copy: " + repairedPath);

                SafeAtomicFile.ReplaceFile(tempPath, repairedPath);
                workflowSelectedSavePath = repairedPath;
                SaveAppConfig();
                InvalidateHostSaveAnalysisCache();
                RefreshWorkflowSaveSelectionList();

                HostSaveCandidateResult repaired = AnalyzeWorkflowHostSaveCandidate();
                if (!WorkflowSaveIsReady(repaired))
                {
                    reason = "repaired save was created but post-check still scores " + (repaired == null ? 0 : repaired.Score) + "/100.";
                    return false;
                }

                reason = appliedRules == null || appliedRules.Count == 0 ? "safe game-rule profile injected." : "safe game-rule profile injected: " + String.Join(", ", appliedRules.ToArray()) + ".";
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
            finally
            {
                SafeAtomicFile.TryDeleteTempFile(tempPath);
            }
        }

        private bool TryRewriteSaveWithBroadRuleRepair(string sourcePath, string tempPath, out List<string> appliedRules, out string failureReason)
        {
            appliedRules = new List<string>();
            failureReason = "";
            long zipOffset;
            bool startsWithZip;
            if (!TryDetectZipLayout(sourcePath, out startsWithZip, out zipOffset, out failureReason))
                return false;

            if (startsWithZip || zipOffset > 0)
                return TryRewriteZipWithBroadRuleRepair(sourcePath, startsWithZip ? 0 : zipOffset, tempPath, out appliedRules, out failureReason);

            string originalText;
            using (FileStream stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                originalText = DecodeUtf8Bounded(ReadStreamBytesBounded(stream, MaxSaveRepairFileBytes));

            string updatedText = ApplyBroadCriticalRuleRepairsToText(originalText, out appliedRules);
            if (appliedRules.Count == 0 || String.Equals(originalText, updatedText, StringComparison.Ordinal))
            {
                failureReason = "save text does not expose a repairable game_rules block.";
                return false;
            }

            WriteUtf8FileFlushed(tempPath, updatedText);
            return true;
        }

        private bool TryRewriteZipWithBroadRuleRepair(string sourcePath, long zipOffset, string tempPath, out List<string> appliedRules, out string failureReason)
        {
            appliedRules = new List<string>();
            failureReason = "";
            using (FileStream input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                if (zipOffset < 0 || zipOffset >= input.Length)
                {
                    failureReason = "embedded zip offset is invalid.";
                    return false;
                }

                if (zipOffset > 0)
                {
                    CopyExactBytes(input, output, zipOffset);
                    input.Position = zipOffset;
                }

                bool changed = false;
                HashSet<string> normalizedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long totalUncompressedBytes = 0;
                using (ZipArchive sourceArchive = new ZipArchive(input, ZipArchiveMode.Read, true))
                {
                    if (sourceArchive.Entries.Count > MaxSaveRepairZipEntryCount)
                    {
                        failureReason = "save zip contains too many entries.";
                        return false;
                    }

                    using (ZipArchive destinationArchive = new ZipArchive(output, ZipArchiveMode.Create, true))
                    {
                        foreach (ZipArchiveEntry sourceEntry in sourceArchive.Entries)
                        {
                            string normalizedName;
                            if (!TryValidateRepairZipEntry(sourceEntry, normalizedEntryNames, ref totalUncompressedBytes, out normalizedName, out failureReason))
                                return false;

                            ZipArchiveEntry destinationEntry = destinationArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                            destinationEntry.LastWriteTime = sourceEntry.LastWriteTime;
                            if (String.Equals(normalizedName, "meta", StringComparison.OrdinalIgnoreCase)
                                || String.Equals(normalizedName, "gamestate", StringComparison.OrdinalIgnoreCase))
                            {
                                string originalText = DecodeUtf8Bounded(ReadZipEntryBytesBounded(sourceEntry, MaxSaveRepairEntryUncompressedBytes));
                                List<string> entryRules;
                                string updatedText = ApplyBroadCriticalRuleRepairsToText(originalText, out entryRules);
                                if (entryRules.Count > 0 && !String.Equals(originalText, updatedText, StringComparison.Ordinal))
                                {
                                    changed = true;
                                    MergeAppliedRules(appliedRules, entryRules);
                                }
                                using (Stream destinationStream = destinationEntry.Open())
                                    WriteUtf8StreamFlushed(destinationStream, updatedText);
                            }
                            else
                            {
                                using (Stream sourceStream = sourceEntry.Open())
                                using (Stream destinationStream = destinationEntry.Open())
                                    sourceStream.CopyTo(destinationStream);
                            }
                        }
                    }
                }

                if (!changed)
                {
                    failureReason = "save zip meta/gamestate entries did not expose a repairable game_rules block.";
                    return false;
                }

                output.Flush(true);
                return true;
            }
        }

        private string ApplyBroadCriticalRuleRepairsToText(string text, out List<string> appliedRules)
        {
            string repaired = ApplyCriticalRuleRepairsToText(text, out appliedRules);
            if (String.IsNullOrEmpty(repaired))
                return repaired;

            string settingsBody;
            int settingsOpen;
            int settingsClose;
            if (!TryLocateGameRuleSettingsBody(repaired, out settingsBody, out settingsOpen, out settingsClose))
            {
                string injected = InjectSafeGameRuleSettingsBlock(repaired, out appliedRules);
                if (!String.Equals(injected, repaired, StringComparison.Ordinal))
                    return injected;
            }

            string current = repaired;
            foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
            {
                string before = current;
                current = ReplaceDirectRuleAssignments(current, definition);
                if (!String.Equals(before, current, StringComparison.Ordinal))
                    AddAppliedRuleName(appliedRules, definition.DisplayName);
            }
            return current;
        }

        private string InjectSafeGameRuleSettingsBlock(string text, out List<string> appliedRules)
        {
            appliedRules = new List<string>();
            Match rulesMatch = Regex.Match(text ?? "", "(?is)\\bgame_rules\\s*=\\s*\\{");
            if (!rulesMatch.Success)
                return text;

            int openIndex = text.IndexOf('{', rulesMatch.Index);
            if (openIndex < 0)
                return text;

            StringBuilder sb = new StringBuilder();
            sb.Append("\n\tsettings={ ");
            foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
            {
                if (!String.IsNullOrWhiteSpace(definition.SafeToken))
                {
                    sb.Append(definition.SafeToken);
                    sb.Append(' ');
                    AddAppliedRuleName(appliedRules, definition.DisplayName);
                }
            }
            sb.Append("}\n");
            return text.Substring(0, openIndex + 1) + sb.ToString() + text.Substring(openIndex + 1);
        }

        private string ReplaceDirectRuleAssignments(string text, CriticalSaveRuleDefinition definition)
        {
            if (definition == null || definition.Keys == null || String.IsNullOrWhiteSpace(definition.SafeToken))
                return text;

            string current = text ?? "";
            foreach (string key in definition.Keys)
            {
                if (String.IsNullOrWhiteSpace(key))
                    continue;

                current = Regex.Replace(
                    current,
                    "(?im)^(\\s*" + Regex.Escape(key) + "\\s*=\\s*)\"?[^\\s\\r\\n\"{}]+\"?",
                    "$1" + definition.SafeToken);
                current = Regex.Replace(
                    current,
                    "(?ims)(\\b" + Regex.Escape(key) + "\\s*=\\s*\\{[^{}]*?\\b(?:value|selected|option|setting)\\s*=\\s*)\"?[^\\s\\r\\n\"{}]+\"?",
                    "$1" + definition.SafeToken);
            }
            return current;
        }

        private void AddAppliedRuleName(List<string> appliedRules, string displayName)
        {
            if (appliedRules == null || String.IsNullOrWhiteSpace(displayName))
                return;
            foreach (string existing in appliedRules)
                if (String.Equals(existing, displayName, StringComparison.OrdinalIgnoreCase))
                    return;
            appliedRules.Add(displayName);
        }

        private List<string> CollectRemainingWorkflowAutoBlockers(WorkflowScenarioSnapshot snapshot)
        {
            List<string> blockers = new List<string>();
            if (snapshot == null || snapshot.States == null)
                return blockers;
            foreach (WorkflowStepState state in snapshot.States)
            {
                if (state.Required && state.AutoManaged && !state.Passed)
                    blockers.Add(MakeWorkflowStepLabelReadable(state));
            }
            return blockers;
        }

        private void OpenParityRoomWithOnlineDashboardText()
        {
            System.Windows.Forms.Timer patchTimer = new System.Windows.Forms.Timer();
            patchTimer.Interval = 250;
            patchTimer.Tick += delegate
            {
                Form form = FindOpenParityRoomForm();
                if (form == null)
                    return;
                PatchParityRoomOnlineText(form);
                if (form.IsDisposed)
                    patchTimer.Stop();
            };
            patchTimer.Start();
            try
            {
                OpenParityRoom();
            }
            finally
            {
                patchTimer.Stop();
                patchTimer.Dispose();
            }
        }

        private Form FindOpenParityRoomForm()
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form != null && form != this && form.Text.IndexOf("parity room", StringComparison.OrdinalIgnoreCase) >= 0)
                    return form;
            }
            return null;
        }

        private void PatchParityRoomOnlineText(Control root)
        {
            foreach (Control control in root.Controls)
            {
                Button button = control as Button;
                if (button != null)
                {
                    if (String.Equals(button.Text, "Create local room", StringComparison.OrdinalIgnoreCase))
                        button.Text = "Create online room";
                }

                Label label = control as Label;
                if (label != null)
                    label.Text = PatchParityRoomOnlineString(label.Text);

                RichTextBox richText = control as RichTextBox;
                if (richText != null)
                {
                    string patched = PatchParityRoomOnlineString(richText.Text);
                    if (!String.Equals(richText.Text, patched, StringComparison.Ordinal))
                        richText.Text = patched;
                }

                if (control.HasChildren)
                    PatchParityRoomOnlineText(control);
            }
        }

        private string PatchParityRoomOnlineString(string value)
        {
            string text = value ?? "";
            string host = GetOnlineParityAdvertisedAddress();
            text = text.Replace("Create a local loopback room or join an existing local host room.", "Create an online direct room or join a host by reachable IP. Do not use 127.0.0.1 for another player.");
            text = text.Replace("Create a local loopback room or join an existing local host room", "Create an online direct room or join a host by reachable IP");
            text = text.Replace("local room", "online room");
            text = text.Replace("local host room", "online host room");
            text = text.Replace("Live local room", "Live online-capable room");
            text = text.Replace("Host: 127.0.0.1", "Host/IP to share: " + host);
            return text;
        }

        private string GetOnlineParityAdvertisedAddress()
        {
            string address = GetPrimaryNonLoopbackIPv4Address();
            return String.IsNullOrWhiteSpace(address) ? "<your public/VPN IP>" : address;
        }

        private string GetPrimaryNonLoopbackIPv4Address()
        {
            try
            {
                foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;
                    IPInterfaceProperties properties = networkInterface.GetIPProperties();
                    foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                            return address.Address.ToString();
                    }
                }
            }
            catch
            {
            }
            return "";
        }
    }
}
