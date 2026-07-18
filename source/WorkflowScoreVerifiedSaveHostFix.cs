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
        private void ConfigureScoreVerifiedSaveHostFixHandler()
        {
            if (workflowApplySafeStartButton == null || workflowSaveHostFixInProgress)
                return;

            workflowApplySafeStartButton.Text = "Fix save + host";
            workflowApplySafeStartButton.Enabled = !IsGameRunning();
            ReplaceClickHandlers(workflowApplySafeStartButton, delegate { RunWorkflowScoreVerifiedSaveHostFix(); });
        }

        private void RunWorkflowScoreVerifiedSaveHostFix()
        {
            if (workflowSaveHostFixInProgress)
            {
                MessageBox.Show("Fix save + host is already running.", "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string reason;
            try
            {
                HostSaveCandidateResult before = AnalyzeWorkflowHostSaveCandidate();
                if (!WorkflowSaveIsReady(before))
                {
                    if (TryPublishScoreVerifiedSelectedSaveBaseline(out reason))
                    {
                        HostSaveCandidateResult after = AnalyzeWorkflowHostSaveCandidate();
                        Log("OK   Workflow score-verified save pre-repair selected save " + ScoreText(before) + " -> " + ScoreText(after) + ": " + reason);
                    }
                    else
                    {
                        Log("WARN Workflow score-verified save pre-repair did not reach ready state: " + reason);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("WARN Workflow score-verified save pre-repair failed: " + ex.Message);
            }

            RunWorkflowSaveAndHostFixNonBlocking();
        }

        private bool TryPublishScoreVerifiedSelectedSaveBaseline(out string reason)
        {
            reason = "";
            HostSaveCandidateResult current = AnalyzeWorkflowHostSaveCandidate();
            if (current == null || current.Save == null || String.IsNullOrWhiteSpace(current.Save.Path) || !File.Exists(current.Save.Path))
            {
                reason = "selected save is missing";
                return false;
            }
            if (!current.Save.Readable)
            {
                reason = "selected save is not safely readable";
                return false;
            }
            if (!current.Save.VersionMatchesInstalled)
            {
                reason = "selected save version does not match installed CK3 version";
                return false;
            }

            string sourcePath = current.Save.Path;
            string originalSelectedPath = workflowSelectedSavePath;
            string repairedPath = BuildAvailableWorkflowSafeSavePath(sourcePath);
            string dir = Path.GetDirectoryName(repairedPath) ?? Path.GetDirectoryName(sourcePath) ?? "";
            string tempPath = Path.Combine(dir, Path.GetFileName(repairedPath) + ".score-verified-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Directory.CreateDirectory(dir);
                List<string> appliedRules;
                string failureReason;
                if (!TryRewriteWorkflowSaveWithScoreVerifiedRules(sourcePath, tempPath, out appliedRules, out failureReason))
                {
                    reason = String.IsNullOrWhiteSpace(failureReason) ? "selected save did not expose a score-repairable rule area" : failureReason;
                    return false;
                }

                RecordCreatedFileForRestore(repairedPath, "CK3MPS score-verified repaired host save copy: " + repairedPath);
                SafeAtomicFile.ReplaceFile(tempPath, repairedPath);
                workflowSelectedSavePath = repairedPath;
                SaveAppConfig();
                InvalidateHostSaveAnalysisCache();
                RefreshWorkflowSaveSelectionList();

                HostSaveCandidateResult repaired = AnalyzeWorkflowHostSaveCandidate();
                if (WorkflowSaveIsReady(repaired))
                {
                    reason = appliedRules == null || appliedRules.Count == 0
                        ? "score-visible critical game rules normalized"
                        : "score-visible critical game rules normalized: " + String.Join(", ", appliedRules.ToArray());
                    return true;
                }

                reason = "score-verified copy still failed post-check " + ScoreText(repaired) + "; remaining rules: " + WorkflowSaveScoreRuleDiagnostics(repaired);
                workflowSelectedSavePath = originalSelectedPath;
                SaveAppConfig();
                InvalidateHostSaveAnalysisCache();
                RefreshWorkflowSaveSelectionList();
                return false;
            }
            catch (Exception ex)
            {
                workflowSelectedSavePath = originalSelectedPath;
                SaveAppConfig();
                InvalidateHostSaveAnalysisCache();
                RefreshWorkflowSaveSelectionList();
                reason = ex.Message;
                return false;
            }
            finally
            {
                SafeAtomicFile.TryDeleteTempFile(tempPath);
            }
        }

        private bool TryRewriteWorkflowSaveWithScoreVerifiedRules(string sourcePath, string tempPath, out List<string> appliedRules, out string failureReason)
        {
            appliedRules = new List<string>();
            failureReason = "";
            long zipOffset;
            bool startsWithZip;
            if (!TryDetectLargeScoreZipLayout(sourcePath, out startsWithZip, out zipOffset, out failureReason))
                return false;
            if (startsWithZip || zipOffset > 0)
                return TryRewriteWorkflowZipWithScoreVerifiedRules(sourcePath, startsWithZip ? 0 : zipOffset, tempPath, out appliedRules, out failureReason);

            using (FileStream input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                bool changed = RewriteScoreVerifiedPrefix(input, output, appliedRules, true);
                output.Flush(true);
                if (!changed)
                {
                    failureReason = "plain-text save did not expose score-visible critical game rules";
                    return false;
                }
                return true;
            }
        }

        private bool TryRewriteWorkflowZipWithScoreVerifiedRules(string sourcePath, long zipOffset, string tempPath, out List<string> appliedRules, out string failureReason)
        {
            appliedRules = new List<string>();
            failureReason = "";
            string isolatedZipPath = tempPath + ".source.zip";
            string sourceZipPath = sourcePath;
            string repairedZipPath = tempPath + ".payload.zip";
            try
            {
                FileInfo info = new FileInfo(sourcePath);
                if (info.Length <= 0 || info.Length > WorkflowLargeSaveRepairFileBytes)
                {
                    failureReason = "save exceeds the large safe repair size limit";
                    return false;
                }
                if (zipOffset < 0 || zipOffset >= info.Length)
                {
                    failureReason = "embedded zip offset is invalid";
                    return false;
                }

                if (zipOffset > 0)
                {
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
                bool sawGamestate = false;
                HashSet<string> normalizedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long totalUncompressedBytes = 0;
                using (FileStream sourceZipStream = new FileStream(sourceZipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (ZipArchive sourceArchive = new ZipArchive(sourceZipStream, ZipArchiveMode.Read, false))
                using (FileStream repairedZipStream = new FileStream(repairedZipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (ZipArchive destinationArchive = new ZipArchive(repairedZipStream, ZipArchiveMode.Create, true))
                {
                    if (sourceArchive.Entries.Count > WorkflowLargeSaveRepairZipEntryCount)
                    {
                        failureReason = "save zip contains too many entries";
                        return false;
                    }

                    foreach (ZipArchiveEntry sourceEntry in sourceArchive.Entries)
                    {
                        string normalizedName;
                        string validationFailure;
                        if (!TryValidateLargeScoreRepairZipEntry(sourceEntry, normalizedEntryNames, ref totalUncompressedBytes, out normalizedName, out validationFailure))
                        {
                            failureReason = validationFailure;
                            return false;
                        }

                        ZipArchiveEntry destinationEntry = destinationArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Fastest);
                        destinationEntry.LastWriteTime = sourceEntry.LastWriteTime;
                        using (Stream sourceStream = sourceEntry.Open())
                        using (Stream destinationStream = destinationEntry.Open())
                        {
                            bool isMeta = String.Equals(normalizedName, "meta", StringComparison.OrdinalIgnoreCase);
                            bool isGamestate = String.Equals(normalizedName, "gamestate", StringComparison.OrdinalIgnoreCase);
                            if (isMeta || isGamestate)
                            {
                                if (isGamestate)
                                    sawGamestate = true;
                                if (RewriteScoreVerifiedPrefix(sourceStream, destinationStream, appliedRules, isGamestate))
                                    changed = true;
                            }
                            else
                            {
                                CopyWorkflowStreamBounded(sourceStream, destinationStream, sourceEntry.Length);
                            }
                        }
                    }
                }

                if (!changed || !sawGamestate)
                {
                    failureReason = sawGamestate
                        ? "save zip meta/gamestate did not expose score-visible critical game rules"
                        : "save zip does not contain a gamestate entry";
                    return false;
                }

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
                return true;
            }
            catch (Exception ex)
            {
                failureReason = "score-verified zip rewrite failed: " + ex.Message;
                return false;
            }
            finally
            {
                SafeAtomicFile.TryDeleteTempFile(isolatedZipPath);
                SafeAtomicFile.TryDeleteTempFile(repairedZipPath);
            }
        }

        private bool RewriteScoreVerifiedPrefix(Stream input, Stream output, List<string> appliedRules, bool forceVisibleSafeBlock)
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
            string updatedPrefix = BuildScoreVerifiedCriticalRulesPrefix(originalPrefix, appliedRules, forceVisibleSafeBlock);
            bool changed = !String.Equals(originalPrefix, updatedPrefix, StringComparison.Ordinal);
            byte[] updatedBytes = Encoding.UTF8.GetBytes(changed ? updatedPrefix : originalPrefix);
            output.Write(updatedBytes, 0, updatedBytes.Length);
            if (totalRead > transformLength)
                output.Write(prefixBuffer, transformLength, totalRead - transformLength);
            CopyWorkflowStreamToEnd(input, output);
            return changed;
        }

        private string BuildScoreVerifiedCriticalRulesPrefix(string text, List<string> appliedRules, bool forceVisibleSafeBlock)
        {
            string working = ApplyScoreDrivenCriticalRuleRepairsToText(text, out var initialRules);
            MergeAppliedRules(appliedRules, initialRules);
            working = ForceCriticalRuleAssignmentsForScore(working, appliedRules);

            if (forceVisibleSafeBlock && working.IndexOf("game_rules", StringComparison.OrdinalIgnoreCase) < 0)
            {
                foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
                    AddAppliedRuleName(appliedRules, definition.DisplayName);
                working = BuildScoreSafeGameRulesOverlay() + Environment.NewLine + working;
            }

            return working;
        }

        private string ForceCriticalRuleAssignmentsForScore(string text, List<string> appliedRules)
        {
            if (String.IsNullOrEmpty(text))
                return text;

            string updated = text;
            foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
            {
                string assignmentValue = ScoreSafeAssignmentValue(definition);
                bool definitionChanged = false;
                foreach (string key in definition.Keys ?? new string[0])
                {
                    string pattern = "(?im)^(\\s*)" + Regex.Escape(key) + "\\s*=\\s*(?:\"[^\"]*\"|[^\\s\\r\\n{}]+)";
                    string replacement = "$1" + key + "=" + assignmentValue;
                    string replaced = Regex.Replace(updated, pattern, replacement);
                    if (!String.Equals(replaced, updated, StringComparison.Ordinal))
                    {
                        updated = replaced;
                        definitionChanged = true;
                    }
                }

                if (definitionChanged)
                    AddAppliedRuleName(appliedRules, definition.DisplayName);
            }

            string gameRulesBlock = SaveRuleUtilities.ExtractBraceBlock(updated, "game_rules");
            if (!String.IsNullOrEmpty(gameRulesBlock))
            {
                int insertAt = updated.IndexOf(gameRulesBlock, StringComparison.Ordinal);
                if (insertAt >= 0)
                {
                    StringBuilder assignments = new StringBuilder();
                    foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
                    {
                        string key = definition.Keys != null && definition.Keys.Length > 0 ? definition.Keys[0] : definition.Id;
                        if (gameRulesBlock.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            assignments.AppendLine("\t" + key + "=" + ScoreSafeAssignmentValue(definition));
                            AddAppliedRuleName(appliedRules, definition.DisplayName);
                        }
                    }
                    if (assignments.Length > 0)
                        updated = updated.Substring(0, insertAt + 1) + Environment.NewLine + assignments + updated.Substring(insertAt + 1);
                }
            }
            else
            {
                updated = BuildScoreSafeGameRulesOverlay() + Environment.NewLine + updated;
                foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
                    AddAppliedRuleName(appliedRules, definition.DisplayName);
            }

            return updated;
        }

        private string ScoreSafeAssignmentValue(CriticalSaveRuleDefinition definition)
        {
            if (definition == null)
                return "disabled";
            if (String.Equals(definition.Id, "ai_landless_adventurers", StringComparison.OrdinalIgnoreCase))
                return "25";
            if (String.Equals(definition.Id, "multiplayer_murder_schemes", StringComparison.OrdinalIgnoreCase))
                return definition.SafeToken;
            if (String.Equals(definition.Id, "great_steppe", StringComparison.OrdinalIgnoreCase))
                return "off";
            return "disabled";
        }

        private string BuildScoreSafeGameRulesOverlay()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("game_rules={");
            sb.Append("\tsettings={ ");
            foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
            {
                if (!String.IsNullOrWhiteSpace(definition.SafeToken))
                    sb.Append(definition.SafeToken).Append(' ');
            }
            sb.AppendLine("}");
            foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
            {
                string key = definition.Keys != null && definition.Keys.Length > 0 ? definition.Keys[0] : definition.Id;
                sb.Append('\t').Append(key).Append('=').AppendLine(ScoreSafeAssignmentValue(definition));
            }
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
