using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private sealed class RestoreRollbackRecord
        {
            public RestoreEntry Entry;
            public string TargetPath = "";
            public string BackupPath = "";
            public string StagePath = "";
            public string RollbackPath = "";
            public bool FileExisted;
            public bool DirectoryExisted;
            public bool Registry;
            public bool RestoreDefault;
            public bool NoOp;
            public bool Committed;
            public bool OriginalMovedToRollback;
            public bool UsesLegacyDefaultRestore;
            public string RegistryValue = "";
            public string InstalledRegistryValue = "";
            public RestoreOperationPathSnapshot OriginalSnapshot;
            public RestoreOperationPathSnapshot InstalledSnapshot;
        }

        // Test-only fault injection. It is ignored unless CK3MPS_TEST_MODE=1.
        private string restoreTransactionTestFaultPoint = "";
        private int restoreTransactionTestFaultIndex = -1;
        private List<RestoreRollbackRecord> restoreTransactionTestLastRollbackRecords;
        private System.Threading.ManualResetEvent restoreTransactionTestAfterCommitReached;
        private System.Threading.ManualResetEvent restoreTransactionTestContinueAfterCommit;

        private void ExecuteRestoreBatch(List<RestoreEntry> entries, bool restoreDefault)
        {
            if (entries == null || entries.Count == 0)
                return;

            EnsureStabilizerRoot();
            string transactionParent = Path.Combine(stabilizerRoot, "restore_transactions");
            string transactionRoot = Path.Combine(transactionParent, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
            EnsureRestoreTransactionPath(transactionRoot, stabilizerRoot);
            Directory.CreateDirectory(transactionRoot);

            string manifest = RestoreManifestFile();
            string manifestBackup = Path.Combine(transactionRoot, "restore_manifest.tsv");
            bool manifestExisted = !String.IsNullOrEmpty(manifest) && File.Exists(manifest);
            if (manifestExisted)
            {
                try
                {
                    File.Copy(manifest, manifestBackup, false);
                    if (!FileContentsEqual(manifest, manifestBackup))
                        throw new IOException("Restore manifest transaction snapshot verification failed.");
                }
                catch
                {
                    DeleteRestoreTransactionDirectory(transactionRoot);
                    throw;
                }
            }

            List<RestoreRollbackRecord> rollbackRecords = new List<RestoreRollbackRecord>();
            if (String.Equals(Environment.GetEnvironmentVariable("CK3MPS_TEST_MODE"), "1", StringComparison.Ordinal))
                restoreTransactionTestLastRollbackRecords = null;
            try
            {
                for (int index = 0; index < entries.Count; index++)
                {
                    RestoreEntry entry = entries[index];
                    EnsureRestoreTransactionOperationStillAllowed(entry, restoreDefault ? "default restore batch" : "restore batch");

                    RestoreRollbackRecord rollback = PrepareRestoreRollbackRecord(entry, transactionRoot, index, restoreDefault);
                    rollbackRecords.Add(rollback);
                    if (rollback.NoOp)
                        continue;

                    EnsureRestoreTransactionOperationStillAllowed(entry, restoreDefault ? "default restore commit" : "restore commit");
                    CommitRestoreRecord(rollback, index);
                    RecordRestoreTransactionResult(rollback, index);
                    FinalizeRestoreRecord(rollback);
                }
            }
            catch (Exception operationError)
            {
                List<Exception> rollbackErrors = new List<Exception>();
                for (int index = rollbackRecords.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        RollbackRestoreRecord(rollbackRecords[index]);
                    }
                    catch (Exception rollbackError)
                    {
                        rollbackErrors.Add(rollbackError);
                    }
                }

                try
                {
                    RestoreManifestSnapshot(manifest, manifestBackup, manifestExisted);
                }
                catch (Exception manifestError)
                {
                    rollbackErrors.Add(manifestError);
                }

                if (String.Equals(Environment.GetEnvironmentVariable("CK3MPS_TEST_MODE"), "1", StringComparison.Ordinal))
                    restoreTransactionTestLastRollbackRecords = rollbackRecords;

                if (rollbackErrors.Count == 0)
                {
                    DeleteRestoreTransactionDirectory(transactionRoot);
                    throw new InvalidOperationException("Restore batch failed and every applied item was rolled back. " + operationError.Message, operationError);
                }

                StringBuilder details = new StringBuilder();
                details.Append("Restore batch failed: ").Append(operationError.Message);
                details.Append(" Rollback also reported ").Append(rollbackErrors.Count).Append(" error(s): ");
                foreach (Exception rollbackError in rollbackErrors)
                    details.Append(rollbackError.Message).Append("; ");
                details.Append("Recovery data was preserved at ").Append(transactionRoot);
                throw new InvalidOperationException(details.ToString(), operationError);
            }

            DeleteRestoreTransactionDirectory(transactionRoot);
        }

        private RestoreRollbackRecord PrepareRestoreRollbackRecord(RestoreEntry entry, string transactionRoot, int index, bool restoreDefault)
        {
            RestoreRollbackRecord record = new RestoreRollbackRecord();
            record.Entry = entry;
            record.RestoreDefault = restoreDefault;
            record.Registry = String.Equals(entry.Kind, "registry", StringComparison.OrdinalIgnoreCase);

            if (record.Registry)
            {
                record.RegistryValue = ReadRegistryRestoreValue(entry.SourcePath);
                record.OriginalSnapshot = CaptureRestoreTransactionSnapshot(entry.SourcePath, true);
                string desired = restoreDefault || String.IsNullOrEmpty(entry.Before) || String.Equals(entry.Before, "(missing)", StringComparison.Ordinal)
                    ? "(missing)"
                    : entry.Before;
                record.InstalledRegistryValue = desired;
                record.NoOp = String.Equals(record.RegistryValue, desired, StringComparison.Ordinal);
                return record;
            }

            string normalized;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(entry.SourcePath, out normalized))
                throw new InvalidOperationException("Restore rollback path is invalid: " + entry.SourcePath);
            if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(normalized))
                throw new InvalidOperationException("Restore rollback refuses reparse-point paths: " + normalized);

            record.TargetPath = normalized;
            record.FileExisted = File.Exists(normalized);
            record.DirectoryExisted = Directory.Exists(normalized);
            record.OriginalSnapshot = CaptureRestoreTransactionSnapshot(normalized, false);

            if (record.FileExisted)
            {
                record.BackupPath = Path.Combine(transactionRoot, index.ToString("D4") + ".file");
                File.Copy(normalized, record.BackupPath, false);
                if (!FileContentsEqual(normalized, record.BackupPath))
                    throw new IOException("Restore rollback file snapshot verification failed: " + normalized);
            }
            else if (record.DirectoryExisted)
            {
                EnsureDirectoryTreeHasNoReparsePoints(normalized, "Restore target snapshot");
                record.BackupPath = Path.Combine(transactionRoot, index.ToString("D4") + ".directory");
                CopyDirectory(normalized, record.BackupPath);
                EnsureDirectoryTreeHasNoReparsePoints(record.BackupPath, "Restore rollback snapshot");
                if (!DirectoryContentsEqual(normalized, record.BackupPath))
                    throw new IOException("Restore rollback directory snapshot verification failed: " + normalized);
            }

            if (restoreDefault && (IsSteamLocalConfigEntry(entry) || IsSteamSharedConfigEntry(entry)))
            {
                record.UsesLegacyDefaultRestore = true;
                record.NoOp = !record.FileExisted && !record.DirectoryExisted;
                return record;
            }

            string kind = NullText(entry.Kind).Trim().ToLowerInvariant();
            bool deleteTarget = restoreDefault || kind == "created_file";
            if (deleteTarget)
            {
                record.NoOp = !record.FileExisted && !record.DirectoryExisted;
                return record;
            }

            string parent = Path.GetDirectoryName(normalized);
            if (String.IsNullOrEmpty(parent))
                throw new InvalidOperationException("Restore target has no parent: " + normalized);
            Directory.CreateDirectory(parent);

            if (kind == "file" || kind == "moved_file")
            {
                string backupPath = entry.BackupPath;
                if (!File.Exists(backupPath))
                    throw new FileNotFoundException("Backup file is missing.", backupPath);
                if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(backupPath))
                    throw new InvalidOperationException("Restore backup uses a reparse point: " + backupPath);
                if (record.FileExisted && FileContentsEqual(normalized, backupPath))
                {
                    record.NoOp = true;
                    return record;
                }
                if (kind == "moved_file" && record.FileExisted)
                    throw new IOException("A file already exists at the moved-file restore target. Restore refused to overwrite user data: " + normalized);

                string temp = Path.Combine(parent, Path.GetFileName(normalized) + ".restore-" + Guid.NewGuid().ToString("N") + ".tmp");
                record.StagePath = temp;
                try
                {
                    File.Copy(backupPath, temp, false);
                    InjectRestoreTransactionFault("staging-copy", index);
                    if (!FileContentsEqual(backupPath, temp))
                        throw new IOException("Restore staging copy does not match the recorded backup.");
                    return record;
                }
                catch
                {
                    SafeAtomicFile.TryDeleteTempFile(temp);
                    record.StagePath = "";
                    throw;
                }
            }

            if (kind == "directory" || kind == "moved_directory")
            {
                string backupPath = entry.BackupPath;
                if (!Directory.Exists(backupPath))
                    throw new DirectoryNotFoundException("Backup directory is missing: " + backupPath);
                EnsureDirectoryTreeHasNoReparsePoints(backupPath, "Restore backup");
                if (record.DirectoryExisted && DirectoryContentsEqual(normalized, backupPath))
                {
                    record.NoOp = true;
                    return record;
                }
                if (kind == "moved_directory" && record.DirectoryExisted)
                    throw new IOException("A directory already exists at the moved-directory restore target. Restore refused to overwrite user data: " + normalized);

                string suffix = Guid.NewGuid().ToString("N");
                string stage = Path.Combine(parent, ".ck3mps-restore-stage-" + suffix);
                string rollback = Path.Combine(parent, ".ck3mps-restore-rollback-" + suffix);
                record.StagePath = stage;
                record.RollbackPath = rollback;
                try
                {
                    CopyDirectory(backupPath, stage);
                    InjectRestoreTransactionFault("staging-copy", index);
                    EnsureDirectoryTreeHasNoReparsePoints(stage, "Restore staging directory");
                    if (!DirectoryContentsEqual(backupPath, stage))
                        throw new IOException("Directory restore staging verification failed.");
                    return record;
                }
                catch
                {
                    if (Directory.Exists(stage))
                        Directory.Delete(stage, true);
                    record.StagePath = "";
                    record.RollbackPath = "";
                    throw;
                }
            }

            throw new InvalidOperationException("This restore entry is informational and cannot be restored directly.");
        }

        private void CommitRestoreRecord(RestoreRollbackRecord record, int index)
        {
            if (record == null || record.Entry == null || record.NoOp)
                return;

            if (record.Registry)
            {
                RestoreRegistrySerializedValue(record.Entry.SourcePath, record.InstalledRegistryValue);
                record.Committed = true;
                record.InstalledSnapshot = CaptureRestoreTransactionSnapshot(record.Entry.SourcePath, true);
                if (!String.Equals(record.InstalledSnapshot.RegistryValue, record.InstalledRegistryValue, StringComparison.Ordinal))
                    throw new IOException("Registry restore commit verification failed: " + record.Entry.SourcePath);
                InjectRestoreTransactionFault("after-new-target", index);
                return;
            }

            if (record.UsesLegacyDefaultRestore)
            {
                record.Committed = true;
                try
                {
                    RestoreDefaultEntry(record.Entry);
                    record.InstalledSnapshot = CaptureRestoreTransactionSnapshot(record.TargetPath, false);
                }
                catch
                {
                    record.InstalledSnapshot = CaptureRestoreTransactionSnapshot(record.TargetPath, false);
                    throw;
                }
                InjectRestoreTransactionFault("after-new-target", index);
                return;
            }

            string kind = NullText(record.Entry.Kind).Trim().ToLowerInvariant();
            bool deleteTarget = record.RestoreDefault || kind == "created_file";
            if (deleteTarget)
            {
                InjectRestoreTransactionFault("before-rename", index);
                if (record.DirectoryExisted)
                {
                    string target = record.TargetPath;
                    string rollback = Path.Combine(Path.GetDirectoryName(target), ".ck3mps-restore-rollback-" + Guid.NewGuid().ToString("N"));
                    record.RollbackPath = rollback;
                    Directory.Move(target, rollback);
                    record.OriginalMovedToRollback = true;
                    record.Committed = true;
                    record.InstalledSnapshot = CaptureRestoreTransactionSnapshot(target, false);
                    InjectRestoreTransactionFault("after-target-to-rollback", index);
                }
                else if (record.FileExisted)
                {
                    string target = record.TargetPath;
                    File.Delete(target);
                    record.Committed = true;
                    record.InstalledSnapshot = CaptureRestoreTransactionSnapshot(target, false);
                    InjectRestoreTransactionFault("after-new-target", index);
                }
                return;
            }

            if (kind == "file" || kind == "moved_file")
            {
                record.InstalledSnapshot = CaptureRestoreTransactionSnapshot(record.StagePath, false);
                record.InstalledSnapshot.NormalizedPath = record.TargetPath;
                InjectRestoreTransactionFault("before-rename", index);
                record.Committed = true;
                SafeAtomicFile.ReplaceFile(record.StagePath, record.TargetPath);
                record.StagePath = "";
                RestoreOperationPathSnapshot committedSnapshot = CaptureRestoreTransactionSnapshot(record.TargetPath, false);
                if (!RestoreTransactionSnapshotsEqual(committedSnapshot, record.InstalledSnapshot)
                    || !FileContentsEqual(record.Entry.BackupPath, record.TargetPath))
                    throw new IOException("File restore commit verification failed.");
                InjectRestoreTransactionFault("after-new-target", index);
                return;
            }

            if (kind == "directory" || kind == "moved_directory")
            {
                string target = record.TargetPath;
                string stage = record.StagePath;
                string rollback = record.RollbackPath;
                InjectRestoreTransactionFault("before-rename", index);
                if (Directory.Exists(target))
                {
                    Directory.Move(target, rollback);
                    record.OriginalMovedToRollback = true;
                    InjectRestoreTransactionFault("after-target-to-rollback", index);
                }

                record.InstalledSnapshot = CaptureRestoreTransactionSnapshot(stage, false);
                record.InstalledSnapshot.NormalizedPath = record.TargetPath;
                record.Committed = true;
                Directory.Move(stage, target);
                record.StagePath = "";
                EnsureDirectoryTreeHasNoReparsePoints(target, "Committed restore target");
                RestoreOperationPathSnapshot committedSnapshot = CaptureRestoreTransactionSnapshot(target, false);
                if (!RestoreTransactionSnapshotsEqual(committedSnapshot, record.InstalledSnapshot)
                    || !DirectoryContentsEqual(record.Entry.BackupPath, target))
                    throw new IOException("Directory restore commit verification failed.");
                InjectRestoreTransactionFault("after-new-target", index);
                return;
            }

            throw new InvalidOperationException("Unsupported restore transaction kind: " + record.Entry.Kind);
        }

        private void RecordRestoreTransactionResult(RestoreRollbackRecord record, int index)
        {
            if (record == null || record.Entry == null || record.NoOp || record.UsesLegacyDefaultRestore)
                return;

            InjectRestoreTransactionFault("manifest-log", index);
            RestoreEntry entry = record.Entry;
            string kind = NullText(entry.Kind).Trim().ToLowerInvariant();
            if (record.RestoreDefault)
            {
                RecordRestoreEntry("default_restore", entry.SourcePath, "", "Default restore: removed CK3/Launcher override so the owner can recreate defaults: " + entry.SourcePath, DescribeRestoreSnapshot(record.OriginalSnapshot), "(missing/default)", "restored_default");
                return;
            }

            if (kind == "registry")
            {
                RecordRestoreEntry("restore_action", entry.SourcePath, "", "Restored registry value: " + entry.SourcePath, record.RegistryValue, ReadRegistryRestoreValue(entry.SourcePath), "restored");
                return;
            }
            if (kind == "created_file")
            {
                RecordRestoreEntry("restore_action", entry.SourcePath, "", "Deleted CK3MPS-created file: " + entry.SourcePath, DescribeRestoreSnapshot(record.OriginalSnapshot), "(missing)", "restored");
                return;
            }
            if (kind == "directory" || kind == "moved_directory")
            {
                RecordRestoreEntry("restore_action", entry.SourcePath, entry.BackupPath, "Restored directory: " + entry.SourcePath, entry.Before, DescribePath(entry.SourcePath), "restored");
                return;
            }

            RecordRestoreEntry("restore_action", entry.SourcePath, entry.BackupPath, "Restored file: " + entry.SourcePath, entry.Before, DescribePath(entry.SourcePath), "restored");
        }

        private void FinalizeRestoreRecord(RestoreRollbackRecord record)
        {
            if (record == null)
                return;
            if (!String.IsNullOrEmpty(record.RollbackPath) && Directory.Exists(record.RollbackPath))
            {
                string rollback = record.RollbackPath;
                Directory.Delete(rollback, true);
                record.RollbackPath = "";
            }
        }

        private void RollbackRestoreRecord(RestoreRollbackRecord record)
        {
            if (record == null || record.Entry == null || record.NoOp)
                return;

            try
            {
                if (record.Registry)
                {
                    string current = ReadRegistryRestoreValue(record.Entry.SourcePath);
                    if (String.Equals(current, record.RegistryValue, StringComparison.Ordinal))
                        return;
                    if (record.Committed && !String.Equals(current, record.InstalledRegistryValue, StringComparison.Ordinal))
                        throw new IOException("Registry target changed after restore commit; automatic rollback refused: " + record.Entry.SourcePath);
                    RestoreRegistrySerializedValue(record.Entry.SourcePath, record.RegistryValue);
                    if (!String.Equals(ReadRegistryRestoreValue(record.Entry.SourcePath), record.RegistryValue, StringComparison.Ordinal))
                        throw new IOException("Registry rollback verification failed: " + record.Entry.SourcePath);
                    return;
                }

                string target = record.TargetPath;
                RestoreOperationPathSnapshot currentSnapshot = CaptureRestoreTransactionSnapshot(target, false);
                if (RestoreTransactionSnapshotsEqual(currentSnapshot, record.OriginalSnapshot))
                    return;

                if (record.OriginalMovedToRollback && !String.IsNullOrEmpty(record.RollbackPath) && Directory.Exists(record.RollbackPath))
                {
                    if (currentSnapshot.FileExists || currentSnapshot.DirectoryExists)
                    {
                        if (record.InstalledSnapshot == null || !RestoreTransactionSnapshotsEqual(currentSnapshot, record.InstalledSnapshot))
                            throw new IOException("Restore target changed after commit; automatic directory rollback refused: " + target);
                        if (Directory.Exists(target))
                            Directory.Delete(target, true);
                        else if (File.Exists(target))
                            File.Delete(target);
                    }

                    string rollback = record.RollbackPath;
                    Directory.Move(rollback, target);
                    record.RollbackPath = "";
                    if (!RestoreTransactionSnapshotsEqual(CaptureRestoreTransactionSnapshot(target, false), record.OriginalSnapshot))
                        throw new IOException("Directory rollback verification failed: " + target);
                    return;
                }

                if (record.FileExisted)
                {
                    if (currentSnapshot.FileExists || currentSnapshot.DirectoryExists)
                    {
                        if (record.InstalledSnapshot == null || !RestoreTransactionSnapshotsEqual(currentSnapshot, record.InstalledSnapshot))
                            throw new IOException("Restore target changed after commit; automatic file rollback refused: " + target);
                    }
                    RestoreFileAtomically(record.BackupPath, target);
                    if (!FileContentsEqual(record.BackupPath, target))
                        throw new IOException("File rollback verification failed: " + target);
                    return;
                }

                if (record.DirectoryExisted)
                {
                    if (currentSnapshot.FileExists || currentSnapshot.DirectoryExists)
                    {
                        if (record.InstalledSnapshot == null || !RestoreTransactionSnapshotsEqual(currentSnapshot, record.InstalledSnapshot))
                            throw new IOException("Restore target changed after commit; automatic directory rollback refused: " + target);
                    }
                    AtomicReplaceDirectoryFromBackup(record.BackupPath, target);
                    if (!DirectoryContentsEqual(record.BackupPath, target))
                        throw new IOException("Directory rollback verification failed: " + target);
                    return;
                }

                if (!currentSnapshot.FileExists && !currentSnapshot.DirectoryExists)
                    return;
                if (record.InstalledSnapshot == null || !RestoreTransactionSnapshotsEqual(currentSnapshot, record.InstalledSnapshot))
                    throw new IOException("Restore target changed after commit; automatic cleanup refused: " + target);
                if (File.Exists(target))
                    File.Delete(target);
                if (Directory.Exists(target))
                    Directory.Delete(target, true);
            }
            finally
            {
                CleanupRestoreRecordTemps(record);
            }
        }

        private void RestoreManifestSnapshot(string manifest, string manifestBackup, bool manifestExisted)
        {
            if (String.IsNullOrEmpty(manifest))
                return;
            if (!manifestExisted)
            {
                if (File.Exists(manifest))
                    File.Delete(manifest);
                return;
            }
            RestoreFileAtomically(manifestBackup, manifest);
            if (!FileContentsEqual(manifestBackup, manifest))
                throw new IOException("Restore manifest rollback verification failed.");
        }

        private void RestoreFileAtomically(string backupPath, string targetPath)
        {
            if (!File.Exists(backupPath))
                throw new FileNotFoundException("Rollback file snapshot is missing.", backupPath);
            string directory = Path.GetDirectoryName(targetPath);
            if (String.IsNullOrEmpty(directory))
                throw new InvalidOperationException("Rollback target directory is missing: " + targetPath);
            Directory.CreateDirectory(directory);
            string temp = Path.Combine(directory, Path.GetFileName(targetPath) + ".restore-rollback-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(backupPath, temp, false);
                if (!FileContentsEqual(backupPath, temp))
                    throw new IOException("Rollback staging file does not match its snapshot.");
                SafeAtomicFile.ReplaceFile(temp, targetPath);
            }
            finally
            {
                SafeAtomicFile.TryDeleteTempFile(temp);
            }
        }

        private void AtomicReplaceDirectoryFromBackup(string backupPath, string targetPath)
        {
            string backup;
            string target;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(backupPath, out backup)
                || !PathContainmentUtilities.TryNormalizeAbsolutePath(targetPath, out target))
                throw new InvalidOperationException("Directory restore paths are invalid.");
            if (!Directory.Exists(backup))
                throw new DirectoryNotFoundException("Backup directory is missing: " + backup);
            if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(backup)
                || PathContainmentUtilities.ContainsReparsePointInExistingSegments(target))
                throw new InvalidOperationException("Directory restore refuses reparse-point paths.");
            EnsureDirectoryTreeHasNoReparsePoints(backup, "Directory rollback backup");

            string parent = Path.GetDirectoryName(target);
            if (String.IsNullOrEmpty(parent))
                throw new InvalidOperationException("Directory restore target has no parent: " + target);
            Directory.CreateDirectory(parent);

            string suffix = Guid.NewGuid().ToString("N");
            string stage = Path.Combine(parent, ".ck3mps-restore-stage-" + suffix);
            string rollback = Path.Combine(parent, ".ck3mps-restore-rollback-" + suffix);
            bool originalMoved = false;
            bool committed = false;
            try
            {
                CopyDirectory(backup, stage);
                EnsureDirectoryTreeHasNoReparsePoints(stage, "Directory rollback staging");
                if (!DirectoryContentsEqual(backup, stage))
                    throw new IOException("Directory restore staging verification failed.");

                if (Directory.Exists(target))
                {
                    Directory.Move(target, rollback);
                    originalMoved = true;
                }

                Directory.Move(stage, target);
                if (!DirectoryContentsEqual(backup, target))
                    throw new IOException("Directory restore commit verification failed.");
                committed = true;

                if (Directory.Exists(rollback))
                    Directory.Delete(rollback, true);
            }
            catch
            {
                if (Directory.Exists(target) && (!committed || originalMoved))
                    Directory.Delete(target, true);
                if (originalMoved && Directory.Exists(rollback) && !Directory.Exists(target))
                    Directory.Move(rollback, target);
                throw;
            }
            finally
            {
                if (Directory.Exists(stage))
                    Directory.Delete(stage, true);
                if (committed && Directory.Exists(rollback))
                    Directory.Delete(rollback, true);
            }
        }

        private void RestoreRegistrySerializedValue(string path, string serialized)
        {
            RegistryKey root;
            string subKey;
            string name;
            ParseRegistryRestorePath(path, out root, out subKey, out name);
            if (String.IsNullOrEmpty(serialized) || String.Equals(serialized, "(missing)", StringComparison.Ordinal))
            {
                using (RegistryKey key = root.OpenSubKey(subKey, true))
                    if (key != null)
                        key.DeleteValue(name, false);
                return;
            }

            int colon = serialized.IndexOf(':');
            if (colon <= 0)
                throw new InvalidOperationException("Registry rollback snapshot format is invalid: " + path);
            RegistryValueKind kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), serialized.Substring(0, colon));
            object value = ParseRegistryValue(serialized.Substring(colon + 1), kind);
            using (RegistryKey key = root.CreateSubKey(subKey))
            {
                if (key == null)
                    throw new InvalidOperationException("Registry rollback key could not be opened: " + path);
                key.SetValue(name, value, kind);
            }
        }

        private RestoreOperationPathSnapshot CaptureRestoreTransactionSnapshot(string path, bool registry)
        {
            RestoreOperationPathSnapshot snapshot = CaptureRestoreOperationPathSnapshot(path, registry);
            if (snapshot.DirectoryExists)
            {
                EnsureDirectoryTreeHasNoReparsePoints(snapshot.NormalizedPath, "Restore snapshot");
                snapshot.Sha256 = ComputeDirectorySnapshotHash(snapshot.NormalizedPath);
            }
            return snapshot;
        }

        private bool RestoreTransactionSnapshotsEqual(RestoreOperationPathSnapshot left, RestoreOperationPathSnapshot right)
        {
            if (left == null || right == null)
                return left == right;
            if (!String.Equals(left.NormalizedPath, right.NormalizedPath, StringComparison.OrdinalIgnoreCase)
                || left.FileExists != right.FileExists
                || left.DirectoryExists != right.DirectoryExists
                || left.HasReparsePoint != right.HasReparsePoint)
                return false;
            if (!String.Equals(left.RegistryValue, right.RegistryValue, StringComparison.Ordinal))
                return false;
            if (left.FileExists)
            {
                return left.Length == right.Length
                    && left.Attributes == right.Attributes
                    && String.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            if (left.DirectoryExists)
            {
                return left.Attributes == right.Attributes
                    && String.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        private string ComputeDirectorySnapshotHash(string root)
        {
            Dictionary<string, bool> entries = EnumerateDirectoryEntriesBounded(root);
            if (entries == null)
                throw new IOException("Directory snapshot exceeded bounded traversal limits: " + root);
            List<string> paths = new List<string>(entries.Keys);
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            StringBuilder canonical = new StringBuilder();
            foreach (string relative in paths)
            {
                bool isFile = entries[relative];
                canonical.Append(isFile ? "F|" : "D|").Append(relative).Append('|');
                if (isFile)
                {
                    string file = Path.Combine(root, relative);
                    canonical.Append(new FileInfo(file).Length).Append('|').Append(FileHashOrMissing(file));
                }
                canonical.Append('\n');
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(canonical.ToString());
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder result = new StringBuilder();
                foreach (byte value in hash)
                    result.Append(value.ToString("x2"));
                return result.ToString();
            }
        }

        private void EnsureDirectoryTreeHasNoReparsePoints(string root, string operation)
        {
            string normalized;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(root, out normalized) || !Directory.Exists(normalized))
                throw new DirectoryNotFoundException(operation + " directory is missing: " + root);
            if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(normalized))
                throw new InvalidOperationException(operation + " refuses a reparse-point path: " + normalized);

            Stack<string> pending = new Stack<string>();
            pending.Push(normalized);
            int visited = 0;
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                if (++visited > MaxBoundedTraversalDirectories)
                    throw new IOException(operation + " exceeded bounded traversal limits.");
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException(operation + " refuses reparse points: " + current);
                foreach (string child in Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly))
                    pending.Push(child);
                foreach (string file in Directory.GetFiles(current, "*", SearchOption.TopDirectoryOnly))
                {
                    attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException(operation + " refuses reparse points: " + file);
                }
            }
        }

        private void EnsureRestoreTransactionPath(string path, string allowedRoot)
        {
            string normalizedPath;
            string normalizedRoot;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(path, out normalizedPath)
                || !PathContainmentUtilities.TryNormalizeAbsolutePath(allowedRoot, out normalizedRoot)
                || !PathContainmentUtilities.IsWithinRoot(normalizedRoot, normalizedPath))
                throw new InvalidOperationException("Restore transaction path is outside the stabilizer root.");
            if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(normalizedPath))
                throw new InvalidOperationException("Restore transaction path uses a reparse point.");
        }

        private void EnsureRestoreTransactionOperationStillAllowed(RestoreEntry entry, string operation)
        {
            if (IsRestoreTransactionTestRegistryEntry(entry))
            {
                EnsureRestoreOperationSnapshotUnchanged(entry, operation);
                return;
            }
            if (IsRestoreTransactionMissingDirectoryEntry(entry))
            {
                ValidateRestoreTransactionMissingDirectoryEntry(entry, operation);
                EnsureRestoreOperationSnapshotUnchanged(entry, operation);
                return;
            }
            EnsureRestoreOperationStillAllowed(entry, operation);
        }

        private bool IsRestoreTransactionMissingDirectoryEntry(RestoreEntry entry)
        {
            if (entry == null)
                return false;
            string kind = NullText(entry.Kind).Trim().ToLowerInvariant();
            if (kind != "directory" && kind != "moved_directory")
                return false;
            return !File.Exists(entry.SourcePath) && !Directory.Exists(entry.SourcePath);
        }

        private void ValidateRestoreTransactionMissingDirectoryEntry(RestoreEntry entry, string operation)
        {
            string source;
            string backup;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(entry.SourcePath, out source)
                || !PathContainmentUtilities.TryNormalizeAbsolutePath(entry.BackupPath, out backup))
                throw new InvalidOperationException("Blocked " + operation + ": directory restore paths are malformed.");
            if (!Directory.Exists(backup)
                || !PathContainmentUtilities.IsWithinRoot(RestoreBackupRoot(), backup)
                || PathContainmentUtilities.ContainsReparsePointInExistingSegments(source)
                || PathContainmentUtilities.ContainsReparsePointInExistingSegments(backup))
                throw new InvalidOperationException("Blocked " + operation + ": missing-directory restore paths are not trusted.");
            EnsureDirectoryTreeHasNoReparsePoints(backup, "Missing-directory restore backup");

            string docs = Ck3PathUtilities.NormalizeDirectoryPath(ck3Docs);
            string parent = Ck3PathUtilities.NormalizeDirectoryPath(Path.GetDirectoryName(source));
            string name = Path.GetFileName(source);
            bool allowed = String.Equals(parent, docs, StringComparison.OrdinalIgnoreCase) && IsKnownRestoreDirectoryName(name);
            if (!allowed)
            {
                string local = Ck3PathUtilities.NormalizeDirectoryPath(LocalLauncherRoot());
                string roaming = Ck3PathUtilities.NormalizeDirectoryPath(RoamingLauncherRoot());
                allowed = (String.Equals(parent, local, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(parent, roaming, StringComparison.OrdinalIgnoreCase))
                    && IsKnownLauncherRestoreDirectoryName(name);
            }
            if (!allowed)
                throw new InvalidOperationException("Blocked " + operation + ": missing directory is outside the CK3/Paradox allowlist.");
        }

        private bool IsKnownRestoreDirectoryName(string name)
        {
            string value = NullText(name);
            return String.Equals(value, "player", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "shadercache", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "logs", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "newsfeed", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "oos", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "crashes", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "dumps", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "exceptions", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "playsets_backup", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("oos_archive_", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsKnownLauncherRestoreDirectoryName(string name)
        {
            string value = NullText(name);
            return String.Equals(value, "Cache", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "GPUCache", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "DawnGraphiteCache", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "DawnWebGPUCache", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "logs", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "game-metadata", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "telemetry-whitelist-cache", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsRestoreTransactionTestRegistryEntry(RestoreEntry entry)
        {
            if (!String.Equals(Environment.GetEnvironmentVariable("CK3MPS_TEST_MODE"), "1", StringComparison.Ordinal))
                return false;
            if (entry == null || !String.Equals(entry.Kind, "registry", StringComparison.OrdinalIgnoreCase))
                return false;
            return NullText(entry.SourcePath).StartsWith(@"HKCU\Software\CK3MPS\Tests\", StringComparison.OrdinalIgnoreCase);
        }

        private void InjectRestoreTransactionFault(string point, int index)
        {
            if (!String.Equals(Environment.GetEnvironmentVariable("CK3MPS_TEST_MODE"), "1", StringComparison.Ordinal))
                return;
            if (!String.Equals(restoreTransactionTestFaultPoint, point, StringComparison.OrdinalIgnoreCase)
                && !(String.Equals(restoreTransactionTestFaultPoint, "after-new-target-pause", StringComparison.OrdinalIgnoreCase)
                    && String.Equals(point, "after-new-target", StringComparison.OrdinalIgnoreCase)))
                return;
            if (restoreTransactionTestFaultIndex >= 0 && restoreTransactionTestFaultIndex != index)
                return;
            if (String.Equals(restoreTransactionTestFaultPoint, "after-new-target-pause", StringComparison.OrdinalIgnoreCase))
            {
                if (restoreTransactionTestAfterCommitReached != null)
                    restoreTransactionTestAfterCommitReached.Set();
                if (restoreTransactionTestContinueAfterCommit == null || !restoreTransactionTestContinueAfterCommit.WaitOne(10000))
                    throw new IOException("Timed out waiting for the restore transaction test continuation signal.");
            }
            throw new IOException("Injected restore transaction fault at " + restoreTransactionTestFaultPoint + " for item " + index + ".");
        }

        private string DescribeRestoreSnapshot(RestoreOperationPathSnapshot snapshot)
        {
            if (snapshot == null || (!snapshot.FileExists && !snapshot.DirectoryExists))
                return "(missing)";
            if (snapshot.FileExists)
                return "file | bytes=" + snapshot.Length + " | sha256=" + NullText(snapshot.Sha256);
            return "directory | sha256=" + NullText(snapshot.Sha256);
        }

        private void CleanupRestoreRecordTemps(RestoreRollbackRecord record)
        {
            if (record == null)
                return;
            if (!String.IsNullOrEmpty(record.StagePath))
            {
                string stage = record.StagePath;
                if (File.Exists(stage))
                    SafeAtomicFile.TryDeleteTempFile(stage);
                if (Directory.Exists(stage))
                    Directory.Delete(stage, true);
                record.StagePath = "";
            }
        }

        private void DeleteRestoreTransactionDirectory(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;
            EnsureRestoreTransactionPath(path, stabilizerRoot);
            EnsureDirectoryTreeHasNoReparsePoints(path, "Restore transaction cleanup");
            Directory.Delete(path, true);
            string parent = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(parent)
                && Directory.Exists(parent)
                && Directory.GetFiles(parent).Length == 0
                && Directory.GetDirectories(parent).Length == 0)
                Directory.Delete(parent, false);
        }
    }
}
