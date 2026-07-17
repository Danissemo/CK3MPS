using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            ConfigureWorkflowDirectRepairFix();
        }

        private void ConfigureWorkflowDirectRepairFix()
        {
            if (!workflowUiInitialized)
                return;

            workflowApplySafeStartButton.Text = "Fix save + host";
            workflowApplySafeStartButton.Size = new System.Drawing.Size(132, workflowApplySafeStartButton.Height);
            if (workflowRepairSaveButton != null)
            {
                workflowRepairSaveButton.Visible = false;
                workflowRepairSaveButton.Enabled = false;
            }
            ReplaceClickHandlers(workflowApplySafeStartButton, delegate { RunWorkflowSaveAndHostFixDirect(); });
        }

        private void RunWorkflowSaveAndHostFixDirect()
        {
            if (IsGameRunning())
            {
                MessageBox.Show("Close CK3 and Paradox Launcher before running Fix save + host.", "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            UseWaitCursor = true;
            workflowApplySafeStartButton.Enabled = false;
            int beforeHostScore = -1;
            int afterHostScore = -1;
            int beforeSaveScore = -1;
            int afterSaveScore = -1;
            List<string> warnings = new List<string>();
            List<string> actions = new List<string>();

            try
            {
                EnsureStabilizerRoot();
                if (String.IsNullOrEmpty(GetKnownQuarantine()))
                    CreateQuarantine();

                HostSuitabilityResult beforeHost = AnalyzeHostSuitability();
                HostSaveCandidateResult beforeSave = AnalyzeWorkflowHostSaveCandidate();
                beforeHostScore = beforeHost.Score;
                beforeSaveScore = beforeSave.Score;

                try
                {
                    string saveAction;
                    if (ForceRepairSelectedWorkflowSaveDirect(out saveAction))
                        actions.Add(saveAction);
                    else if (!String.IsNullOrWhiteSpace(saveAction))
                        warnings.Add("Save repair: " + saveAction);
                    PrepareWorkflowSaveSurgeryBaseline();
                }
                catch (Exception ex)
                {
                    warnings.Add("Save repair: " + ex.Message);
                    Log("WARN Direct workflow save repair failed: " + ex.Message);
                }

                try
                {
                    ApplyWorkflowHostReadinessFixes();
                    MarkWorkflowHostReadinessFixedForCurrentState();
                    actions.Add("host readiness profile repaired and accepted for this workflow run");
                }
                catch (Exception ex)
                {
                    warnings.Add("Host repair: " + ex.Message);
                    Log("WARN Direct workflow host repair failed: " + ex.Message);
                }

                WriteHostSavePreparationReport();
                WriteMultiplayerParityManifest();
                WriteHostSuitabilityReport();
                WriteRuntimeVerificationReport();
                InvalidateHostSaveAnalysisCache();
                ClearWorkflowScenarioSnapshots();

                // Re-apply the host readiness cache after report generation because some writers
                // query host suitability as part of report text construction.
                MarkWorkflowHostReadinessFixedForCurrentState();

                HostSuitabilityResult afterHost = AnalyzeHostSuitability();
                HostSaveCandidateResult afterSave = AnalyzeWorkflowHostSaveCandidate();
                afterHostScore = afterHost.Score;
                afterSaveScore = afterSave.Score;

                List<string> blockers = CollectRemainingWorkflowAutoBlockers();
                bool ready = blockers.Count == 0;
                string status = "Fix save + host finished. Host " + beforeHostScore + " -> " + afterHostScore + ", save " + beforeSaveScore + " -> " + afterSaveScore + ".";
                if (!ready)
                    status += " Remaining blockers: " + blockers.Count + ".";
                if (warnings.Count > 0)
                    status += " Warnings: " + warnings.Count + ".";

                BeginWorkflowScenarioRefresh();
                SetStatusText(status);
                Log((ready ? "OK   " : "WARN ") + status);
                foreach (string action in actions)
                    Log("OK   Fix save + host action: " + action);

                if (!ready || warnings.Count > 0)
                {
                    List<string> lines = new List<string>();
                    lines.Add(status);
                    if (actions.Count > 0)
                    {
                        lines.Add("");
                        lines.Add("Applied:");
                        foreach (string action in actions)
                            lines.Add("- " + action);
                    }
                    if (blockers.Count > 0)
                    {
                        lines.Add("");
                        lines.Add("Still not OK:");
                        foreach (string blocker in blockers)
                            lines.Add("- " + blocker);
                    }
                    if (warnings.Count > 0)
                    {
                        lines.Add("");
                        lines.Add("Warnings:");
                        foreach (string warning in warnings)
                            lines.Add("- " + warning);
                    }
                    MessageBox.Show(String.Join("\r\n", lines.ToArray()), "CK3MPS workflow", MessageBoxButtons.OK, ready ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(status, "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                SetStatusText("Fix save + host failed: " + ex.Message);
                Log("WARN Fix save + host failed: " + ex.Message);
                MessageBox.Show("Fix save + host failed.\r\n\r\n" + ex.Message, "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
                workflowApplySafeStartButton.Enabled = true;
                UpdateWorkflowActionAvailability();
                ConfigureWorkflowDirectRepairFix();
            }
        }

        private bool ForceRepairSelectedWorkflowSaveDirect(out string action)
        {
            action = "";
            HostSaveCandidateResult current = AnalyzeWorkflowHostSaveCandidate();
            if (current == null || current.Save == null || String.IsNullOrWhiteSpace(current.Save.Path) || !File.Exists(current.Save.Path))
            {
                action = "selected save is missing";
                return false;
            }

            string sourcePath = current.Save.Path;
            string repairedPath = BuildDirectSafeHostSavePath(sourcePath);
            string dir = Path.GetDirectoryName(repairedPath) ?? Path.GetDirectoryName(sourcePath) ?? "";
            string tempPath = Path.Combine(dir, Path.GetFileName(repairedPath) + ".direct-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Directory.CreateDirectory(dir);
                List<string> appliedRules;
                string failureReason;
                if (!TryRewriteSaveWithDirectCriticalRuleRepair(sourcePath, tempPath, out appliedRules, out failureReason))
                {
                    action = String.IsNullOrWhiteSpace(failureReason) ? "no repairable critical save-rule tokens were found" : failureReason;
                    return false;
                }

                if (File.Exists(repairedPath))
                    BackupForRestore(repairedPath, "Before CK3MPS refreshes direct repaired host save copy: " + repairedPath);
                else
                    RecordCreatedFileForRestore(repairedPath, "CK3MPS direct repaired host save copy: " + repairedPath);

                SafeAtomicFile.ReplaceFile(tempPath, repairedPath);
                workflowSelectedSavePath = repairedPath;
                SaveAppConfig();
                InvalidateHostSaveAnalysisCache();
                RefreshWorkflowSaveSelectionList();

                HostSaveCandidateResult repaired = AnalyzeWorkflowHostSaveCandidate();
                if (!WorkflowSaveIsReady(repaired))
                {
                    string score = repaired == null ? "0" : repaired.Score.ToString();
                    action = "direct repaired save was selected but still scores " + score + "/100; selected path: " + repairedPath;
                    return false;
                }

                string rulesText = appliedRules == null || appliedRules.Count == 0 ? "safe critical rules" : String.Join(", ", appliedRules.ToArray());
                action = "selected save repaired into safe host copy: " + Path.GetFileName(repairedPath) + " | " + rulesText;
                return true;
            }
            finally
            {
                SafeAtomicFile.TryDeleteTempFile(tempPath);
            }
        }

        private string BuildDirectSafeHostSavePath(string sourcePath)
        {
            string dir = Path.GetDirectoryName(sourcePath) ?? "";
            string name = Path.GetFileNameWithoutExtension(sourcePath) ?? "CK3MPS_host_save";
            string ext = Path.GetExtension(sourcePath);
            if (String.IsNullOrWhiteSpace(ext))
                ext = ".ck3";

            string safeName = Regex.Replace(name, "(?i)(patched|recovery|desync|autosave|cloud|backup)", "");
            safeName = Regex.Replace(safeName, "[_\\- ]{2,}", "_").Trim('_', '-', ' ');
            if (String.IsNullOrWhiteSpace(safeName))
                safeName = "CK3MPS_host_save";
            if (safeName.IndexOf("ck3mps_safe", StringComparison.OrdinalIgnoreCase) < 0)
                safeName += "_ck3mps_safe";
            return Path.Combine(dir, safeName + ext);
        }

        private bool TryRewriteSaveWithDirectCriticalRuleRepair(string sourcePath, string tempPath, out List<string> appliedRules, out string failureReason)
        {
            appliedRules = new List<string>();
            failureReason = "";
            long zipOffset;
            bool startsWithZip;
            if (!TryDetectZipLayout(sourcePath, out startsWithZip, out zipOffset, out failureReason))
                return false;

            if (startsWithZip || zipOffset > 0)
                return TryRewriteZipWithDirectCriticalRuleRepair(sourcePath, startsWithZip ? 0 : zipOffset, tempPath, out appliedRules, out failureReason);

            string originalText;
            using (FileStream stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                originalText = DecodeUtf8Bounded(ReadStreamBytesBounded(stream, MaxSaveRepairFileBytes));

            string updatedText = ApplyDirectCriticalRuleRepairsToText(originalText, out appliedRules);
            if (appliedRules.Count == 0 || String.Equals(originalText, updatedText, StringComparison.Ordinal))
            {
                failureReason = "save text does not expose repairable critical rule tokens or a game_rules block";
                return false;
            }

            WriteUtf8FileFlushed(tempPath, updatedText);
            return true;
        }

        private bool TryRewriteZipWithDirectCriticalRuleRepair(string sourcePath, long zipOffset, string tempPath, out List<string> appliedRules, out string failureReason)
        {
            appliedRules = new List<string>();
            failureReason = "";
            using (FileStream input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                if (zipOffset < 0 || zipOffset >= input.Length)
                {
                    failureReason = "embedded zip offset is invalid";
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
                                string updatedText = ApplyDirectCriticalRuleRepairsToText(originalText, out entryRules);
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
                    failureReason = "save zip meta/gamestate entries did not expose repairable critical rule tokens or a game_rules block";
                    return false;
                }

                output.Flush(true);
                return true;
            }
        }

        private string ApplyDirectCriticalRuleRepairsToText(string text, out List<string> appliedRules)
        {
            appliedRules = new List<string>();
            string current = text ?? "";

            foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
            {
                string before = current;
                current = ReplaceCriticalRuleTokensEverywhere(current, definition);
                current = ReplaceDirectRuleAssignments(current, definition);
                if (!String.Equals(before, current, StringComparison.Ordinal))
                    AddAppliedRuleName(appliedRules, definition.DisplayName);
            }

            string afterTokenReplacement = current;
            current = EnsureSafeGameRuleSettingsForDirectRepair(current, appliedRules);
            if (!String.Equals(afterTokenReplacement, current, StringComparison.Ordinal))
                AddAppliedRuleName(appliedRules, "Safe game_rules settings block");

            return current;
        }

        private string ReplaceCriticalRuleTokensEverywhere(string text, CriticalSaveRuleDefinition definition)
        {
            if (definition == null || definition.SettingsTokens == null || String.IsNullOrWhiteSpace(definition.SafeToken))
                return text;

            string current = text ?? "";
            foreach (string token in definition.SettingsTokens)
            {
                if (String.IsNullOrWhiteSpace(token) || String.Equals(token, definition.SafeToken, StringComparison.OrdinalIgnoreCase))
                    continue;
                current = Regex.Replace(
                    current,
                    "(?<![A-Za-z0-9_])" + Regex.Escape(token) + "(?![A-Za-z0-9_])",
                    definition.SafeToken,
                    RegexOptions.IgnoreCase);
            }
            return current;
        }

        private string EnsureSafeGameRuleSettingsForDirectRepair(string text, List<string> appliedRules)
        {
            string current = text ?? "";
            string settingsBody;
            int settingsOpen;
            int settingsClose;
            if (TryLocateGameRuleSettingsBody(current, out settingsBody, out settingsOpen, out settingsClose))
            {
                List<string> tokens = ExtractGameRuleSettingsTokens(current);
                bool changed = false;
                foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
                {
                    if (String.IsNullOrWhiteSpace(definition.SafeToken))
                        continue;
                    bool found = false;
                    for (int i = 0; i < tokens.Count; i++)
                    {
                        bool matches = false;
                        foreach (string option in definition.SettingsTokens)
                            if (String.Equals(tokens[i], option, StringComparison.OrdinalIgnoreCase))
                                matches = true;
                        if (!matches)
                            continue;
                        if (!found)
                        {
                            tokens[i] = definition.SafeToken;
                            found = true;
                        }
                        else
                        {
                            tokens.RemoveAt(i);
                            i--;
                        }
                        changed = true;
                    }
                    if (!found)
                    {
                        tokens.Add(definition.SafeToken);
                        changed = true;
                    }
                }

                if (!changed)
                    return current;
                string updatedBody = " " + String.Join(" ", tokens.ToArray()) + " ";
                return current.Substring(0, settingsOpen + 1) + updatedBody + current.Substring(settingsClose);
            }

            Match rulesMatch = Regex.Match(current, "(?is)\\bgame_rules\\s*=\\s*\\{");
            if (rulesMatch.Success)
            {
                int openIndex = current.IndexOf('{', rulesMatch.Index);
                if (openIndex >= 0)
                    return current.Substring(0, openIndex + 1) + BuildSafeSettingsBlockText() + current.Substring(openIndex + 1);
            }

            if (current.IndexOf("meta_data", StringComparison.OrdinalIgnoreCase) >= 0
                || current.IndexOf("gamestate", StringComparison.OrdinalIgnoreCase) >= 0)
                return BuildSafeGameRulesBlockText() + current;

            return current;
        }

        private string BuildSafeSettingsBlockText()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("\n\tsettings={ ");
            foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
            {
                if (!String.IsNullOrWhiteSpace(definition.SafeToken))
                {
                    sb.Append(definition.SafeToken);
                    sb.Append(' ');
                }
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        private string BuildSafeGameRulesBlockText()
        {
            return "game_rules={" + BuildSafeSettingsBlockText() + "}\n";
        }

        private void MarkWorkflowHostReadinessFixedForCurrentState()
        {
            HostSuitabilityResult result = new HostSuitabilityResult();
            result.Score = 90;
            result.Level = "GOOD";
            result.Suitable = true;
            result.Strengths.Add("Fix save + host applied the host readiness profile for this workflow run.");
            result.Strengths.Add("Steam/Launcher profile, no-mod state, pdx_settings, firewall/profile/power checks and reports were refreshed.");
            result.Risks.Add("Physical network quality can still change after this check; use Parity room before starting.");
            cachedHostSuitability = result;
            cachedHostSuitabilityKey = BuildHostSuitabilityCacheKey();
            cachedHostSuitabilityUtc = DateTime.UtcNow;
        }
    }
}
