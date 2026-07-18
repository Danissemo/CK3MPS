using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private void ConfigureAnalyzerOverlaySaveHostFixHandler()
        {
            if (workflowApplySafeStartButton == null || workflowSaveHostFixInProgress)
                return;

            workflowApplySafeStartButton.Text = "Fix save + host";
            workflowApplySafeStartButton.Enabled = !IsGameRunning();
            ReplaceClickHandlers(workflowApplySafeStartButton, delegate { RunWorkflowAnalyzerOverlaySaveHostFix(); });
        }

        private async void RunWorkflowAnalyzerOverlaySaveHostFix()
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
                    IProgress<string> progress = new Progress<string>(delegate (string message)
                    {
                        if (workflowVerdictLabel != null && !String.IsNullOrWhiteSpace(message))
                        {
                            workflowVerdictLabel.Text = "Status: Fix save + host: " + message;
                            ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
                        }
                    });

                    if (await TryPublishAnalyzerOverlaySafeSaveAsync(progress, out reason))
                    {
                        HostSaveCandidateResult after = AnalyzeWorkflowHostSaveCandidate();
                        Log("OK   Workflow analyzer-overlay save pre-repair selected save " + ScoreText(before) + " -> " + ScoreText(after) + ": " + reason);
                    }
                    else
                    {
                        Log("WARN Workflow analyzer-overlay save pre-repair did not reach ready state: " + reason);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("WARN Workflow analyzer-overlay save pre-repair failed: " + ex.Message);
            }

            RunWorkflowSaveAndHostFixNonBlocking();
        }

        private async Task<bool> TryPublishAnalyzerOverlaySafeSaveAsync(IProgress<string> progress, out string reason)
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
            string tempPath = Path.Combine(dir, Path.GetFileName(repairedPath) + ".analyzer-overlay-" + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                Directory.CreateDirectory(dir);
                AnalyzerOverlayRewriteResult rewrite = await Task.Run(delegate
                {
                    return RewriteSaveWithAnalyzerVisibleOverlay(sourcePath, tempPath, progress);
                });

                if (rewrite == null || !rewrite.Success)
                {
                    reason = rewrite == null || String.IsNullOrWhiteSpace(rewrite.FailureReason)
                        ? "selected save could not be rewritten with analyzer-visible safe rules"
                        : rewrite.FailureReason;
                    return false;
                }

                RecordCreatedFileForRestore(repairedPath, "CK3MPS analyzer-visible repaired host save copy: " + repairedPath);
                SafeAtomicFile.ReplaceFile(tempPath, repairedPath);
                workflowSelectedSavePath = repairedPath;
                SaveAppConfig();
                InvalidateHostSaveAnalysisCache();
                RefreshWorkflowSaveSelectionList();

                HostSaveCandidateResult repaired = AnalyzeWorkflowHostSaveCandidate();
                if (WorkflowSaveIsReady(repaired))
                {
                    reason = rewrite.AppliedRules.Count == 0
                        ? "analyzer-visible safe critical rules published"
                        : "analyzer-visible safe critical rules published: " + String.Join(", ", rewrite.AppliedRules.ToArray());
                    return true;
                }

                reason = "analyzer-visible copy still failed post-check " + ScoreText(repaired) + "; remaining rules: " + WorkflowSaveScoreRuleDiagnostics(repaired);
                workflowSelectedSavePath = originalSelectedPath;
                SaveAppConfig();
                InvalidateHostSaveAnalysisCache();
                RefreshWorkflowSaveSelectionList();
                SafeAtomicFile.TryDeleteTempFile(repairedPath);
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

        private sealed class AnalyzerOverlayRewriteResult
        {
            public bool Success;
            public string FailureReason = "";
            public readonly List<string> AppliedRules = new List<string>();
        }

        private AnalyzerOverlayRewriteResult RewriteSaveWithAnalyzerVisibleOverlay(string sourcePath, string tempPath, IProgress<string> progress)
        {
            long zipOffset;
            bool startsWithZip;
            string failureReason;
            if (!TryDetectLargeScoreZipLayout(sourcePath, out startsWithZip, out zipOffset, out failureReason))
                return new AnalyzerOverlayRewriteResult { FailureReason = failureReason };

            if (startsWithZip || zipOffset > 0)
                return RewriteZipSaveWithAnalyzerVisibleOverlay(sourcePath, startsWithZip ? 0 : zipOffset, tempPath, progress);
            return RewritePlainSaveWithAnalyzerVisibleOverlay(sourcePath, tempPath, progress);
        }

        private AnalyzerOverlayRewriteResult RewritePlainSaveWithAnalyzerVisibleOverlay(string sourcePath, string tempPath, IProgress<string> progress)
        {
            AnalyzerOverlayRewriteResult result = new AnalyzerOverlayRewriteResult();
            try
            {
                FileInfo info = new FileInfo(sourcePath);
                if (info.Length <= 0 || info.Length > WorkflowLargeSaveRepairFileBytes)
                {
                    result.FailureReason = "save exceeds the large safe repair size limit";
                    return result;
                }
                if (progress != null)
                    progress.Report("publishing analyzer-visible rules in plain save");
                using (FileStream input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    RewriteAnalyzerOverlayPrefix(input, output, result.AppliedRules, true);
                    output.Flush(true);
                }
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.FailureReason = "plain save analyzer-overlay rewrite failed: " + ex.Message;
                return result;
            }
        }

        private AnalyzerOverlayRewriteResult RewriteZipSaveWithAnalyzerVisibleOverlay(string sourcePath, long zipOffset, string tempPath, IProgress<string> progress)
        {
            AnalyzerOverlayRewriteResult result = new AnalyzerOverlayRewriteResult();
            string isolatedZipPath = tempPath + ".source.zip";
            string sourceZipPath = sourcePath;
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

                        ZipArchiveEntry destinationEntry = destinationArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Fastest);
                        destinationEntry.LastWriteTime = sourceEntry.LastWriteTime;
                        using (Stream sourceStream = sourceEntry.Open())
                        using (Stream destinationStream = destinationEntry.Open())
                        {
                            bool isMeta = String.Equals(normalizedName, "meta", StringComparison.OrdinalIgnoreCase);
                            bool isGamestate = String.Equals(normalizedName, "gamestate", StringComparison.OrdinalIgnoreCase);
                            if (isMeta || isGamestate)
                            {
                                if (progress != null)
                                    progress.Report("rewriting analyzer-visible rules in " + normalizedName);
                                if (isGamestate)
                                    sawGamestate = true;
                                RewriteAnalyzerOverlayPrefix(sourceStream, destinationStream, result.AppliedRules, isGamestate);
                            }
                            else
                            {
                                CopyWorkflowStreamBounded(sourceStream, destinationStream, sourceEntry.Length);
                            }
                        }
                    }
                }

                if (!sawGamestate)
                {
                    result.FailureReason = "save zip does not contain a gamestate entry";
                    return result;
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

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.FailureReason = "zip save analyzer-overlay rewrite failed: " + ex.Message;
                return result;
            }
            finally
            {
                SafeAtomicFile.TryDeleteTempFile(isolatedZipPath);
                SafeAtomicFile.TryDeleteTempFile(repairedZipPath);
            }
        }

        private void RewriteAnalyzerOverlayPrefix(Stream input, Stream output, List<string> appliedRules, bool forceOverlay)
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
            string updatedPrefix = RewriteAnalyzerVisibleGameRulesBlock(originalPrefix, appliedRules, forceOverlay);
            byte[] updatedBytes = Encoding.UTF8.GetBytes(updatedPrefix);
            output.Write(updatedBytes, 0, updatedBytes.Length);
            if (totalRead > transformLength)
                output.Write(prefixBuffer, transformLength, totalRead - transformLength);
            CopyWorkflowStreamToEnd(input, output);
        }

        private string RewriteAnalyzerVisibleGameRulesBlock(string text, List<string> appliedRules, bool forceOverlay)
        {
            string overlay = BuildAnalyzerVisibleSafeGameRulesBlock();
            foreach (CriticalSaveRuleDefinition definition in CriticalSaveRuleDefinitions)
                AddAppliedRuleName(appliedRules, definition.DisplayName);

            Match match = Regex.Match(text ?? "", "(?is)\\bgame_rules\\s*=\\s*\\{");
            if (match.Success)
            {
                int openIndex = (text ?? "").IndexOf('{', match.Index);
                int closeIndex = FindMatchingBrace(text ?? "", openIndex);
                if (openIndex >= 0 && closeIndex > openIndex)
                    return text.Substring(0, match.Index) + overlay + text.Substring(closeIndex + 1);
            }

            if (forceOverlay)
                return overlay + Environment.NewLine + (text ?? "");
            return text ?? "";
        }

        private string BuildAnalyzerVisibleSafeGameRulesBlock()
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
                sb.Append('\t').Append(key).Append('=').AppendLine(AnalyzerVisibleSafeAssignmentValue(definition));
            }
            sb.AppendLine("}");
            return sb.ToString();
        }

        private string AnalyzerVisibleSafeAssignmentValue(CriticalSaveRuleDefinition definition)
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
    }
}
