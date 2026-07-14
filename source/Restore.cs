using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            public override string ToString()
            {
                return DisplayText ?? Description ?? Id ?? "";
            }
        }

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
                File.WriteAllText(manifest, "id\tcreated\tkind\tsource\tbackup\tdescription\tbefore\tafter\tstatus\trun_id\r\n", Encoding.UTF8);

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
                File.WriteAllText(snapshot, sb.ToString(), Encoding.UTF8);
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

        private void RecordCreatedFileForRestore(string path, string description)
        {
            RecordRestoreEntry("created_file", path, "", description, "(missing)", "Will be created by CK3MPS.", "");
        }

        private void RecordMovedForRestore(string source, string dest, string description)
        {
            RecordRestoreEntry(File.Exists(dest) ? "moved_file" : "moved_directory", source, dest, description, "Moved out of original location.", "Moved to quarantine.", "");
        }

        private void RecordRegistryBeforeChange(RegistryKey root, string subKey, string name, string description, string after)
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
                RecordRestoreEntry("registry", source, "", description, before, after, "");
            }
            catch (Exception ex)
            {
                Log("WARN Registry restore snapshot failed: " + ex.Message);
            }
        }

        private void RecordSystemSnapshot(string description, string command, string output)
        {
            try
            {
                if (String.IsNullOrEmpty(lastQuarantine))
                    return;
                string file = UniquePath(Path.Combine(RestoreBackupRoot(), SafeFileName(description) + ".txt"));
                Directory.CreateDirectory(RestoreBackupRoot());
                File.WriteAllText(file, "Command: " + command + Environment.NewLine + output, Encoding.UTF8);
                RecordRestoreEntry("system_snapshot", command, file, description, output, "", "");
            }
            catch (Exception ex)
            {
                Log("WARN System restore snapshot failed: " + ex.Message);
            }
        }

        private void RecordRestoreEntry(string kind, string source, string backup, string description, string before, string after, string status)
        {
            string manifest = RestoreManifestFile();
            if (String.IsNullOrEmpty(manifest))
                return;
            if (!File.Exists(manifest))
                InitializeRestoreManifest();
            if (String.IsNullOrEmpty(currentRestoreRunId))
                currentRestoreRunId = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string line = String.Join("\t", new[]
            {
                Guid.NewGuid().ToString("N"),
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
            File.AppendAllText(manifest, line + Environment.NewLine, Encoding.UTF8);
        }

        private string DescribePath(string path)
        {
            if (File.Exists(path))
                return FileTimeHashLine(path);
            if (Directory.Exists(path))
                return "directory | items=" + CountItems(path);
            return "(missing)";
        }

        private List<RestoreEntry> ReadRestoreEntries()
        {
            List<RestoreEntry> entries = new List<RestoreEntry>();
            string manifest = RestoreManifestFile();
            if (String.IsNullOrEmpty(manifest) || !File.Exists(manifest))
                return entries;

            string[] lines = File.ReadAllLines(manifest, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('\t');
                if (parts.Length < 9)
                    continue;
                RestoreEntry entry = new RestoreEntry
                {
                    Id = parts[0],
                    Created = parts[1],
                    Kind = parts[2],
                    SourcePath = parts[3],
                    BackupPath = parts[4],
                    Description = parts[5],
                    Before = parts[6],
                    After = parts[7],
                    Status = parts[8],
                    RunId = RestoreManifestUtilities.RunIdFromManifestParts(parts, parts[1])
                };
                entry.DisplayText = BuildListDisplayText(entry);
                entries.Add(entry);
            }
            ApplyRestoreStatusOverlay(entries);
            return entries;
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
            restoreListBox.Items.Clear();
            string selectedRun = Convert.ToString(restoreRunBox.SelectedItem);
            List<RestoreEntry> visibleEntries = new List<RestoreEntry>();
            foreach (RestoreEntry entry in ReadRestoreEntries())
            {
                if (!String.IsNullOrEmpty(selectedRun) && selectedRun != "All runs" && !String.Equals(entry.RunId, selectedRun, StringComparison.OrdinalIgnoreCase))
                    continue;
                visibleEntries.Add(entry);
            }
            visibleEntries.Sort(CompareRestoreEntriesNewestFirst);
            foreach (RestoreEntry entry in visibleEntries)
                restoreListBox.Items.Add(entry);

            if (restoreListBox.Items.Count > 0)
                restoreListBox.SelectedIndex = 0;
            else
                restoreDetailsBox.Text = "(no restore entries yet)";
        }

        private void ShowSelectedRestoreDetails()
        {
            if (restoreListBox.SelectedItems.Count > 1)
            {
                ShowMultiSelectionRestoreDetails();
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

        private void ShowMultiSelectionRestoreDetails()
        {
            int selectedCount = restoreListBox.SelectedItems.Count;
            int registryCount = 0;
            int fileCount = 0;
            int folderCount = 0;
            int infoCount = 0;
            int defaultSupportedCount = 0;

            foreach (object item in restoreListBox.SelectedItems)
            {
                RestoreEntry entry = item as RestoreEntry;
                if (entry == null)
                    continue;

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
            sb.AppendLine("Removes all selected entries from the Restore list and deletes unused backup files/folders when possible.");
            sb.AppendLine();
            sb.AppendLine("Restore selected / Restore default:");
            sb.AppendLine("Use a single selected item for restore actions. Bulk restore is intentionally disabled to avoid accidental mass rollback.");
            sb.AppendLine();
            sb.AppendLine("Default restore support among selected: " + defaultSupportedCount + " of " + selectedCount);
            restoreDetailsBox.Text = sb.ToString();
        }

        private static int CompareRestoreEntriesNewestFirst(RestoreEntry left, RestoreEntry right)
        {
            DateTime a;
            DateTime b;
            bool aOk = DateTime.TryParse(left.Created, out a);
            bool bOk = DateTime.TryParse(right.Created, out b);
            if (aOk && bOk)
            {
                int cmp = b.CompareTo(a);
                if (cmp != 0)
                    return cmp;
            }
            return StringComparer.OrdinalIgnoreCase.Compare(right.Id, left.Id);
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
            RestoreEntry entry = restoreListBox.SelectedItem as RestoreEntry;
            if (entry == null)
                return;

            DialogResult result = MessageBox.Show("Restore this item?\r\n\r\n" + entry.Description + "\r\n\r\nTarget:\r\n" + entry.SourcePath, "CK3MPS restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
                return;

            try
            {
                if (entry.Kind == "file" || entry.Kind == "moved_file")
                    RestoreFileEntry(entry);
                else if (entry.Kind == "created_file")
                    RestoreCreatedFileEntry(entry);
                else if (entry.Kind == "directory" || entry.Kind == "moved_directory")
                    RestoreDirectoryEntry(entry);
                else if (entry.Kind == "registry")
                    RestoreRegistryEntry(entry);
                else
                    throw new InvalidOperationException("This restore entry is informational. Use the details text or Windows restore point for this item.");

                Log("OK   Restored: " + entry.Description);
                RefreshRestoreList();
            }
            catch (Exception ex)
            {
                Log("ERROR Restore failed: " + ex.Message);
                MessageBox.Show(ex.Message, "CK3MPS restore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RestoreSelectedItemToDefault()
        {
            RestoreEntry entry = restoreListBox.SelectedItem as RestoreEntry;
            if (entry == null)
                return;

            if (!DefaultRestoreSupported(entry))
            {
                MessageBox.Show("Default restore is not supported for this item. Use Restore selected to restore the recorded previous value.", "CK3MPS restore", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult result = MessageBox.Show(
                "Reset this item to game/launcher/Windows default behavior?\r\n\r\n" + entry.SourcePath + "\r\n\r\nCK3MPS will back up the current value first, then remove the override/file so the owner can recreate defaults.",
                "CK3MPS default restore",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
                return;

            try
            {
                RestoreDefaultEntry(entry);
                Log("OK   Restored default behavior: " + entry.Description);
                RefreshRestoreList();
            }
            catch (Exception ex)
            {
                Log("ERROR Default restore failed: " + ex.Message);
                MessageBox.Show(ex.Message, "CK3MPS restore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DeleteSelectedRestoreEntries()
        {
            List<RestoreEntry> selectedEntries = new List<RestoreEntry>();
            foreach (object item in restoreListBox.SelectedItems)
            {
                RestoreEntry entry = item as RestoreEntry;
                if (entry != null)
                    selectedEntries.Add(entry);
            }

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
            File.WriteAllLines(manifest, kept.ToArray(), Encoding.UTF8);

            HashSet<string> remainingBackups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RestoreEntry existing in ReadRestoreEntries())
                if (!String.IsNullOrEmpty(existing.BackupPath))
                    remainingBackups.Add(existing.BackupPath);

            foreach (RestoreEntry removed in entries)
            {
                if (!toDelete.Contains(removed.Id) || String.IsNullOrEmpty(removed.BackupPath) || remainingBackups.Contains(removed.BackupPath))
                    continue;

                if (File.Exists(removed.BackupPath))
                    File.Delete(removed.BackupPath);
                else if (Directory.Exists(removed.BackupPath))
                    Directory.Delete(removed.BackupPath, true);
            }
        }

        private bool DefaultRestoreSupported(RestoreEntry entry)
        {
            if (entry.Kind == "registry")
                return true;
            if (IsSteamLocalConfigEntry(entry) || IsSteamSharedConfigEntry(entry))
                return true;
            if (entry.Kind == "file" || entry.Kind == "created_file" || entry.Kind == "moved_file" || entry.Kind == "directory" || entry.Kind == "moved_directory")
                return IsOwnedByCk3OrParadoxLauncher(entry.SourcePath);
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
            if (!String.IsNullOrEmpty(ck3Docs) && path.StartsWith(ck3Docs, StringComparison.OrdinalIgnoreCase))
                return true;
            string localLauncher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paradox Interactive", "launcher-v2");
            string roamingLauncher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paradox Interactive", "launcher-v2");
            return RestoreManifestUtilities.IsOwnedByCk3OrParadoxLauncher(path, ck3Docs, localLauncher, roamingLauncher);
        }

        private void RestoreDefaultEntry(RestoreEntry entry)
        {
            string beforeNow = DescribeRestoreCurrentState(entry);
            if (IsSteamLocalConfigEntry(entry))
            {
                bool changed = RemoveSteamLaunchOptionsOverride();
                RecordRestoreEntry("default_restore", entry.SourcePath, "", "Default restore: removed Steam LaunchOptions override for CK3.", beforeNow, NullText(ExtractSteamLaunchOptions()), changed ? "restored_default" : "already_default");
                return;
            }

            if (IsSteamSharedConfigEntry(entry))
            {
                bool changed = RemoveSteamCloudOverride();
                RecordRestoreEntry("default_restore", entry.SourcePath, "", "Default restore: removed Steam Cloud override for CK3.", beforeNow, SteamCloudDisabledOrUnknownQuiet() ? "cloud override removed/unknown" : "cloud flag visible", changed ? "restored_default" : "already_default");
                return;
            }

            if (entry.Kind == "registry")
            {
                RegistryKey root;
                string subKey;
                string name;
                ParseRegistryRestorePath(entry.SourcePath, out root, out subKey, out name);
                using (RegistryKey key = root.OpenSubKey(subKey, true))
                {
                    if (key != null)
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

            RecordRestoreEntry("default_restore", entry.SourcePath, "", "Default restore: removed CK3/Launcher override so owner can recreate defaults: " + entry.SourcePath, beforeNow, "(missing/default)", "restored_default");
        }

        private void RestoreFileEntry(RestoreEntry entry)
        {
            if (!File.Exists(entry.BackupPath))
                throw new FileNotFoundException("Backup file is missing.", entry.BackupPath);

            string dir = Path.GetDirectoryName(entry.SourcePath);
            if (!String.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            if (File.Exists(entry.SourcePath))
                BackupForRestore(entry.SourcePath, "Pre-restore backup of current file: " + entry.SourcePath);
            File.Copy(entry.BackupPath, entry.SourcePath, true);
            RecordRestoreEntry("restore_action", entry.SourcePath, entry.BackupPath, "Restored file: " + entry.SourcePath, entry.Before, DescribePath(entry.SourcePath), "restored");
        }

        private void RestoreCreatedFileEntry(RestoreEntry entry)
        {
            if (!File.Exists(entry.SourcePath))
            {
                RecordRestoreEntry("restore_action", entry.SourcePath, "", "Created file was already missing: " + entry.SourcePath, entry.Before, "(missing)", "restored");
                return;
            }

            BackupForRestore(entry.SourcePath, "Pre-restore backup of created file before deleting it: " + entry.SourcePath);
            File.Delete(entry.SourcePath);
            RecordRestoreEntry("restore_action", entry.SourcePath, "", "Deleted CK3MPS-created file: " + entry.SourcePath, entry.Before, "(missing)", "restored");
        }

        private void RestoreDirectoryEntry(RestoreEntry entry)
        {
            if (!Directory.Exists(entry.BackupPath))
                throw new DirectoryNotFoundException("Backup directory is missing: " + entry.BackupPath);

            if (Directory.Exists(entry.SourcePath))
            {
                BackupForRestore(entry.SourcePath, "Pre-restore backup of current directory: " + entry.SourcePath);
                Directory.Delete(entry.SourcePath, true);
            }
            CopyDirectory(entry.BackupPath, entry.SourcePath);
            RecordRestoreEntry("restore_action", entry.SourcePath, entry.BackupPath, "Restored directory: " + entry.SourcePath, entry.Before, DescribePath(entry.SourcePath), "restored");
        }

        private void RestoreRegistryEntry(RestoreEntry entry)
        {
            RegistryKey root;
            string subKey;
            string name;
            ParseRegistryRestorePath(entry.SourcePath, out root, out subKey, out name);

            string beforeNow = ReadRegistryRestoreValue(entry.SourcePath);
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
            int firstSlash = path.IndexOf('\\');
            int lastSlash = path.LastIndexOf('\\');
            if (firstSlash <= 0 || lastSlash <= firstSlash)
                throw new InvalidOperationException("Registry restore path is invalid: " + path);

            string rootText = path.Substring(0, firstSlash);
            subKey = path.Substring(firstSlash + 1, lastSlash - firstSlash - 1);
            name = path.Substring(lastSlash + 1);

            if (String.Equals(rootText, "HKCU", StringComparison.OrdinalIgnoreCase))
                root = Registry.CurrentUser;
            else if (String.Equals(rootText, "HKLM", StringComparison.OrdinalIgnoreCase))
                root = Registry.LocalMachine;
            else
                throw new InvalidOperationException("Registry root is not supported: " + rootText);
        }
    }
}
