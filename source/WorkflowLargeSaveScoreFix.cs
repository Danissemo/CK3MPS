using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private const long WorkflowLargeSaveRepairFileBytes = 512L * 1024L * 1024L;
        private const long WorkflowLargeSaveRepairEntryCompressedBytes = 512L * 1024L * 1024L;
        private const long WorkflowLargeSaveRepairEntryUncompressedBytes = 768L * 1024L * 1024L;
        private const long WorkflowLargeSaveRepairTotalUncompressedBytes = 1024L * 1024L * 1024L;
        private const int WorkflowLargeSaveRepairZipEntryCount = 128;
        private const int WorkflowLargeSaveRepairZipEntryNameBytes = 240;
        private const double WorkflowLargeSaveRepairCompressionRatio = 1000d;

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            ConfigureLargeSaveScoreWorkflowFixHandler();
        }

        private void ConfigureLargeSaveScoreWorkflowFixHandler()
        {
            if (workflowApplySafeStartButton == null || workflowSaveHostFixInProgress)
                return;

            workflowApplySafeStartButton.Text = "Fix save + host";
            workflowApplySafeStartButton.Enabled = !IsGameRunning();
            ReplaceClickHandlers(workflowApplySafeStartButton, delegate { RunWorkflowSaveAndHostFixWithLargeSaveScoreRepair(); });
        }

        private void RunWorkflowSaveAndHostFixWithLargeSaveScoreRepair()
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

            try
            {
                HostSaveCandidateResult before = AnalyzeWorkflowHostSaveCandidate();
                if (!WorkflowSaveIsReady(before))
                {
                    string reason;
                    if (TryForceWorkflowHostSaveIntoLargeSafeBaseline(out reason))
                    {
                        HostSaveCandidateResult after = AnalyzeWorkflowHostSaveCandidate();
                        Log("OK   Workflow large-save pre-repair verified selected host save score " + ScoreText(before) + " -> " + ScoreText(after) + ": " + reason);
                    }
                    else
                    {
                        Log("WARN Workflow large-save pre-repair could not improve selected host save score: " + reason);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("WARN Workflow large-save pre-repair failed before main fix run: " + ex.Message);
            }

            try
            {
                RunWorkflowSaveAndHostFix();
            }
            finally
            {
                BeginInvoke((MethodInvoker)delegate { ConfigureLargeSaveScoreWorkflowFixHandler(); });
            }
        }

        private bool TryForceWorkflowHostSaveIntoLargeSafeBaseline(out string reason)
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
            string repairedPath = BuildVerifiedWorkflowSafeSavePath(sourcePath);
            if (String.Equals(sourcePath, repairedPath, StringComparison.OrdinalIgnoreCase))
            {
                reason = "safe-copy destination equals source save";
                return false;
            }

            string dir = Path.GetDirectoryName(repairedPath) ?? Path.GetDirectoryName(sourcePath) ?? "";
            string tempPath = Path.Combine(dir, Path.GetFileName(repairedPath) + ".large-score-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Directory.CreateDirectory(dir);
                List<string> appliedRules;
                string failureReason;
                if (!TryRewriteSaveWithLargeScoreRuleRepair(sourcePath, tempPath, out appliedRules, out failureReason))
                {
                    reason = String.IsNullOrWhiteSpace(failureReason) ? "no repairable game-rule area was found" : failureReason;
                    return false;
                }

                if (File.Exists(repairedPath))
                    BackupForRestore(repairedPath, "Before CK3MPS refreshes large verified repaired host save copy: " + repairedPath);
                else
                    RecordCreatedFileForRestore(repairedPath, "CK3MPS large verified repaired host save copy: " + repairedPath);

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
                    reason = "large repaired save was created but post-check still scores " + (repaired == null ? 0 : repaired.Score) + "/100";
                    return false;
                }

                reason = appliedRules == null || appliedRules.Count == 0
                    ? "safe game-rule profile injected into large save"
                    : "safe game-rule profile injected into large save: " + String.Join(", ", appliedRules.ToArray());
                return true;
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

        private bool TryRewriteSaveWithLargeScoreRuleRepair(string sourcePath, string tempPath, out List<string> appliedRules, out string failureReason)
        {
            appliedRules = new List<string>();
            failureReason = "";
            long zipOffset;
            bool startsWithZip;
            if (!TryDetectLargeScoreZipLayout(sourcePath, out startsWithZip, out zipOffset, out failureReason))
                return false;

            if (startsWithZip || zipOffset > 0)
                return TryRewriteZipWithLargeScoreRuleRepair(sourcePath, startsWithZip ? 0 : zipOffset, tempPath, out appliedRules, out failureReason);

            string originalText;
            using (FileStream stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                originalText = DecodeUtf8Bounded(ReadStreamBytesBounded(stream, WorkflowLargeSaveRepairFileBytes));

            string updatedText = ApplyBroadCriticalRuleRepairsToText(originalText, out appliedRules);
            if (appliedRules.Count == 0 || String.Equals(originalText, updatedText, StringComparison.Ordinal))
            {
                failureReason = "save text does not expose a repairable game_rules block";
                return false;
            }

            WriteUtf8FileFlushed(tempPath, updatedText);
            return true;
        }

        private bool TryRewriteZipWithLargeScoreRuleRepair(string sourcePath, long zipOffset, string tempPath, out List<string> appliedRules, out string failureReason)
        {
            appliedRules = new List<string>();
            failureReason = "";
            if (String.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                failureReason = "source save is missing";
                return false;
            }

            byte[] prefixBytes;
            byte[] zipBytes;
            try
            {
                using (FileStream input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (zipOffset < 0 || zipOffset >= input.Length)
                    {
                        failureReason = "embedded zip offset is invalid";
                        return false;
                    }

                    if (input.Length > WorkflowLargeSaveRepairFileBytes)
                    {
                        failureReason = "save exceeds the large safe repair size limit";
                        return false;
                    }

                    prefixBytes = zipOffset > 0 ? ReadExactLargeScoreBytes(input, zipOffset) : new byte[0];
                    zipBytes = ReadStreamBytesBounded(input, WorkflowLargeSaveRepairFileBytes);
                }
            }
            catch (Exception ex)
            {
                failureReason = "could not read selected save zip payload: " + ex.Message;
                return false;
            }

            bool changed = false;
            HashSet<string> normalizedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalUncompressedBytes = 0;
            byte[] repairedZipBytes;
            try
            {
                using (MemoryStream sourceZipStream = new MemoryStream(zipBytes, false))
                using (ZipArchive sourceArchive = new ZipArchive(sourceZipStream, ZipArchiveMode.Read, false))
                using (MemoryStream repairedZipStream = new MemoryStream())
                {
                    if (sourceArchive.Entries.Count > WorkflowLargeSaveRepairZipEntryCount)
                    {
                        failureReason = "save zip contains too many entries";
                        return false;
                    }

                    using (ZipArchive destinationArchive = new ZipArchive(repairedZipStream, ZipArchiveMode.Create, true))
                    {
                        foreach (ZipArchiveEntry sourceEntry in sourceArchive.Entries)
                        {
                            string normalizedName;
                            if (!TryValidateLargeScoreRepairZipEntry(sourceEntry, normalizedEntryNames, ref totalUncompressedBytes, out normalizedName, out failureReason))
                                return false;

                            ZipArchiveEntry destinationEntry = destinationArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                            destinationEntry.LastWriteTime = sourceEntry.LastWriteTime;
                            if (String.Equals(normalizedName, "meta", StringComparison.OrdinalIgnoreCase)
                                || String.Equals(normalizedName, "gamestate", StringComparison.OrdinalIgnoreCase))
                            {
                                string originalText = DecodeUtf8Bounded(ReadZipEntryBytesBounded(sourceEntry, WorkflowLargeSaveRepairEntryUncompressedBytes));
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

                    if (!changed)
                    {
                        failureReason = "save zip meta/gamestate entries did not expose a repairable game_rules block";
                        return false;
                    }
                    repairedZipBytes = repairedZipStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                failureReason = "save zip could not be rewritten safely: " + ex.Message;
                return false;
            }

            try
            {
                using (FileStream output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (prefixBytes.Length > 0)
                        output.Write(prefixBytes, 0, prefixBytes.Length);
                    output.Write(repairedZipBytes, 0, repairedZipBytes.Length);
                    output.Flush(true);
                }
                return true;
            }
            catch (Exception ex)
            {
                failureReason = "could not write repaired save copy: " + ex.Message;
                return false;
            }
        }

        private bool TryDetectLargeScoreZipLayout(string sourcePath, out bool startsWithZip, out long zipOffset, out string failureReason)
        {
            startsWithZip = false;
            zipOffset = -1;
            failureReason = "";
            try
            {
                using (FileStream stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] probe = new byte[4];
                    if (stream.Read(probe, 0, probe.Length) == probe.Length && LooksLikeZipArchive(probe, 0))
                    {
                        startsWithZip = true;
                        zipOffset = 0;
                        return true;
                    }

                    stream.Position = 0;
                    zipOffset = FindLargeScoreEmbeddedZipOffset(stream, WorkflowLargeSaveRepairFileBytes);
                    return true;
                }
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return false;
            }
        }

        private int FindLargeScoreEmbeddedZipOffset(Stream stream, long maxBytes)
        {
            if (stream == null || !stream.CanRead)
                return -1;

            byte[] buffer = new byte[64 * 1024];
            int bytesRead;
            int previousCount = 0;
            long scanned = 0;
            while ((bytesRead = stream.Read(buffer, previousCount, buffer.Length - previousCount)) > 0)
            {
                int total = previousCount + bytesRead;
                int start = scanned == 0 ? 4 : 0;
                for (int i = start; i <= total - 4; i++)
                    if (LooksLikeZipArchive(buffer, i))
                        return (int)(scanned - previousCount + i);

                if (total >= 3)
                {
                    buffer[0] = buffer[total - 3];
                    buffer[1] = buffer[total - 2];
                    buffer[2] = buffer[total - 1];
                    previousCount = 3;
                }
                else
                {
                    previousCount = total;
                }

                scanned += bytesRead;
                if (scanned > maxBytes)
                    throw new InvalidOperationException("save exceeds the large safe repair size limit");
            }

            return -1;
        }

        private bool TryValidateLargeScoreRepairZipEntry(ZipArchiveEntry entry, HashSet<string> normalizedEntryNames, ref long totalUncompressedBytes, out string normalizedName, out string failureReason)
        {
            normalizedName = "";
            failureReason = "";
            if (entry == null)
            {
                failureReason = "save zip contains a missing entry";
                return false;
            }

            if (Encoding.UTF8.GetByteCount(entry.FullName ?? "") > WorkflowLargeSaveRepairZipEntryNameBytes)
            {
                failureReason = "save zip contains an entry with an oversized name";
                return false;
            }

            if (!TryNormalizeRepairZipEntryName(entry.FullName, out normalizedName))
            {
                failureReason = "save zip contains a dangerous path entry: " + (entry.FullName ?? "");
                return false;
            }

            if (!normalizedEntryNames.Add(normalizedName))
            {
                failureReason = "save zip contains duplicate normalized entry names";
                return false;
            }

            long compressedLength = entry.CompressedLength;
            long uncompressedLength = entry.Length;
            if (compressedLength < 0 || compressedLength > WorkflowLargeSaveRepairEntryCompressedBytes)
            {
                failureReason = "save zip entry compressed size exceeds the large repair limit";
                return false;
            }

            if (uncompressedLength < 0 || uncompressedLength > WorkflowLargeSaveRepairEntryUncompressedBytes)
            {
                failureReason = "save zip entry uncompressed size exceeds the large repair limit";
                return false;
            }

            totalUncompressedBytes += uncompressedLength;
            if (totalUncompressedBytes > WorkflowLargeSaveRepairTotalUncompressedBytes)
            {
                failureReason = "save zip total uncompressed size exceeds the large repair limit";
                return false;
            }

            if (compressedLength > 0 && uncompressedLength > 0)
            {
                double ratio = (double)uncompressedLength / (double)compressedLength;
                if (ratio > WorkflowLargeSaveRepairCompressionRatio)
                {
                    failureReason = "save zip entry compression ratio exceeds the large repair limit";
                    return false;
                }
            }

            return true;
        }

        private byte[] ReadExactLargeScoreBytes(Stream input, long byteCount)
        {
            if (byteCount < 0 || byteCount > WorkflowLargeSaveRepairFileBytes)
                throw new InvalidOperationException("prefix exceeds the large safe repair size limit");
            byte[] bytes = new byte[(int)byteCount];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = input.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of file while reading save prefix.");
                offset += read;
            }
            return bytes;
        }
    }
}
