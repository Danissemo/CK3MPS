using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private sealed class RestoreEntry
        {
            public string Id;
            public string Kind;
            public string SourcePath;
            public string BackupPath;
            public string Description;
            public string Before;
            public string After;
            public string Created;
            public string Status;
            public string RunId;
            public string DisplayText;
            public string ValidationError;

            public override string ToString()
            {
                return DisplayText ?? Description ?? Id ?? "";
            }
        }

        private sealed class RestoreOperationPathSnapshot
        {
            public string NormalizedPath;
            public bool FileExists;
            public bool DirectoryExists;
            public long Length;
            public DateTime LastWriteUtc;
            public FileAttributes Attributes;
            public bool HasReparsePoint;
            public string RegistryValue;
            public string Sha256;
        }

        private Dictionary<string, RestoreOperationPathSnapshot> activeRestoreOperationSnapshots;

        private string RestoreManifestFile()
        {
            if (String.IsNullOrEmpty(lastQuarantine) || !Directory.Exists(lastQuarantine))
            {
                string known = FindLatestQuarantine();
                if (!String.IsNullOrEmpty(known) && Directory.Exists(known))
                    lastQuarantine = known;
            }
            return String.IsNullOrEmpty(lastQuarantine) ? "" : Path.Combine(lastQuarantine, "restore_manifest.tsv");
        }

        private string RestoreBackupRoot()
        {
            return Path.Combine(lastQuarantine, "restore_backups");
        }

        private void InitializeRestoreManifest()
        {
            if (String.IsNullOrEmpty(lastQuarantine))
                return;

            Directory.CreateDirectory(RestoreBackupRoot());
            string manifest = RestoreManifestFile();
            if (!File.Exists(manifest))
                SafeAtomicFile.WriteAllText(manifest, "id\tcreated\tkind\tsource\tbackup\tdescription\tbefore\tafter\tstatus\trun_id\r\n", Encoding.UTF8);

            CapturePreChangeSnapshot();
            RefreshRestoreList();
        }

        private void CapturePreChangeSnapshot()
        {
            try
            {
                string snapshot = Path.Combine(lastQuarantine, "restore_pre_change_snapshot.txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("CK3MPS pre-change snapshot");
                sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("App version: " + AppVersion);
                sb.AppendLine("CK3 settings/saves folder: " + NullText(ck3Docs));
                sb.AppendLine("CK3 game folder: " + NullText(ck3Install));
                sb.AppendLine("Steam localconfig: " + FileTimeHashLine(localConfig));
                sb.AppendLine("Steam sharedconfig: " + FileTimeHashLine(sharedConfig));
                sb.AppendLine("Steam appmanifest: " + FileTimeHashLine(appManifest));
                sb.AppendLine("dlc_load.json: " + FileTimeHashLine(Path.Combine(ck3Docs, "dlc_load.json")));
                sb.AppendLine("pdx_settings.txt: " + FileTimeHashLine(Path.Combine(ck3Docs, "pdx_settings.txt")));
                sb.AppendLine("launcher-v2.sqlite: " + FileTimeHashLine(Path.Combine(ck3Docs, "launcher-v2.sqlite")));
                sb.AppendLine("save games items: " + CountItems(Path.Combine(ck3Docs, "save games")));
                sb.AppendLine("mod descriptors: " + CountFiles(Path.Combine(ck3Docs, "mod"), "*.mod"));
                sb.AppendLine("active continue title: " + NullText(DetectActiveSaveTitle()));
                sb.AppendLine("Steam launch options: " + NullText(ExtractSteamLaunchOptions()));
                sb.AppendLine("Steam Cloud disabled/unknown: " + YesNo(SteamCloudDisabledOrUnknownQuiet()));
                SafeAtomicFile.WriteAllText(snapshot, sb.ToString(), Encoding.UTF8);
                RecordRestoreEntry("snapshot", "(run snapshot)", snapshot, "Pre-change snapshot", "Current CK3/Steam/Launcher settings before CK3MPS changes.", "", "");
            }
            catch (Exception ex)
            {
                Log("WARN Pre-change snapshot could not be written: " + ex.Message);
            }
        }

        private string BackupForRestore(string path, string description)
        {
            if (String.IsNullOrEmpty(lastQuarantine) || String.IsNullOrEmpty(path))
                return "";
            if (!File.Exists(path) && !Directory.Exists(path))
                return "";

            Directory.CreateDirectory(RestoreBackupRoot());
            string dest = UniquePath(Path.Combine(RestoreBackupRoot(), SafeFileName(path)));
            if (File.Exists(path))
                File.Copy(path, dest, true);
            else
                CopyDirectory(path, dest);

            RecordRestoreEntry(File.Exists(path) ? "file" : "directory", path, dest, description, DescribePath(path), "Will be changed by CK3MPS.", "");
            return dest;
        }

        private string RecordCreatedFileForRestore(string path, string description)
        {
            return RecordRestoreEntry("created_file", path, "", description, "(missing)", "Will be created by CK3MPS.", "");
        }

        private string RecordMovedForRestore(string source, string dest, string description)
        {
            return RecordRestoreEntry(File.Exists(dest) ? "moved_file" : "moved_directory", source, dest, description, "Moved out of original location.", "Moved to quarantine.", "");
        }

        private string RecordRegistryBeforeChange(RegistryKey root, string subKey, string name, string description, string after)
        {
            try
            {
                string rootName = root == Registry.CurrentUser ? "HKCU" : (root == Registry.LocalMachine ? "HKLM" : root.Name);
                string source = rootName + "\\" + subKey + "\\" + name;
                string before = "(missing)";
                using (RegistryKey key = root.OpenSubKey(subKey, false))
                {
                    if (key != null)
                    {
                        object value = key.GetValue(name, null);
                        RegistryValueKind kind = value == null ? RegistryValueKind.Unknown : key.GetValueKind(name);
                        if (value != null)
                            before = RestoreManifestUtilities.SerializeRegistryValue(value, kind);
                    }
                }
                if (String.Equals(before, after, StringComparison.Ordinal))
                    return "";
                return RecordRestoreEntry("registry", source, "", description, before, after, "");
            }
            catch (Exception ex)
            {
                Log("WARN Registry restore snapshot failed: " + ex.Message);
                return "";
            }
        }

        private string RecordSystemSnapshot(string description, string command, string output)
        {
            try
            {
                if (String.IsNullOrEmpty(lastQuarantine))
                    return "";
                string file = UniquePath(Path.Combine(RestoreBackupRoot(), SafeFileName(description) + ".txt"));
                Directory.CreateDirectory(RestoreBackupRoot());
                SafeAtomicFile.WriteAllText(file, "Command: " + command + Environment.NewLine + output, Encoding.UTF8);
                return RecordRestoreEntry("system_snapshot", command, file, description, output, "", "");
            }
            catch (Exception ex)
            {
                Log("WARN System restore snapshot failed: " + ex.Message);
                return "";
            }
        }

        private string RecordRestoreEntry(string kind, string source, string backup, string description, string before, string after, string status)
        {
            string manifest = RestoreManifestFile();
            if (String.IsNullOrEmpty(manifest))
                return "";
            if (!File.Exists(manifest))
                InitializeRestoreManifest();
            if (String.IsNullOrEmpty(currentRestoreRunId))
                currentRestoreRunId = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string entryId = Guid.NewGuid().ToString("N");
            string line = String.Join("\t", new[]
            {
                entryId,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                RestoreManifestUtilities.EscapeTsv(kind),
                RestoreManifestUtilities.EscapeTsv(source),
                RestoreManifestUtilities.EscapeTsv(backup),
                RestoreManifestUtilities.EscapeTsv(description),
                RestoreManifestUtilities.EscapeTsv(before),
                RestoreManifestUtilities.EscapeTsv(after),
                RestoreManifestUtilities.EscapeTsv(status),
                RestoreManifestUtilities.EscapeTsv(currentRestoreRunId)
            });
            List<string> lines = new List<string>();
            if (File.Exists(manifest))
                lines.AddRange(File.ReadAllLines(manifest, Encoding.UTF8));
            if (lines.Count == 0)
                lines.Add("id\tcreated\tkind\tsource\tbackup\tdescription\tbefore\tafter\tstatus\trun_id");
            lines.Add(line);
            SafeAtomicFile.WriteAllLines(manifest, lines, Encoding.UTF8);
            return entryId;
        }

        private void UpdateRestoreEntryStatus(string entryId, string status)
        {
            if (String.IsNullOrWhiteSpace(entryId))
                return;

            string manifest = RestoreManifestFile();
            if (String.IsNullOrEmpty(manifest) || !File.Exists(manifest))
                throw new InvalidOperationException("Restore manifest is missing.");

            string[] lines = File.ReadAllLines(manifest, Encoding.UTF8);
            bool updated = false;
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('\t');
                if (parts.Length < 9 || !String.Equals(parts[0], entryId, StringComparison.OrdinalIgnoreCase))
                    continue;

                parts[8] = RestoreManifestUtilities.EscapeTsv(status);
                lines[i] = String.Join("\t", parts);
                updated = true;
                break;
            }

            if (!updated)
                throw new InvalidOperationException("Restore manifest entry was not found: " + entryId);

            SafeAtomicFile.WriteAllLines(manifest, lines, Encoding.UTF8);
        }

        private string DescribePath(string path)
        {
            if (File.Exists(path))
                return FileTimeHashLine(path);
            if (Directory.Exists(path))
                return "directory | items=" + CountItems(path);
            return "(missing)";
        }

        private bool FileContentsEqual(string left, string right)
        {
            if (!File.Exists(left) || !File.Exists(right))
                return false;

            FileInfo leftInfo = new FileInfo(left);
            FileInfo rightInfo = new FileInfo(right);
            if (leftInfo.Length != rightInfo.Length)
                return false;

            return String.Equals(FileHashOrMissing(left), FileHashOrMissing(right), StringComparison.OrdinalIgnoreCase);
        }

        private bool DirectoryContentsEqual(string left, string right)
        {
            if (!Directory.Exists(left) || !Directory.Exists(right))
                return false;

            Dictionary<string, bool> leftEntries = EnumerateDirectoryEntriesBounded(left);
            Dictionary<string, bool> rightEntries = EnumerateDirectoryEntriesBounded(right);
            if (leftEntries == null || rightEntries == null)
                return false;
            if (leftEntries.Count != rightEntries.Count)
                return false;

            foreach (KeyValuePair<string, bool> pair in leftEntries)
            {
                bool rightIsFile;
                if (!rightEntries.TryGetValue(pair.Key, out rightIsFile))
                    return false;
                if (pair.Value != rightIsFile)
                    return false;
                if (pair.Value && !FileContentsEqual(Path.Combine(left, pair.Key), Path.Combine(right, pair.Key)))
                    return false;
            }

            return true;
        }

        private Dictionary<string, bool> EnumerateDirectoryEntriesBounded(string root)
        {
            string normalizedRoot;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(root, out normalizedRoot) || !Directory.Exists(normalizedRoot))
                return null;

            Dictionary<string, bool> entries = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            Stopwatch sw = Stopwatch.StartNew();
            Stack<TraversalFrame> stack = new Stack<TraversalFrame>();
            stack.Push(new TraversalFrame(normalizedRoot, 0));

            int visitedDirectories = 0;
            int visitedFiles = 0;
            while (stack.Count > 0)
            {
                if (sw.ElapsedMilliseconds >= MaxBoundedTraversalElapsedMs)
                    return null;

                TraversalFrame frame = stack.Pop();
                if (frame.Depth > MaxBoundedTraversalDepth)
                    continue;
                if (ShouldSkipReparseDirectory(frame.Path))
                    continue;
                if (++visitedDirectories > MaxBoundedTraversalDirectories)
                    return null;

                string[] childDirectories = new string[0];
                try
                {
                    childDirectories = Directory.GetDirectories(frame.Path, "*", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                }

                for (int i = childDirectories.Length - 1; i >= 0; i--)
                {
                    string child = childDirectories[i];
                    if (ShouldSkipReparseDirectory(child))
                        continue;
                    string relative = child.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    entries[relative] = false;
                    stack.Push(new TraversalFrame(child, frame.Depth + 1));
                }

                string[] files = new string[0];
                try
                {
                    files = Directory.GetFiles(frame.Path, "*", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                }

                foreach (string file in files)
                {
                    if (sw.ElapsedMilliseconds >= MaxBoundedTraversalElapsedMs)
                        return null;
                    if (++visitedFiles > MaxBoundedTraversalDirectories)
                        return null;
                    string relative = file.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    entries[relative] = true;
                }
            }

            return entries;
        }

        private bool ShouldSkipReparseDirectory(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }

        private sealed class TraversalFrame
        {
            public readonly string Path;
            public readonly int Depth;

            public TraversalFrame(string path, int depth)
            {
                Path = path;
                Depth = depth;
            }
        }

        private List<RestoreEntry> ReadRestoreEntries()
        {
            List<RestoreEntry> entries = new List<RestoreEntry>();
            string manifest = RestoreManifestFile();
            if (String.IsNullOrEmpty(manifest) || !File.Exists(manifest))
                return entries;

            ReconcilePreparedRestoreEntries(manifest);
            string[] lines = File.ReadAllLines(manifest, Encoding.UTF8);
            Dictionary<string, List<RestoreEntry>> entriesById = new Dictionary<string, List<RestoreEntry>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                RestoreEntry entry = ParseRestoreManifestLine(lines[i], i + 1);
                if (entry == null)
                    continue;

                ValidateRestoreEntry(entry);
                entry.DisplayText = BuildListDisplayText(entry);
                entries.Add(entry);
                if (!String.IsNullOrWhiteSpace(entry.Id))
                {
                    List<RestoreEntry> sameId;
                    if (!entriesById.TryGetValue(entry.Id, out sameId))
                    {
                        sameId = new List<RestoreEntry>();
                        entriesById[entry.Id] = sameId;
                    }
                    sameId.Add(entry);
                }
            }

            MarkDuplicateRestoreEntries(entriesById);
            ApplyRestoreStatusOverlay(entries);
            return entries;
        }

        private void ReconcilePreparedRestoreEntries(string manifest)
        {
            if (String.IsNullOrWhiteSpace(manifest) || !File.Exists(manifest))
                return;

            string[] lines = File.ReadAllLines(manifest, Encoding.UTF8);
            bool updated = false;
            for (int i = 1; i < lines.Length; i++)
            {
                RestoreEntry entry = ParseRestoreManifestLine(lines[i], i + 1);
                if (entry == null)
                    continue;

                string nextStatus;
                if (!TryResolvePreparedRestoreStatus(entry, out nextStatus))
                    continue;

                string[] parts = lines[i].Split('\t');
                if (parts.Length < 9)
                    continue;

                parts[8] = RestoreManifestUtilities.EscapeTsv(nextStatus);
                lines[i] = String.Join("\t", parts);
                updated = true;
            }

            if (updated)
                SafeAtomicFile.WriteAllLines(manifest, lines, Encoding.UTF8);
        }

        private bool TryResolvePreparedRestoreStatus(RestoreEntry entry, out string nextStatus)
        {
            nextStatus = "";
            if (entry == null || !String.Equals(NullText(entry.Status), "prepared", StringComparison.OrdinalIgnoreCase))
                return false;

            string kind = NullText(entry.Kind).Trim().ToLowerInvariant();
            if (kind != "moved_file" && kind != "moved_directory")
                return false;

            string error;
            if (!TryValidateManifestSourcePath(entry.SourcePath, kind == "moved_directory", true, true, out error)
                || !TryValidateManifestBackupPath(entry.BackupPath, kind == "moved_directory", out error))
            {
                nextStatus = "failed";
                return true;
            }

            bool sourceExists = kind == "moved_directory" ? Directory.Exists(entry.SourcePath) : File.Exists(entry.SourcePath);
            bool backupExists = kind == "moved_directory" ? Directory.Exists(entry.BackupPath) : File.Exists(entry.BackupPath);

            if (!sourceExists && backupExists && PreparedRestoreBackupHashMatches(entry, kind))
                nextStatus = "committed";
            else if (sourceExists && !backupExists && PreparedRestoreSourceHashMatches(entry, kind))
                nextStatus = "rolled_back";
            else
                nextStatus = "failed";

            return true;
        }

        private RestoreEntry ParseRestoreManifestLine(string line, int lineNumber)
        {
            string[] parts = (line ?? "").Split('\t');
            if (parts.Length != 9 && parts.Length != 10)
                return CreateInvalidRestoreManifestEntry(lineNumber, "Manifest row must contain 9 or 10 tab-separated columns.", line);

            string created = parts[1];
            if (!IsValidRestoreManifestCreated(created))
                return CreateInvalidRestoreManifestEntry(lineNumber, "Created timestamp is malformed.", line);

            string runId = RestoreManifestUtilities.RunIdFromManifestParts(parts, created);
            if (!IsValidRestoreManifestRunId(runId))
                return CreateInvalidRestoreManifestEntry(lineNumber, "Run ID is malformed.", line);

            return new RestoreEntry
            {
                Id = parts[0],
                Created = created,
                Kind = parts[2],
                SourcePath = parts[3],
                BackupPath = parts[4],
                Description = parts[5],
                Before = parts[6],
                After = parts[7],
                Status = parts[8],
                RunId = runId
            };
        }

        private RestoreEntry CreateInvalidRestoreManifestEntry(int lineNumber, string error, string rawLine)
        {
            string snippet = ShortRestoreText((rawLine ?? "").Trim(), 72);
            string description = "Invalid restore manifest row " + lineNumber;
            if (!String.IsNullOrEmpty(snippet))
                description += ": " + snippet;

            return new RestoreEntry
            {
                Id = "__invalid_manifest_row_" + lineNumber.ToString(CultureInfo.InvariantCulture),
                Created = "",
                Kind = "manifest_error",
                SourcePath = "",
                BackupPath = "",
                Description = description,
                Before = "",
                After = "",
                Status = "invalid",
                RunId = "invalid",
                ValidationError = error
            };
        }

        private void MarkDuplicateRestoreEntries(Dictionary<string, List<RestoreEntry>> entriesById)
        {
            foreach (KeyValuePair<string, List<RestoreEntry>> pair in entriesById)
            {
                if (pair.Value == null || pair.Value.Count < 2)
                    continue;

                foreach (RestoreEntry entry in pair.Value)
                {
                    entry.ValidationError = "Restore entry ID is duplicated in restore_manifest.tsv.";
                    entry.Status = "invalid";
                    entry.DisplayText = BuildListDisplayText(entry);
                }
            }
        }

        private bool IsValidRestoreManifestCreated(string created)
        {
            DateTime ignored;
            return DateTime.TryParseExact(created, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out ignored);
        }

        private bool IsValidRestoreManifestRunId(string runId)
        {
            if (String.IsNullOrWhiteSpace(runId) || runId.Length != 15 || runId[8] != '_')
                return false;

            for (int i = 0; i < runId.Length; i++)
            {
                if (i == 8)
                    continue;
                if (!Char.IsDigit(runId[i]))
                    return false;
            }

            return true;
        }

        private void ValidateRestoreEntry(RestoreEntry entry)
        {
            if (entry == null)
                return;
            if (!String.IsNullOrEmpty(entry.ValidationError))
            {
                entry.Status = "invalid";
                return;
            }

            string error;
            if (!TryValidateRestoreEntry(entry, out error))
            {
                entry.ValidationError = error;
                entry.Status = "invalid";
            }
        }

        private bool TryValidateRestoreEntry(RestoreEntry entry, out string error)
        {
            error = "";
            if (entry == null)
            {
                error = "Restore entry is missing.";
                return false;
            }

            if (String.IsNullOrWhiteSpace(entry.Id))
            {
                error = "Restore entry ID is missing.";
                return false;
            }

            string kind = NullText(entry.Kind).Trim().ToLowerInvariant();
            switch (kind)
            {
                case "snapshot":
                    return TryValidateInformationalSnapshotEntry(entry, false, out error);
                case "system_snapshot":
                    return TryValidateInformationalSnapshotEntry(entry, true, out error);
                case "registry":
                    return TryValidateRegistryRestorePath(entry.SourcePath, out error);
                case "file":
                case "moved_file":
                    return TryValidateRestoreFileEntry(entry, out error);
                case "created_file":
                    return TryValidateCreatedFileEntry(entry, out error);
                case "directory":
                case "moved_directory":
                    return TryValidateRestoreDirectoryEntry(entry, out error);
                default:
                    error = "Restore entry kind is not supported: " + entry.Kind;
                    return false;
            }
        }

        private bool TryValidateInformationalSnapshotEntry(RestoreEntry entry, bool systemSnapshot, out string error)
        {
            error = "";
            if (String.IsNullOrWhiteSpace(entry.BackupPath))
            {
                error = "Snapshot backup path is missing.";
                return false;
            }
            if (!TryValidateManifestBackupPath(entry.BackupPath, false, out error))
                return false;
            if (!File.Exists(entry.BackupPath))
            {
                error = "Snapshot backup file is missing.";
                return false;
            }
            if (!systemSnapshot && !String.Equals(entry.SourcePath, "(run snapshot)", StringComparison.Ordinal))
            {
                error = "Run snapshot source marker is invalid.";
                return false;
            }
            if (systemSnapshot && String.IsNullOrWhiteSpace(entry.SourcePath))
            {
                error = "System snapshot command description is missing.";
                return false;
            }
            return true;
        }

        private bool PreparedRestoreBackupHashMatches(RestoreEntry entry, string kind)
        {
            if (kind == "moved_directory")
                return true;
            string expected = PreparedRestoreExpectedHash(entry);
            return !String.IsNullOrEmpty(expected)
                && String.Equals(expected, FileHashOrMissing(entry.BackupPath), StringComparison.OrdinalIgnoreCase);
        }

        private bool PreparedRestoreSourceHashMatches(RestoreEntry entry, string kind)
        {
            if (kind == "moved_directory")
                return true;
            string expected = PreparedRestoreExpectedHash(entry);
            return !String.IsNullOrEmpty(expected)
                && String.Equals(expected, FileHashOrMissing(entry.SourcePath), StringComparison.OrdinalIgnoreCase);
        }

        private string PreparedRestoreExpectedHash(RestoreEntry entry)
        {
            const string prefix = "sha256:";
            string value = NullText(entry == null ? "" : entry.Before).Trim();
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(prefix.Length).Trim()
                : "";
        }

        private bool TryValidateRestoreFileEntry(RestoreEntry entry, out string error)
        {
            return TryValidateManifestBackupPath(entry.BackupPath, false, out error)
                && TryValidateManifestSourcePath(entry.SourcePath, false, true, true, out error);
        }

        private bool TryValidateCreatedFileEntry(RestoreEntry entry, out string error)
        {
            return TryValidateManifestSourcePath(entry.SourcePath, false, true, false, out error);
        }

        private bool TryValidateRestoreDirectoryEntry(RestoreEntry entry, out string error)
        {
            return TryValidateManifestBackupPath(entry.BackupPath, true, out error)
                && TryValidateManifestSourcePath(entry.SourcePath, true, true, false, out error);
        }

        private bool TryValidateManifestBackupPath(string path, bool directoryExpected, out string error)
        {
            error = "";
            string normalized;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(path, out normalized))
            {
                error = "Backup path is malformed.";
                return false;
            }

            string backupRoot = RestoreBackupRoot();
            if (String.IsNullOrWhiteSpace(backupRoot) || !PathContainmentUtilities.IsWithinRoot(backupRoot, normalized) || String.Equals(Ck3PathUtilities.NormalizeDirectoryPath(backupRoot), normalized, StringComparison.OrdinalIgnoreCase))
            {
                error = "Backup path is outside the current restore backup root.";
                return false;
            }

            if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(normalized))
            {
                error = "Backup path uses a reparse point.";
                return false;
            }

            if (directoryExpected)
            {
                if (File.Exists(normalized))
                {
                    error = "Backup path points to a file instead of a directory.";
                    return false;
                }
            }
            else if (Directory.Exists(normalized))
            {
                error = "Backup path points to a directory instead of a file.";
                return false;
            }

            return true;
        }

        private bool TryValidateManifestSourcePath(string path, bool directoryExpected, bool allowCreatedMissing, bool allowManagedWorkflowSave, out string error)
        {
            error = "";
            string normalized;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(path, out normalized))
            {
                error = "Source path is malformed.";
                return false;
            }

            RestoreManifestUtilities.RestorePathKind pathKind = RestoreManifestUtilities.GetRestorePathKind(normalized, ck3Docs, LocalLauncherRoot(), RoamingLauncherRoot());
            bool allowed = pathKind == RestoreManifestUtilities.RestorePathKind.Ck3ConfigFile
                || pathKind == RestoreManifestUtilities.RestorePathKind.Ck3GeneratedDirectory
                || pathKind == RestoreManifestUtilities.RestorePathKind.LauncherCache
                || (allowManagedWorkflowSave && pathKind == RestoreManifestUtilities.RestorePathKind.ManagedWorkflowSave);
            if (!allowed)
            {
                error = "Source path is outside the CK3/Paradox allowlist.";
                return false;
            }

            if (PathContainmentUtilities.ContainsReparsePointInExistingSegments(normalized))
            {
                error = "Source path uses a reparse point.";
                return false;
            }

            if (!allowCreatedMissing || File.Exists(normalized) || Directory.Exists(normalized))
            {
                if (directoryExpected && File.Exists(normalized))
                {
                    error = "Source path points to a file instead of a directory.";
                    return false;
                }
                if (!directoryExpected && Directory.Exists(normalized))
                {
                    error = "Source path points to a directory instead of a file.";
                    return false;
                }
            }

            return true;
        }

        private bool TryValidateRegistryRestorePath(string path, out string error)
        {
            error = "";
            try
            {
                RegistryKey root;
                string subKey;
                string name;
                ParseRegistryRestorePath(path, out root, out subKey, out name);
                if (String.IsNullOrWhiteSpace(subKey) || String.IsNullOrWhiteSpace(name))
                {
                    error = "Registry restore path is incomplete.";
                    return false;
                }
                if (!RestoreManifestUtilities.IsAllowedRegistryRestoreTarget(path))
                {
                    error = "Registry restore path is outside the CK3MPS allowlist.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void ApplyRestoreStatusOverlay(List<RestoreEntry> entries)
        {
            Dictionary<string, bool> restored = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (RestoreEntry entry in entries)
            {
                if (entry.Kind == "restore_action" || entry.Kind == "default_restore")
                    restored[entry.SourcePath] = true;
            }

            foreach (RestoreEntry entry in entries)
            {
                if (String.IsNullOrEmpty(entry.Status) && restored.ContainsKey(entry.SourcePath) && entry.Kind != "restore_action" && entry.Kind != "default_restore")
                    entry.Status = "restored";
                else if (String.IsNullOrEmpty(entry.Status) && entry.Kind != "snapshot" && entry.Kind != "system_snapshot")
                    entry.Status = "active";
            }
        }

        private void RefreshRestoreList()
        {
            RefreshRestoreRuns();
            RefreshRestoreListOnly();
        }

        private void RefreshRestoreRuns()
        {
            List<RestoreEntry> entries = ReadRestoreEntries();
            string selected = Convert.ToString(restoreRunBox.SelectedItem);
            if (String.IsNullOrEmpty(selected))
                selected = "All runs";

            updatingRestoreUi = true;
            try
            {
                restoreRunBox.Items.Clear();
                restoreRunBox.Items.Add("All runs");
                SortedDictionary<string, bool> runs = new SortedDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (RestoreEntry entry in entries)
                    if (!String.IsNullOrEmpty(entry.RunId))
                        runs[entry.RunId] = true;
                List<string> runIds = new List<string>(runs.Keys);
                runIds.Sort(delegate (string a, string b) { return StringComparer.OrdinalIgnoreCase.Compare(b, a); });
                foreach (string run in runIds)
                    restoreRunBox.Items.Add(run);
                restoreRunBox.SelectedItem = restoreRunBox.Items.Contains(selected) ? selected : "All runs";
            }
            finally
            {
                updatingRestoreUi = false;
            }
        }

        private void RefreshRestoreListOnly()
        {
            CaptureCheckedRestoreEntryIds();
            restoreListBox.Items.Clear();
            string selectedRun = Convert.ToString(restoreRunBox.SelectedItem);
            List<RestoreEntry> visibleEntries = new List<RestoreEntry>();
            foreach (RestoreEntry entry in ReadRestoreEntries())
            {
                if (!String.IsNullOrEmpty(selectedRun) && selectedRun != "All runs" && !String.Equals(entry.RunId, selectedRun, StringComparison.OrdinalIgnoreCase))
                    continue;
                visibleEntries.Add(entry);
            }
            visibleEntries.Sort(CompareRestoreEntries);
            foreach (RestoreEntry entry in visibleEntries)
            {
                restoreListBox.Items.Add(entry);
                if (checkedRestoreEntryIds.Contains(entry.Id))
                    restoreListBox.SetItemChecked(restoreListBox.Items.Count - 1, true);
            }

            SyncCheckedRestoreEntryIds();
            UpdateRestoreSelectAllState();

            if (restoreListBox.Items.Count > 0)
                restoreListBox.SelectedIndex = 0;
            else
                restoreDetailsBox.Text = "(no restore entries yet)";
        }

        private void SetAllVisibleRestoreEntriesChecked(bool value)
        {
            updatingRestoreSelectionUi = true;
            try
            {
                for (int i = 0; i < restoreListBox.Items.Count; i++)
                    restoreListBox.SetItemChecked(i, value);
            }
            finally
            {
                updatingRestoreSelectionUi = false;
            }

            SyncCheckedRestoreEntryIds();
            UpdateRestoreSelectAllState();
            ShowSelectedRestoreDetails();
        }

        private void UpdateRestoreSelectAllState()
        {
            updatingRestoreSelectionUi = true;
            try
            {
                int total = restoreListBox.Items.Count;
                int selected = restoreListBox.CheckedItems.Count;
                restoreSelectAllBox.CheckState = total == 0 || selected == 0
                    ? CheckState.Unchecked
                    : (selected == total ? CheckState.Checked : CheckState.Indeterminate);
            }
            finally
            {
                updatingRestoreSelectionUi = false;
            }
        }

        private void ShowSelectedRestoreDetails()
        {
            if (checkedRestoreEntryIds.Count > 1)
            {
                ShowMultiSelectionRestoreDetails(checkedRestoreEntryIds.Count);
                return;
            }

            RestoreEntry entry = restoreListBox.SelectedItem as RestoreEntry;
            if (entry == null)
            {
                restoreDetailsBox.Text = "";
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Action: " + RestoreActionCaption(entry));
            sb.AppendLine("Status: " + RestoreStatusText(entry));
            sb.AppendLine("Created: " + entry.Created);
            sb.AppendLine("Run: " + NullText(entry.RunId));
            sb.AppendLine();
            sb.AppendLine("What it affects:");
            sb.AppendLine(NullText(entry.Description));
            sb.AppendLine();
            sb.AppendLine("Current item state:");
            sb.AppendLine(DescribeRestoreCurrentState(entry));
            if (!String.IsNullOrEmpty(entry.ValidationError))
            {
                sb.AppendLine();
                sb.AppendLine("Validation:");
                sb.AppendLine("Invalid restore entry. " + entry.ValidationError);
            }
            sb.AppendLine();
            sb.AppendLine("Restore selected:");
            sb.AppendLine(RestoreSelectedExplanation(entry));
            sb.AppendLine();
            sb.AppendLine("Restore default:");
            sb.AppendLine(DefaultRestoreSupported(entry)
                ? "Supported. CK3MPS removes this override and lets Windows/Steam/Launcher/CK3 recreate the default state."
                : "Not supported for this item. Use Restore selected instead.");
            sb.AppendLine();
            sb.AppendLine("Original path:");
            sb.AppendLine(NullText(entry.SourcePath));
            if (!String.IsNullOrEmpty(entry.BackupPath))
            {
                sb.AppendLine();
                sb.AppendLine("Backup/quarantine path:");
                sb.AppendLine(entry.BackupPath);
            }
            sb.AppendLine();
            sb.AppendLine("Before:");
            sb.AppendLine(NullText(entry.Before));
            if (!String.IsNullOrEmpty(entry.After))
            {
                sb.AppendLine();
                sb.AppendLine("After:");
                sb.AppendLine(entry.After);
            }
            string diffSummary = BuildRestoreDiffSummary(entry);
            if (!String.IsNullOrEmpty(diffSummary))
            {
                sb.AppendLine();
                sb.AppendLine("Compact diff:");
                sb.AppendLine(diffSummary);
            }
            restoreDetailsBox.Text = sb.ToString();
        }

        private void ShowMultiSelectionRestoreDetails(int selectedCount)
        {
            int registryCount = 0;
            int fileCount = 0;
            int folderCount = 0;
            int infoCount = 0;
            int defaultSupportedCount = 0;

            foreach (RestoreEntry entry in GetCheckedRestoreEntries())
            {
                if (entry.Kind == "registry")
                    registryCount++;
                else if (entry.Kind == "file" || entry.Kind == "moved_file" || entry.Kind == "created_file")
                    fileCount++;
                else if (entry.Kind == "directory" || entry.Kind == "moved_directory")
                    folderCount++;
                else
                    infoCount++;

                if (DefaultRestoreSupported(entry))
                    defaultSupportedCount++;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Selected restore entries: " + selectedCount);
            sb.AppendLine();
            sb.AppendLine("Contains:");
            sb.AppendLine("- Registry values: " + registryCount);
            sb.AppendLine("- Files: " + fileCount);
            sb.AppendLine("- Folders: " + folderCount);
            sb.AppendLine("- Informational entries: " + infoCount);
            sb.AppendLine();
            sb.AppendLine("Delete selected:");
            sb.AppendLine("Removes all checked entries from the Restore list and deletes unused backup files/folders when possible.");
            sb.AppendLine();
            sb.AppendLine("Restore selected / Restore default:");
            sb.AppendLine("Checked entries are used for bulk restore actions. Highlight any one item to inspect its detailed before/after snapshot.");
            sb.AppendLine();
            sb.AppendLine("Default restore support among selected: " + defaultSupportedCount + " of " + selectedCount);
            restoreDetailsBox.Text = sb.ToString();
        }

        private int CompareRestoreEntries(RestoreEntry left, RestoreEntry right)
        {
            string sortField = Convert.ToString(restoreSortBox.SelectedItem) ?? "Created";
            bool newestFirst = !String.Equals(Convert.ToString(restoreSortDirectionBox.SelectedItem), "Oldest first", StringComparison.OrdinalIgnoreCase);
            int cmp;
            switch (sortField)
            {
                case "Run":
                    cmp = StringComparer.OrdinalIgnoreCase.Compare(NullText(left.RunId), NullText(right.RunId));
                    break;
                case "Status":
                    cmp = StringComparer.OrdinalIgnoreCase.Compare(RestoreStatusText(left), RestoreStatusText(right));
                    break;
                case "Type":
                    cmp = StringComparer.OrdinalIgnoreCase.Compare(RestoreActionCaption(left), RestoreActionCaption(right));
                    break;
                case "Description":
                    cmp = StringComparer.OrdinalIgnoreCase.Compare(NullText(left.Description), NullText(right.Description));
                    break;
                case "Original path":
                    cmp = StringComparer.OrdinalIgnoreCase.Compare(NullText(left.SourcePath), NullText(right.SourcePath));
                    break;
                case "Backup path":
                    cmp = StringComparer.OrdinalIgnoreCase.Compare(NullText(left.BackupPath), NullText(right.BackupPath));
                    break;
                default:
                    cmp = CompareRestoreCreated(left, right);
                    break;
            }
            if (cmp == 0)
                cmp = CompareRestoreCreated(left, right);
            return newestFirst ? -cmp : cmp;
        }

        private static int CompareRestoreCreated(RestoreEntry left, RestoreEntry right)
        {
            DateTime a;
            DateTime b;
            bool aOk = DateTime.TryParse(left.Created, out a);
            bool bOk = DateTime.TryParse(right.Created, out b);
            if (aOk && bOk)
            {
                int cmp = a.CompareTo(b);
                if (cmp != 0)
                    return cmp;
            }
            return StringComparer.OrdinalIgnoreCase.Compare(left.Id, right.Id);
        }

        private string BuildListDisplayText(RestoreEntry entry)
        {
            string created = entry.Created;
            DateTime dt;
            if (DateTime.TryParse(entry.Created, out dt))
                created = dt.ToString("yyyy-MM-dd HH:mm");
            return created
                + " | "
                + RestoreStatusText(entry)
                + " | "
                + RestoreActionCaption(entry)
                + " | "
                + ShortRestoreText(entry.Description, 54);
        }

        private static string RestoreStatusText(RestoreEntry entry)
        {
            string status = String.IsNullOrEmpty(entry.Status) ? "active" : entry.Status;
            if (String.Equals(status, "restored_default", StringComparison.OrdinalIgnoreCase))
                return "default restored";
            if (String.Equals(status, "restore_action", StringComparison.OrdinalIgnoreCase))
                return "restored";
            if (String.Equals(status, "already_default", StringComparison.OrdinalIgnoreCase))
                return "already default";
            if (String.Equals(status, "rolled_back", StringComparison.OrdinalIgnoreCase))
                return "rolled back";
            return status.Replace('_', ' ');
        }

        private string RestoreActionCaption(RestoreEntry entry)
        {
            switch ((entry.Kind ?? "").ToLowerInvariant())
            {
                case "snapshot":
                    return "Snapshot";
                case "system_snapshot":
                    return "System snapshot";
                case "registry":
                    return "Registry value";
                case "file":
                    return "Backed-up file";
                case "directory":
                    return "Backed-up folder";
                case "moved_file":
                    return "Moved file";
                case "moved_directory":
                    return "Moved folder";
                case "created_file":
                    return "Created file";
                case "restore_action":
                    return "Restore action";
                case "default_restore":
                    return "Default restore";
            }
            return NullText(entry.Kind);
        }

        private static string ShortRestoreText(string text, int max)
        {
            string value = (text ?? "").Trim();
            if (value.Length <= max)
                return value;
            return value.Substring(0, max - 3) + "...";
        }

        private string RestoreSelectedExplanation(RestoreEntry entry)
        {
            if (!String.IsNullOrEmpty(entry.ValidationError))
                return "Blocked. CK3MPS marked this restore entry invalid: " + entry.ValidationError;
            if (entry.Kind == "snapshot" || entry.Kind == "system_snapshot")
                return "This is informational only. There is nothing to restore directly from the app.";
            if (entry.Kind == "registry")
                return "Writes the recorded previous registry value back, or removes it if it did not exist before.";
            if (entry.Kind == "created_file")
                return "Deletes the file created by CK3MPS and backs up the current file first if it still exists.";
            if (entry.Kind == "file" || entry.Kind == "moved_file")
                return "Copies the recorded backup file back to the original path.";
            if (entry.Kind == "directory" || entry.Kind == "moved_directory")
                return "Replaces the current folder with the recorded backup folder.";
            return "Restores the previously recorded state for this item.";
        }

        private string DescribeRestoreCurrentState(RestoreEntry entry)
        {
            if (!String.IsNullOrEmpty(entry.ValidationError))
                return "Invalid restore entry. No action is allowed until the manifest row is trusted again.";
            if (entry.Kind == "registry")
                return ReadRegistryRestoreValue(entry.SourcePath);
            if (entry.Kind == "system_snapshot" || entry.Kind == "snapshot")
                return "Informational snapshot. See backup/quarantine path.";
            return DescribePath(entry.SourcePath);
        }

        private string BuildRestoreDiffSummary(RestoreEntry entry)
        {
            if (entry.Kind == "registry")
                return entry.Before + " -> " + ReadRegistryRestoreValue(entry.SourcePath);
            if ((entry.Kind == "file" || entry.Kind == "moved_file" || entry.Kind == "created_file") && File.Exists(entry.BackupPath) && File.Exists(entry.SourcePath))
                return DiffTextFiles(entry.BackupPath, entry.SourcePath);
            return "";
        }

        private string DiffTextFiles(string beforePath, string afterPath)
        {
            try
            {
                FileInfo beforeInfo = new FileInfo(beforePath);
                FileInfo afterInfo = new FileInfo(afterPath);
                if (beforeInfo.Length > 65536 || afterInfo.Length > 65536)
                    return "Binary/large file change. Before=" + beforeInfo.Length + " bytes, after=" + afterInfo.Length + " bytes.";

                string beforeText = File.ReadAllText(beforePath, Encoding.UTF8);
                string afterText = File.ReadAllText(afterPath, Encoding.UTF8);
                if (beforeText == afterText)
                    return "No textual difference detected.";

                string[] beforeLines = beforeText.Replace("\r\n", "\n").Split('\n');
                string[] afterLines = afterText.Replace("\r\n", "\n").Split('\n');
                StringBuilder sb = new StringBuilder();
                int shown = 0;
                int max = Math.Max(beforeLines.Length, afterLines.Length);
                for (int i = 0; i < max && shown < 12; i++)
                {
                    string left = i < beforeLines.Length ? beforeLines[i] : "(missing)";
                    string right = i < afterLines.Length ? afterLines[i] : "(missing)";
                    if (String.Equals(left, right, StringComparison.Ordinal))
                        continue;
                    sb.AppendLine("- " + TrimDiffLine(left));
                    sb.AppendLine("+ " + TrimDiffLine(right));
                    shown++;
                }
                if (shown == 0)
                    return "Text changed, but compact diff could not isolate differing lines.";
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "Diff unavailable: " + ex.Message;
            }
        }

        private string TrimDiffLine(string line)
        {
            string text = (line ?? "").Trim();
            if (text.Length <= 180)
                return text;
            return text.Substring(0, 177) + "...";
        }

        private void RestoreSelectedItem()
        {
            List<RestoreEntry> entries = GetTargetRestoreEntries();
            if (entries.Count == 0)
                return;
            foreach (RestoreEntry entry in entries)
                if (!String.IsNullOrEmpty(entry.ValidationError))
                    throw new InvalidOperationException("Restore entry is invalid: " + entry.ValidationError);

            string targetText = entries.Count == 1
                ? entries[0].Description + "\r\n\r\nTarget:\r\n" + entries[0].SourcePath
                : "Restore " + entries.Count + " checked items to their recorded previous state?";
            Dictionary<string, RestoreOperationPathSnapshot> confirmationSnapshots = CaptureRestoreOperationSnapshots(entries);
            DialogResult result = MessageBox.Show("Restore selected item(s)?\r\n\r\n" + targetText, "CK3MPS restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
                return;

            try
            {
                activeRestoreOperationSnapshots = confirmationSnapshots;
                ExecuteRestoreBatch(entries, false);
                foreach (RestoreEntry entry in entries)
                    Log("OK   Restored: " + entry.Description);
                RefreshRestoreList();
            }
            catch (Exception ex)
            {
                Log("ERROR Restore failed: " + ex.Message);
                MessageBox.Show(ex.Message, "CK3MPS restore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                activeRestoreOperationSnapshots = null;
            }
        }

        private void RestoreSelectedItemToDefault()
        {
            List<RestoreEntry> entries = GetTargetRestoreEntries();
            if (entries.Count == 0)
                return;

            foreach (RestoreEntry entry in entries)
                if (!String.IsNullOrEmpty(entry.ValidationError))
                {
                    MessageBox.Show("One or more selected restore entries are invalid. CK3MPS will not apply default restore to untrusted manifest rows.", "CK3MPS restore", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            foreach (RestoreEntry entry in entries)
                if (!DefaultRestoreSupported(entry))
                {
                    MessageBox.Show("Default restore is not supported for one or more selected items. Use Restore selected to restore the recorded previous value.", "CK3MPS restore", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

            Dictionary<string, RestoreOperationPathSnapshot> confirmationSnapshots = CaptureRestoreOperationSnapshots(entries);
            DialogResult result = MessageBox.Show(
                entries.Count == 1
                    ? "Reset this item to game/launcher/Windows default behavior?\r\n\r\n" + entries[0].SourcePath + "\r\n\r\nCK3MPS will back up the current value first, then remove the override/file so the owner can recreate defaults."
                    : "Reset " + entries.Count + " checked items to game/launcher/Windows default behavior?\r\n\r\nCK3MPS will back up current values first, then remove the overrides/files so the owner can recreate defaults.",
                "CK3MPS default restore",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
                return;

            try
            {
                activeRestoreOperationSnapshots = confirmationSnapshots;
                ExecuteRestoreBatch(entries, true);
                foreach (RestoreEntry entry in entries)
                    Log("OK   Restored default behavior: " + entry.Description);
                RefreshRestoreList();
            }
            catch (Exception ex)
            {
                Log("ERROR Default restore failed: " + ex.Message);
                MessageBox.Show(ex.Message, "CK3MPS restore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                activeRestoreOperationSnapshots = null;
            }
        }

        private void DeleteSelectedRestoreEntries()
        {
            List<RestoreEntry> selectedEntries = GetTargetRestoreEntries();
            if (selectedEntries.Count == 0)
                return;

            DialogResult result = MessageBox.Show(
                selectedEntries.Count == 1
                    ? "Delete this restore record from CK3MPS?\r\n\r\n"
                        + selectedEntries[0].Description
                        + "\r\n\r\nThis removes the entry from the Restore list. If its backup file is no longer used by other entries, CK3MPS will delete that backup too."
                    : "Delete " + selectedEntries.Count + " selected restore records from CK3MPS?\r\n\r\nThis removes the selected entries from the Restore list. If a backup file/folder is no longer used by other entries, CK3MPS will delete it too.",
                "CK3MPS restore",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
                return;

            try
            {
                List<string> ids = new List<string>();
                foreach (RestoreEntry entry in selectedEntries)
                    ids.Add(entry.Id);
                DeleteRestoreEntries(ids);
                Log("OK   Deleted restore entr" + (selectedEntries.Count == 1 ? "y" : "ies") + ": " + selectedEntries.Count);
                RefreshRestoreList();
            }
            catch (Exception ex)
            {
                Log("ERROR Delete restore entry failed: " + ex.Message);
                MessageBox.Show(ex.Message, "CK3MPS restore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private List<RestoreEntry> GetCheckedRestoreEntries()
        {
            List<RestoreEntry> entries = new List<RestoreEntry>();
            foreach (object item in restoreListBox.CheckedItems)
            {
                RestoreEntry entry = item as RestoreEntry;
                if (entry != null)
                    entries.Add(entry);
            }
            return entries;
        }

        private List<RestoreEntry> GetTargetRestoreEntries()
        {
            List<RestoreEntry> checkedEntries = GetCheckedRestoreEntries();
            if (checkedEntries.Count > 0)
                return checkedEntries;

            RestoreEntry selected = restoreListBox.SelectedItem as RestoreEntry;
            if (selected == null)
                return new List<RestoreEntry>();
            return new List<RestoreEntry> { selected };
        }

        private void CaptureCheckedRestoreEntryIds()
        {
            checkedRestoreEntryIds.Clear();
            foreach (RestoreEntry entry in GetCheckedRestoreEntries())
                checkedRestoreEntryIds.Add(entry.Id);
        }

        private void SyncCheckedRestoreEntryIds()
        {
            checkedRestoreEntryIds.Clear();
            foreach (RestoreEntry entry in GetCheckedRestoreEntries())
                checkedRestoreEntryIds.Add(entry.Id);
        }

        private void DeleteRestoreEntries(IEnumerable<string> entryIds)
        {
            HashSet<string> toDelete = new HashSet<string>(entryIds ?? new string[0], StringComparer.OrdinalIgnoreCase);
            if (toDelete.Count == 0)
                return;

            string manifest = RestoreManifestFile();
            if (String.IsNullOrEmpty(manifest) || !File.Exists(manifest))
                return;

            List<RestoreEntry> entries = ReadRestoreEntries();
            List<string> lines = new List<string>(File.ReadAllLines(manifest, Encoding.UTF8));
            if (lines.Count == 0)
                return;

            List<string> kept = new List<string>();
            kept.Add(lines[0]);
            for (int i = 1; i < lines.Count; i++)
            {
                string line = lines[i];
                string[] parts = line.Split('\t');
                if (parts.Length == 0 || !toDelete.Contains(parts[0]))
                    kept.Add(line);
            }
            SafeAtomicFile.WriteAllLines(manifest, kept.ToArray(), Encoding.UTF8);

            HashSet<string> remainingBackups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RestoreEntry existing in ReadRestoreEntries())
                if (!String.IsNullOrEmpty(existing.BackupPath))
                    remainingBackups.Add(existing.BackupPath);

            foreach (RestoreEntry removed in entries)
            {
                if (!toDelete.Contains(removed.Id) || String.IsNullOrEmpty(removed.BackupPath) || remainingBackups.Contains(removed.BackupPath))
                    continue;
                string backupDeleteError;
                if (!TryValidateManifestBackupPath(removed.BackupPath, Directory.Exists(removed.BackupPath), out backupDeleteError))
                    continue;

                if (File.Exists(removed.BackupPath))
                    File.Delete(removed.BackupPath);
                else if (Directory.Exists(removed.BackupPath))
                    Directory.Delete(removed.BackupPath, true);
            }
        }

        private bool DefaultRestoreSupported(RestoreEntry entry)
        {
            if (entry == null || !String.IsNullOrEmpty(entry.ValidationError))
                return false;
            if (entry.Kind == "registry")
                return RestoreManifestUtilities.IsAllowedRegistryRestoreTarget(entry.SourcePath);
            if (IsSteamLocalConfigEntry(entry) || IsSteamSharedConfigEntry(entry))
                return true;
            if (entry.Kind == "file" || entry.Kind == "created_file" || entry.Kind == "moved_file" || entry.Kind == "directory" || entry.Kind == "moved_directory")
                return RestoreManifestUtilities.IsDefaultRestorablePath(entry.SourcePath, ck3Docs, LocalLauncherRoot(), RoamingLauncherRoot());
            return false;
        }

        private bool IsSteamLocalConfigEntry(RestoreEntry entry)
        {
            return !String.IsNullOrEmpty(localConfig)
                && String.Equals(entry.SourcePath, localConfig, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSteamSharedConfigEntry(RestoreEntry entry)
        {
            return !String.IsNullOrEmpty(sharedConfig)
                && String.Equals(entry.SourcePath, sharedConfig, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsOwnedByCk3OrParadoxLauncher(string path)
        {
            if (String.IsNullOrEmpty(path))
                return false;
            return RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(path, ck3Docs, LocalLauncherRoot(), RoamingLauncherRoot());
        }

        private string LocalLauncherRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paradox Interactive", "launcher-v2");
        }

        private string RoamingLauncherRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paradox Interactive", "launcher-v2");
        }

        private void RestoreDefaultEntry(RestoreEntry entry)
        {
            EnsureRestoreOperationStillAllowed(entry, "default restore");
            string beforeNow = DescribeRestoreCurrentState(entry);
            if (IsSteamLocalConfigEntry(entry))
            {
                bool changed = RemoveSteamLaunchOptionsOverride();
                if (changed)
                    RecordRestoreEntry("default_restore", entry.SourcePath, "", "Default restore: removed Steam LaunchOptions override for CK3.", beforeNow, NullText(ExtractSteamLaunchOptions()), "restored_default");
                return;
            }

            if (IsSteamSharedConfigEntry(entry))
            {
                bool changed = RemoveSteamCloudOverride();
                if (changed)
                    RecordRestoreEntry("default_restore", entry.SourcePath, "", "Default restore: removed Steam Cloud override for CK3.", beforeNow, SteamCloudDisabledOrUnknownQuiet() ? "cloud override removed/unknown" : "cloud flag visible", "restored_default");
                return;
            }

            if (entry.Kind == "registry")
            {
                MutationAudit.RecordMutation("registry-delete", entry.SourcePath);
                RegistryKey root;
                string subKey;
                string name;
                ParseRegistryRestorePath(entry.SourcePath, out root, out subKey, out name);
                using (RegistryKey key = root.OpenSubKey(subKey, true))
                {
                    if (key == null || key.GetValue(name, null) == null)
                        return;
                    key.DeleteValue(name, false);
                }
                RecordRestoreEntry("default_restore", entry.SourcePath, "", "Default restored registry value: " + entry.SourcePath, beforeNow, "(missing/default)", "restored_default");
                return;
            }

            if (File.Exists(entry.SourcePath))
            {
                BackupForRestore(entry.SourcePath, "Pre-default-restore backup of current file: " + entry.SourcePath);
                File.Delete(entry.SourcePath);
            }
            else if (Directory.Exists(entry.SourcePath))
            {
                BackupForRestore(entry.SourcePath, "Pre-default-restore backup of current directory: " + entry.SourcePath);
                Directory.Delete(entry.SourcePath, true);
            }
            else
                return;

            RecordRestoreEntry("default_restore", entry.SourcePath, "", "Default restore: removed CK3/Launcher override so owner can recreate defaults: " + entry.SourcePath, beforeNow, "(missing/default)", "restored_default");
        }

        private void RestoreFileEntry(RestoreEntry entry)
        {
            EnsureRestoreOperationStillAllowed(entry, "file restore");
            if (!File.Exists(entry.BackupPath))
                throw new FileNotFoundException("Backup file is missing.", entry.BackupPath);

            string dir = Path.GetDirectoryName(entry.SourcePath);
            if (!String.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            if (File.Exists(entry.SourcePath) && FileContentsEqual(entry.SourcePath, entry.BackupPath))
                return;
            if (String.Equals(entry.Kind, "moved_file", StringComparison.OrdinalIgnoreCase) && File.Exists(entry.SourcePath))
                throw new IOException("A workflow save already exists at the original path. Restore will not overwrite it without a separate user decision.");
            if (File.Exists(entry.SourcePath))
                BackupForRestore(entry.SourcePath, "Pre-restore backup of current file: " + entry.SourcePath);
            string tempPath = entry.SourcePath + ".restore-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(entry.BackupPath, tempPath, false);
                if (!FileContentsEqual(entry.BackupPath, tempPath))
                    throw new IOException("Restore staging copy does not match the recorded backup.");
                EnsureRestoreOperationStillAllowed(entry, "file restore commit");
                SafeAtomicFile.ReplaceFile(tempPath, entry.SourcePath);
            }
            finally
            {
                SafeAtomicFile.TryDeleteTempFile(tempPath);
            }
            RecordRestoreEntry("restore_action", entry.SourcePath, entry.BackupPath, "Restored file: " + entry.SourcePath, entry.Before, DescribePath(entry.SourcePath), "restored");
        }

        private void RestoreCreatedFileEntry(RestoreEntry entry)
        {
            EnsureRestoreOperationStillAllowed(entry, "created-file restore");
            if (!File.Exists(entry.SourcePath))
                return;

            BackupForRestore(entry.SourcePath, "Pre-restore backup of created file before deleting it: " + entry.SourcePath);
            File.Delete(entry.SourcePath);
            RecordRestoreEntry("restore_action", entry.SourcePath, "", "Deleted CK3MPS-created file: " + entry.SourcePath, entry.Before, "(missing)", "restored");
        }

        private void RestoreDirectoryEntry(RestoreEntry entry)
        {
            EnsureRestoreOperationStillAllowed(entry, "directory restore");
            if (!Directory.Exists(entry.BackupPath))
                throw new DirectoryNotFoundException("Backup directory is missing: " + entry.BackupPath);

            if (Directory.Exists(entry.SourcePath) && DirectoryContentsEqual(entry.SourcePath, entry.BackupPath))
                return;
            if (Directory.Exists(entry.SourcePath))
                BackupForRestore(entry.SourcePath, "Pre-restore backup of current directory: " + entry.SourcePath);

            AtomicReplaceDirectoryFromBackup(entry.BackupPath, entry.SourcePath);
            RecordRestoreEntry("restore_action", entry.SourcePath, entry.BackupPath, "Restored directory: " + entry.SourcePath, entry.Before, DescribePath(entry.SourcePath), "restored");
        }

        private void RestoreRegistryEntry(RestoreEntry entry)
        {
            EnsureRestoreOperationStillAllowed(entry, "registry restore");
            MutationAudit.RecordMutation("registry-restore", entry.SourcePath);
            RegistryKey root;
            string subKey;
            string name;
            ParseRegistryRestorePath(entry.SourcePath, out root, out subKey, out name);

            string beforeNow = ReadRegistryRestoreValue(entry.SourcePath);
            string targetValue = entry.Before == "(missing)" || String.IsNullOrEmpty(entry.Before) ? "(missing)" : entry.Before;
            if (String.Equals(beforeNow, targetValue, StringComparison.Ordinal))
                return;
            if (entry.Before == "(missing)" || String.IsNullOrEmpty(entry.Before))
            {
                using (RegistryKey key = root.OpenSubKey(subKey, true))
                {
                    if (key != null)
                        key.DeleteValue(name, false);
                }
            }
            else
            {
                using (RegistryKey key = root.CreateSubKey(subKey))
                {
                    if (key == null)
                        throw new InvalidOperationException("Registry key could not be opened: " + entry.SourcePath);

                    int colon = entry.Before.IndexOf(':');
                    if (colon <= 0)
                        throw new InvalidOperationException("Registry backup format is not supported: " + entry.Before);

                    string kindText = entry.Before.Substring(0, colon);
                    string valueText = entry.Before.Substring(colon + 1);
                    RegistryValueKind kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), kindText);
                    object value = ParseRegistryValue(valueText, kind);
                    key.SetValue(name, value, kind);
                }
            }

            RecordRestoreEntry("restore_action", entry.SourcePath, "", "Restored registry value: " + entry.SourcePath, beforeNow, ReadRegistryRestoreValue(entry.SourcePath), "restored");
        }

        private void EnsureRestoreOperationStillAllowed(RestoreEntry entry, string operation)
        {
            if (entry == null)
                throw new InvalidOperationException("Restore entry is missing.");

            string error;
            if (!TryValidateRestoreEntry(entry, out error))
                throw new InvalidOperationException("Blocked " + operation + " for untrusted restore entry. " + error);
            EnsureRestoreOperationSnapshotUnchanged(entry, operation);
        }

        private Dictionary<string, RestoreOperationPathSnapshot> CaptureRestoreOperationSnapshots(IEnumerable<RestoreEntry> entries)
        {
            Dictionary<string, RestoreOperationPathSnapshot> snapshots = new Dictionary<string, RestoreOperationPathSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (RestoreEntry entry in entries ?? new RestoreEntry[0])
            {
                if (entry == null || String.IsNullOrWhiteSpace(entry.Id))
                    continue;
                if (entry.Kind == "snapshot" || entry.Kind == "system_snapshot")
                    continue;
                snapshots[entry.Id + "|source"] = CaptureRestoreOperationPathSnapshot(entry.SourcePath, entry.Kind == "registry");
                if (!String.IsNullOrWhiteSpace(entry.BackupPath))
                    snapshots[entry.Id + "|backup"] = CaptureRestoreOperationPathSnapshot(entry.BackupPath, false);
            }
            return snapshots;
        }

        private RestoreOperationPathSnapshot CaptureRestoreOperationPathSnapshot(string path, bool registry)
        {
            RestoreOperationPathSnapshot snapshot = new RestoreOperationPathSnapshot();
            if (registry)
            {
                snapshot.NormalizedPath = NullText(path);
                snapshot.RegistryValue = ReadRegistryRestoreValue(path);
                return snapshot;
            }

            string normalized;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(path, out normalized))
                throw new InvalidOperationException("Restore path could not be normalized before confirmation: " + NullText(path));
            snapshot.NormalizedPath = normalized;
            snapshot.FileExists = File.Exists(normalized);
            snapshot.DirectoryExists = Directory.Exists(normalized);
            snapshot.HasReparsePoint = PathContainmentUtilities.ContainsReparsePointInExistingSegments(normalized);
            if (snapshot.FileExists)
            {
                FileInfo info = new FileInfo(normalized);
                snapshot.Length = info.Length;
                snapshot.LastWriteUtc = info.LastWriteTimeUtc;
                snapshot.Attributes = info.Attributes;
                snapshot.Sha256 = FileHashOrMissing(normalized);
            }
            else if (snapshot.DirectoryExists)
            {
                DirectoryInfo info = new DirectoryInfo(normalized);
                snapshot.LastWriteUtc = info.LastWriteTimeUtc;
                snapshot.Attributes = info.Attributes;
            }
            return snapshot;
        }

        private void EnsureRestoreOperationSnapshotUnchanged(RestoreEntry entry, string operation)
        {
            if (activeRestoreOperationSnapshots == null || entry == null)
                return;

            EnsureRestorePathSnapshotUnchanged(entry.Id + "|source", entry.SourcePath, entry.Kind == "registry", operation);
            if (!String.IsNullOrWhiteSpace(entry.BackupPath))
                EnsureRestorePathSnapshotUnchanged(entry.Id + "|backup", entry.BackupPath, false, operation);
        }

        private void EnsureRestorePathSnapshotUnchanged(string key, string path, bool registry, string operation)
        {
            RestoreOperationPathSnapshot expected;
            if (!activeRestoreOperationSnapshots.TryGetValue(key, out expected))
                throw new InvalidOperationException("Blocked " + operation + ": confirmation snapshot is missing.");
            RestoreOperationPathSnapshot current = CaptureRestoreOperationPathSnapshot(path, registry);
            bool same = String.Equals(expected.NormalizedPath, current.NormalizedPath, StringComparison.OrdinalIgnoreCase)
                && expected.FileExists == current.FileExists
                && expected.DirectoryExists == current.DirectoryExists
                && expected.Length == current.Length
                && expected.LastWriteUtc == current.LastWriteUtc
                && expected.Attributes == current.Attributes
                && expected.HasReparsePoint == current.HasReparsePoint
                && String.Equals(expected.Sha256, current.Sha256, StringComparison.OrdinalIgnoreCase)
                && String.Equals(expected.RegistryValue, current.RegistryValue, StringComparison.Ordinal);
            if (!same)
                throw new InvalidOperationException("Blocked " + operation + ": source or backup changed after confirmation. Refresh Restore and try again.");
        }

        private object ParseRegistryValue(string value, RegistryValueKind kind)
        {
            return RestoreManifestUtilities.ParseSerializedRegistryValue(value, kind);
        }

        private string ReadRegistryRestoreValue(string path)
        {
            try
            {
                RegistryKey root;
                string subKey;
                string name;
                ParseRegistryRestorePath(path, out root, out subKey, out name);
                using (RegistryKey key = root.OpenSubKey(subKey, false))
                {
                    if (key == null)
                        return "(missing)";
                    object value = key.GetValue(name, null);
                    if (value == null)
                        return "(missing)";
                    return RestoreManifestUtilities.SerializeRegistryValue(value, key.GetValueKind(name));
                }
            }
            catch (Exception ex)
            {
                return "(unreadable: " + ex.Message + ")";
            }
        }

        private void ParseRegistryRestorePath(string path, out RegistryKey root, out string subKey, out string name)
        {
            string rootText;
            if (!RestoreManifestUtilities.TryParseRegistryRestorePath(path, out rootText, out subKey, out name))
                throw new InvalidOperationException("Registry restore path is invalid: " + path);

            if (String.Equals(rootText, "HKCU", StringComparison.OrdinalIgnoreCase))
                root = Registry.CurrentUser;
            else if (String.Equals(rootText, "HKLM", StringComparison.OrdinalIgnoreCase))
                root = Registry.LocalMachine;
            else
                throw new InvalidOperationException("Registry root is not supported: " + rootText);
        }
    }
}
