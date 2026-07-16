using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private sealed class RestoreRollbackRecord
        {
            public RestoreEntry Entry;
            public string BackupPath = "";
            public bool FileExisted;
            public bool DirectoryExisted;
            public bool Registry;
            public string RegistryValue = "";
        }

        private void ExecuteRestoreBatch(List<RestoreEntry> entries, bool restoreDefault)
        {
            if (entries == null || entries.Count == 0)
                return;

            EnsureStabilizerRoot();
            string transactionRoot = Path.Combine(stabilizerRoot, "restore_transactions", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(transactionRoot);
            string manifest = RestoreManifestFile();
            string manifestBackup = Path.Combine(transactionRoot, "restore_manifest.tsv");
            bool manifestExisted = !String.IsNullOrEmpty(manifest) && File.Exists(manifest);
            if (manifestExisted)
                File.Copy(manifest, manifestBackup, false);

            List<RestoreRollbackRecord> rollbackRecords = new List<RestoreRollbackRecord>();
            try
            {
                for (int index = 0; index < entries.Count; index++)
                {
                    RestoreEntry entry = entries[index];
                    EnsureRestoreOperationStillAllowed(entry, restoreDefault ? "default restore batch" : "restore batch");
                    RestoreRollbackRecord rollback = CaptureRestoreRollbackRecord(entry, transactionRoot, index);
                    rollbackRecords.Add(rollback);

                    if (restoreDefault)
                        RestoreDefaultEntry(entry);
                    else if (entry.Kind == "file" || entry.Kind == "moved_file")
                        RestoreFileEntry(entry);
                    else if (entry.Kind == "created_file")
                        RestoreCreatedFileEntry(entry);
                    else if (entry.Kind == "directory" || entry.Kind == "moved_directory")
                        RestoreDirectoryEntry(entry);
                    else if (entry.Kind == "registry")
                        RestoreRegistryEntry(entry);
                    else
                        throw new InvalidOperationException("This restore entry is informational and cannot be restored directly.");
                }

                DeleteRestoreTransactionDirectory(transactionRoot);
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
        }

        private RestoreRollbackRecord CaptureRestoreRollbackRecord(RestoreEntry entry, string transactionRoot, int index)
        {
            RestoreRollbackRecord record = new RestoreRollbackRecord();
            record.Entry = entry;
            record.Registry = String.Equals(entry.Kind, "registry", StringComparison.OrdinalIgnoreCase);
            if (record.Registry)
            {
                record.RegistryValue = ReadRegistryRestoreValue(entry.SourcePath);
                return record;
            }

            string normalized;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(entry.SourcePath, out normalized))
                throw new InvalidOperationException("Restore rollback path is invalid: " + entry.SourcePath);
            if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(normalized))
                throw new InvalidOperationException("Restore rollback refuses reparse-point paths: " + normalized);

            record.FileExisted = File.Exists(normalized);
            record.DirectoryExisted = Directory.Exists(normalized);
            if (record.FileExisted)
            {
                record.BackupPath = Path.Combine(transactionRoot, index.ToString("D4") + ".file");
                File.Copy(normalized, record.BackupPath, false);
                if (!FileContentsEqual(normalized, record.BackupPath))
                    throw new IOException("Restore rollback file snapshot verification failed: " + normalized);
            }
            else if (record.DirectoryExisted)
            {
                record.BackupPath = Path.Combine(transactionRoot, index.ToString("D4") + ".directory");
                CopyDirectory(normalized, record.BackupPath);
                if (!DirectoryContentsEqual(normalized, record.BackupPath))
                    throw new IOException("Restore rollback directory snapshot verification failed: " + normalized);
            }
            return record;
        }

        private void RollbackRestoreRecord(RestoreRollbackRecord record)
        {
            if (record == null || record.Entry == null)
                return;
            if (record.Registry)
            {
                RestoreRegistrySerializedValue(record.Entry.SourcePath, record.RegistryValue);
                return;
            }

            string target = record.Entry.SourcePath;
            if (record.FileExisted)
            {
                RestoreFileAtomically(record.BackupPath, target);
                return;
            }
            if (record.DirectoryExisted)
            {
                AtomicReplaceDirectoryFromBackup(record.BackupPath, target);
                return;
            }

            if (File.Exists(target))
                File.Delete(target);
            if (Directory.Exists(target))
                Directory.Delete(target, true);
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

        private void DeleteRestoreTransactionDirectory(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;
            if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(path))
                throw new InvalidOperationException("Refusing to delete a restore transaction tree containing a reparse point.");
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
