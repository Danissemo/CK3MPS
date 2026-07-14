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

            public override string ToString()
            {
                return Created + " | " + Kind + " | " + Description;
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
                File.WriteAllText(manifest, "id\tcreated\tkind\tsource\tbackup\tdescription\tbefore\tafter\tstatus\r\n", Encoding.UTF8);

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
                            before = kind + ":" + Convert.ToString(value);
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

            string line = String.Join("\t", new[]
            {
                Guid.NewGuid().ToString("N"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Tsv(kind),
                Tsv(source),
                Tsv(backup),
                Tsv(description),
                Tsv(before),
                Tsv(after),
                Tsv(status)
            });
            File.AppendAllText(manifest, line + Environment.NewLine, Encoding.UTF8);
        }

        private string Tsv(string value)
        {
            return (value ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
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
                entries.Add(new RestoreEntry
                {
                    Id = parts[0],
                    Created = parts[1],
                    Kind = parts[2],
                    SourcePath = parts[3],
                    BackupPath = parts[4],
                    Description = parts[5],
                    Before = parts[6],
                    After = parts[7],
                    Status = parts[8]
                });
            }
            return entries;
        }

        private void RefreshRestoreList()
        {
            restoreListBox.Items.Clear();
            foreach (RestoreEntry entry in ReadRestoreEntries())
                restoreListBox.Items.Add(entry);
            restoreDetailsBox.Text = restoreListBox.Items.Count == 0 ? "(no restore entries yet)" : "";
        }

        private void ShowSelectedRestoreDetails()
        {
            RestoreEntry entry = restoreListBox.SelectedItem as RestoreEntry;
            if (entry == null)
            {
                restoreDetailsBox.Text = "";
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Created: " + entry.Created);
            sb.AppendLine("Kind: " + entry.Kind);
            sb.AppendLine("Description: " + entry.Description);
            sb.AppendLine("Original path: " + entry.SourcePath);
            sb.AppendLine("Backup/quarantine path: " + entry.BackupPath);
            sb.AppendLine();
            sb.AppendLine("Before:");
            sb.AppendLine(entry.Before);
            sb.AppendLine();
            sb.AppendLine("After:");
            sb.AppendLine(entry.After);
            sb.AppendLine();
            sb.AppendLine("Current now:");
            sb.AppendLine(DescribeRestoreCurrentState(entry));
            sb.AppendLine();
            sb.AppendLine("Status:");
            sb.AppendLine(entry.Status);
            restoreDetailsBox.Text = sb.ToString();
        }

        private string DescribeRestoreCurrentState(RestoreEntry entry)
        {
            if (entry.Kind == "registry")
                return ReadRegistryRestoreValue(entry.SourcePath);
            if (entry.Kind == "system_snapshot" || entry.Kind == "snapshot")
                return "Informational snapshot. See backup/quarantine path.";
            return DescribePath(entry.SourcePath);
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
            if (kind == RegistryValueKind.DWord)
                return Int32.Parse(value);
            if (kind == RegistryValueKind.QWord)
                return Int64.Parse(value);
            if (kind == RegistryValueKind.MultiString)
                return value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            return value;
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
                    return key.GetValueKind(name) + ":" + Convert.ToString(value);
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
