using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private void BackupFile(string path)
        {
            if (String.IsNullOrEmpty(lastQuarantine) || String.IsNullOrEmpty(path))
                return;
            if (!File.Exists(path))
            {
                RecordCreatedFileForRestore(path, "Before CK3MPS creates file: " + path);
                return;
            }
            BackupForRestore(path, "Before CK3MPS changes file: " + path);
            string dest = Path.Combine(lastQuarantine, "settings_backup", SafeFileName(path) + ".bak");
            File.Copy(path, UniquePath(dest), true);
        }

        private void BackupNamedFile(string path, string destDir)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path))
                return;
            Directory.CreateDirectory(destDir);
            BackupForRestore(path, "Explicit backup before CK3MPS run: " + path);
            string dest = UniquePath(Path.Combine(destDir, Path.GetFileName(path) + ".bak"));
            File.Copy(path, dest, true);
            Log("Backed up: " + path + " -> " + dest);
        }

        private void MoveChildren(string dir, string destDir)
        {
            if (!Directory.Exists(dir))
                return;
            foreach (string item in Directory.GetFileSystemEntries(dir))
                MoveToQuarantine(item, destDir);
        }

        private void MoveToQuarantine(string source, string destDir)
        {
            if (!File.Exists(source) && !Directory.Exists(source))
                return;

            Directory.CreateDirectory(destDir);
            string dest = UniquePath(Path.Combine(destDir, Path.GetFileName(source)));
            try
            {
                if (File.Exists(source))
                    File.Move(source, dest);
                else
                    Directory.Move(source, dest);
                RecordMovedForRestore(source, dest, "Moved to quarantine: " + source);
                Log("Moved: " + source + " -> " + dest);
            }
            catch (Exception ex)
            {
                Log("Could not move " + source + ": " + ex.Message);
            }
        }

        private string SetSettingBlock(string text, string key, string body)
        {
            string pattern = "(\"" + Regex.Escape(key) + "\"\\s*=\\s*\\{)(.*?)(\\r?\\n\\s*\\})";
            string replacement = "\"" + key + "\"={\r\n\t\t" + body + "\r\n\t}";
            if (Regex.IsMatch(text, pattern, RegexOptions.Singleline))
                return Regex.Replace(text, pattern, replacement, RegexOptions.Singleline);
            if (!text.EndsWith("\r\n", StringComparison.Ordinal))
                text += "\r\n";
            return text + "\"" + key + "\"={\r\n\t\t" + body + "\r\n\t}\r\n";
        }

        private string SetSectionSettingBlock(string text, string section, string key, string body)
        {
            Match sectionMatch = Regex.Match(text ?? "", "\"" + Regex.Escape(section) + "\"\\s*=\\s*\\{", RegexOptions.IgnoreCase);
            if (!sectionMatch.Success)
            {
                if (!String.IsNullOrEmpty(text) && !text.EndsWith("\r\n", StringComparison.Ordinal))
                    text += "\r\n";
                text += "\"" + section + "\"={\r\n}\r\n";
                sectionMatch = Regex.Match(text, "\"" + Regex.Escape(section) + "\"\\s*=\\s*\\{", RegexOptions.IgnoreCase);
            }

            int open = text.IndexOf('{', sectionMatch.Index);
            int close = FindMatchingBrace(text, open);
            if (open < 0 || close <= open)
                return text;

            string sectionBody = text.Substring(open + 1, close - open - 1);
            string updatedBody = SetSettingBlock(sectionBody, key, body);
            if (!updatedBody.StartsWith("\r\n", StringComparison.Ordinal))
                updatedBody = "\r\n" + updatedBody;
            if (!updatedBody.EndsWith("\r\n", StringComparison.Ordinal))
                updatedBody += "\r\n";

            return text.Substring(0, open + 1) + updatedBody + text.Substring(close);
        }

        private string ExtractSectionBody(string text, string section)
        {
            Match sectionMatch = Regex.Match(text ?? "", "\"" + Regex.Escape(section) + "\"\\s*=\\s*\\{", RegexOptions.IgnoreCase);
            if (!sectionMatch.Success)
                return "";

            int open = text.IndexOf('{', sectionMatch.Index);
            int close = FindMatchingBrace(text, open);
            if (open < 0 || close <= open)
                return "";

            return text.Substring(open + 1, close - open - 1);
        }

        private bool SectionSettingMatches(string text, string section, string key, string settingPattern)
        {
            string body = ExtractSectionBody(text, section);
            if (String.IsNullOrEmpty(body))
                return false;

            string pattern = "\"" + Regex.Escape(key) + "\"\\s*=\\s*\\{\\s*" + settingPattern;
            return Regex.IsMatch(body, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }

        private int FindMatchingBrace(string text, int openIndex)
        {
            if (openIndex < 0 || openIndex >= text.Length)
                return -1;
            int depth = 0;
            bool inString = false;
            for (int i = openIndex; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' && (i == 0 || text[i - 1] != '\\'))
                    inString = !inString;
                if (inString)
                    continue;
                if (c == '{')
                    depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return -1;
        }

        private string UniquePath(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            int i = 1;
            while (true)
            {
                string candidate = Path.Combine(dir, name + "_" + i + ext);
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                    return candidate;
                i++;
            }
        }

        private string SafeFileName(string path)
        {
            string s = path.Replace(':', '_').Replace('\\', '_').Replace('/', '_');
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        private string FileHashOrMissing(string path)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path))
                return "(missing)";

            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = File.OpenRead(path))
                {
                    byte[] hash = sha.ComputeHash(stream);
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in hash)
                        sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                return "(hash failed: " + ex.Message + ")";
            }
        }

        private string FindLatestLocalSave()
        {
            string saveDir = Path.Combine(ck3Docs, "save games");
            if (!Directory.Exists(saveDir))
                return "";

            FileInfo[] saves = new DirectoryInfo(saveDir).GetFiles("*.ck3");
            if (saves.Length == 0)
                return "";

            Array.Sort(saves, delegate (FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
            return saves[0].FullName;
        }

        private string FileSizeOrMissing(string path)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path))
                return "(missing)";

            long bytes = new FileInfo(path).Length;
            if (bytes > 1024L * 1024L * 1024L)
                return (bytes / 1024d / 1024d / 1024d).ToString("0.00") + " GB";
            if (bytes > 1024L * 1024L)
                return (bytes / 1024d / 1024d).ToString("0.00") + " MB";
            if (bytes > 1024L)
                return (bytes / 1024d).ToString("0.00") + " KB";
            return bytes + " B";
        }

        private string ListFileNames(string dir, string pattern)
        {
            if (!Directory.Exists(dir))
                return "(folder missing)";

            string[] files = Directory.GetFiles(dir, pattern);
            if (files.Length == 0)
                return "(none)";

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            List<string> names = new List<string>();
            foreach (string file in files)
                names.Add(Path.GetFileName(file));
            return String.Join(", ", names.ToArray());
        }

        private string ExtractJsonArraySummary(string path, string key)
        {
            if (!File.Exists(path))
                return "(missing)";

            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                Match m = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (!m.Success)
                    return "(not found)";
                string value = Regex.Replace(m.Groups[1].Value, "\\s+", " ").Trim();
                return String.IsNullOrEmpty(value) ? "(empty)" : value;
            }
            catch (Exception ex)
            {
                return "(read failed: " + ex.Message + ")";
            }
        }

        private int CountSuspiciousSaveNames()
        {
            string saveDir = Path.Combine(ck3Docs, "save games");
            if (!Directory.Exists(saveDir))
                return 0;

            int count = 0;
            foreach (string file in Directory.GetFiles(saveDir, "*.ck3", SearchOption.TopDirectoryOnly))
            {
                if (IsSuspiciousSaveName(Path.GetFileName(file)))
                    count++;
            }
            return count;
        }

        private List<string> DetectSteamCloudSaveDirs()
        {
            List<string> dirs = new List<string>();
            string userData = Path.Combine(DetectSteamRoot(), "userdata");
            if (!Directory.Exists(userData))
                return dirs;

            foreach (string appDir in Directory.GetDirectories(userData, "1158310", SearchOption.AllDirectories))
            {
                string saveDir = Path.Combine(appDir, "remote", "save games");
                if (Directory.Exists(saveDir))
                    AddUniqueExistingDirectory(dirs, saveDir);
            }
            return dirs;
        }

        private int CountSteamCloudSaveFiles()
        {
            int count = 0;
            foreach (string dir in DetectSteamCloudSaveDirs())
                count += Directory.GetFiles(dir, "*.ck3", SearchOption.TopDirectoryOnly).Length;
            return count;
        }

        private int CountSuspiciousSteamCloudSaveNames()
        {
            int count = 0;
            foreach (string dir in DetectSteamCloudSaveDirs())
                foreach (string file in Directory.GetFiles(dir, "*.ck3", SearchOption.TopDirectoryOnly))
                    if (IsSuspiciousSaveName(Path.GetFileName(file)))
                        count++;
            return count;
        }

        private string BuildSteamCloudSaveFingerprint()
        {
            List<string> items = new List<string>();
            foreach (string dir in DetectSteamCloudSaveDirs())
            {
                string appDir = Directory.GetParent(Directory.GetParent(dir).FullName).FullName;
                string userId = Directory.GetParent(appDir) != null ? Directory.GetParent(appDir).Name : "unknown";
                foreach (string file in Directory.GetFiles(dir, "*.ck3", SearchOption.TopDirectoryOnly))
                    items.Add(userId + ":" + Path.GetFileName(file) + ":" + new FileInfo(file).Length);
            }
            return HashStringList(items);
        }

        private bool SaveLaunchHygieneOk()
        {
            return !ActiveContinueSaveNameSuspicious() && CountSuspiciousSaveNames() == 0;
        }

        private bool ActiveContinueSaveNameSuspicious()
        {
            string title = DetectActiveSaveTitle();
            return IsSuspiciousSaveName(title);
        }

        private bool IsSuspiciousSaveName(string name)
        {
            if (String.IsNullOrEmpty(name))
                return false;

            string lower = name.ToLowerInvariant();
            return lower.Contains("patched")
                || lower.Contains("recovery")
                || lower.Contains("desync")
                || lower.Contains("autosave")
                || lower.Contains("cloud")
                || lower.Contains("backup");
        }

        private string BuildNetworkRouteSummary()
        {
            NetworkRouteProfile profile = AnalyzeNetworkRouteProfile(false);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("- Active IPv4 gateway routes: " + profile.GatewayAdapters);
            sb.AppendLine("- Active IPv6 gateway routes: " + profile.Ipv6GatewayAdapters);
            sb.AppendLine("- Physical routes: " + profile.PhysicalRoutes);
            sb.AppendLine("- Wi-Fi routes: " + profile.WifiRoutes);
            sb.AppendLine("- VPN/virtual routes: " + profile.VpnRoutes);
            sb.AppendLine("- PPPoE routes: " + profile.PppoeRoutes);
            sb.AppendLine("- Mobile/tethering routes: " + profile.MobileRoutes);
            sb.AppendLine("- Low-speed routes: " + profile.LowSpeedRoutes);
            sb.AppendLine("- CGNAT-like addresses: " + profile.CgnatAddresses);
            sb.AppendLine("- Local DNS/filtering servers: " + profile.LocalDnsServers);
            sb.AppendLine("- Windows proxy: " + YesNo(profile.ProxyDetected));
            sb.AppendLine("- Packet loss: " + profile.PacketLossPercent + "%");
            sb.AppendLine("- Max jitter: " + profile.MaxJitterMs + "ms");
            sb.AppendLine("- Route names: " + (profile.RouteNames.Count == 0 ? "(none)" : String.Join("; ", profile.RouteNames.ToArray())));
            if (profile.HasMultipleGateways)
                sb.AppendLine("- Warning: multiple routes can make CK3/Steam/Paradox traffic inconsistent.");
            if (profile.HasWifi)
                sb.AppendLine("- Warning: Wi-Fi route detected; host stability is usually better on Ethernet.");
            if (profile.HasVpn)
                sb.AppendLine("- Warning: VPN/virtual route detected; every player should intentionally use the same route policy.");
            if (profile.HasPppoe)
                sb.AppendLine("- Info: PPPoE detected; do not force MTU/offload globally.");
            if (profile.HasMobile || profile.HasCgnatSignal)
                sb.AppendLine("- Warning: mobile/CGNAT networks can change NAT behavior and should use relay-friendly session setup.");
            return sb.ToString().TrimEnd();
        }

        private string FileTimeHashLine(string path)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path))
                return "(missing)";

            FileInfo info = new FileInfo(path);
            return info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + " | " + FileSizeOrMissing(path) + " | " + FileHashOrMissing(path);
        }

        private void ResetChecks()
        {
            for (int i = 0; i < steps.Items.Count; i++)
                SetStepChecked(i, false);
            RefreshAllGroupStates();
        }

        private void ApplyPreset(string preset)
        {
            if (String.IsNullOrEmpty(preset) || steps.Items.Count == 0)
                return;

            SetAllSteps(false);
            SetStepChecked(1, true);
            SetStepChecked(2, true);

            switch (preset)
            {
                case "Minimum":
                    SetPresetSteps(new[] { 0, 10, 11, 14, 15, 16, 17, 24, 26, 27, 28 });
                    break;

                case "Recommended":
                    ApplyRecommendedPreset();
                    break;

                case "Maximum":
                    SetAllSteps(true);
                    break;

                case "Clean profile only":
                    SetPresetSteps(new[] { 0, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28 });
                    break;

                case "Network only":
                    SetPresetSteps(new[] { 0, 3, 4, 5, 6, 7, 8, 9, 26, 27, 28 });
                    break;

                case "Diagnostic only":
                    SetPresetSteps(new[] { 4, 8, 9, 13, 23, 24, 25, 26, 27, 28 });
                    break;
            }

            RefreshAllGroupStates();
            if (!String.Equals(preset, "Recommended", StringComparison.Ordinal))
                statusLabel.Text = "Preset selected: " + preset + ". You can still change checkboxes manually.";
        }

        private void ApplyRecommendedPreset()
        {
            // Recommended keeps the important reversible CK3 multiplayer fixes, but avoids the
            // highest-risk actions: Windows firewall/registry/adapter tuning, save movement,
            // local .mod descriptor quarantine, and broad CK3 Documents cleanup.
            SetPresetSteps(PresetUtilities.RecommendedStepIndices());

            statusLabel.Text = "Preset selected: Recommended. Applies backed-up CK3/Launcher fixes, but skips Windows tuning, save movement, .mod quarantine and broad folder cleanup.";
        }

        private void SetPresetSteps(int[] indices)
        {
            foreach (int index in indices)
                SetStepChecked(index, true);
        }

        private void SetAllSteps(bool value)
        {
            for (int i = 0; i < steps.Items.Count; i++)
                SetStepChecked(i, value);
            RefreshAllGroupStates();
        }

        private void SetStepChecked(int index, bool value)
        {
            if (index >= 0 && index < steps.Items.Count)
            {
                steps.SetItemChecked(index, value);
                if (index < stepRows.Count && stepRows[index] != null)
                    stepRows[index].CheckBox.Checked = value;
            }
        }

        private bool IsStepChecked(int index)
        {
            return index >= 0 && index < steps.Items.Count && steps.GetItemChecked(index);
        }

        private int CountSelectedSteps()
        {
            int count = 0;
            bool hasAnySelection = false;
            for (int i = 0; i < steps.Items.Count; i++)
            {
                if (IsStepChecked(i))
                {
                    count++;
                    hasAnySelection = true;
                }
            }

            if (hasAnySelection && !IsStepChecked(1))
                count++;
            if (hasAnySelection && !IsStepChecked(2))
                count++;
            return count;
        }

        private void SetBusy(bool busy)
        {
            stabilizeButton.Enabled = !busy;
            checkButton.Enabled = !busy;
            openFolderButton.Enabled = !busy;
            openReportsButton.Enabled = !busy;
            exportSupportButton.Enabled = !busy;
            refreshHistoryButton.Enabled = !busy;
            refreshRestoreButton.Enabled = !busy;
            restoreSelectedButton.Enabled = !busy;
            restoreDefaultButton.Enabled = !busy;
            openQuarantineButton.Enabled = !busy;
            previewButton.Enabled = !busy;
            openGamePathButton.Enabled = !busy;
            openSettingsPathButton.Enabled = !busy;
            resetGamePathButton.Enabled = !busy;
            resetSettingsPathButton.Enabled = !busy;
            updateButton.Enabled = !busy;
            gamePathBrowseButton.Enabled = !busy;
            settingsPathBrowseButton.Enabled = !busy;
            resetPathsButton.Enabled = !busy;
            selectAllButton.Enabled = !busy;
            selectNoneButton.Enabled = !busy;
            presetBox.Enabled = !busy;
            graphicsProfileBox.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void OpenReportsLocation()
        {
            EnsureStabilizerRoot();

            string[] reports = new[]
            {
                StabilizerFile("ck3_stabilizer_last_report.txt"),
                StabilizerFile("ck3_stabilizer_check_only_report.txt"),
                StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt"),
                StabilizerFile("ck3_stabilizer_oos_risk_score.txt"),
                StabilizerFile("ck3_stabilizer_settings_guard.txt"),
                StabilizerFile("ck3_stabilizer_expected_profile_hashes.txt"),
                StabilizerFile("ck3_stabilizer_runtime_verification.txt"),
                StabilizerFile("ck3_stabilizer_pre_session_plan.txt"),
                StabilizerFile("ck3_stabilizer_session_verdict.txt"),
                StabilizerFile("ck3_stabilizer_latest_oos_summary.txt"),
                StabilizerFile("ck3_stabilizer_oos_history.txt"),
                StabilizerFile("ck3_stabilizer_oos_protocol.txt"),
                StabilizerFile("ck3_stabilizer_portable_notes.txt"),
                StabilizerFile("ck3_stabilizer_evidence_pack_index.txt")
            };

            foreach (string report in reports)
            {
                if (File.Exists(report))
                {
                    Process.Start("explorer.exe", "/select,\"" + report + "\"");
                    return;
                }
            }

            Process.Start("explorer.exe", stabilizerRoot);
        }

        private void EnsureStabilizerRoot()
        {
            if (!Directory.Exists(stabilizerRoot))
                Directory.CreateDirectory(stabilizerRoot);
        }

        private void MigrateLegacyStabilizerState()
        {
            try
            {
                string paradoxRoot = Directory.GetParent(stabilizerRoot).FullName;
                string legacyRoot = Path.Combine(paradoxRoot, "CK3Stabilizer");
                if (!Directory.Exists(legacyRoot) || String.Equals(legacyRoot, stabilizerRoot, StringComparison.OrdinalIgnoreCase))
                    return;

                int copied = 0;
                foreach (string file in Directory.GetFiles(legacyRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    string dest = Path.Combine(stabilizerRoot, Path.GetFileName(file));
                    if (File.Exists(dest))
                        continue;
                    File.Copy(file, dest, false);
                    copied++;
                }

                foreach (string dir in Directory.GetDirectories(legacyRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    string dest = Path.Combine(stabilizerRoot, Path.GetFileName(dir));
                    if (Directory.Exists(dest))
                        continue;
                    CopyDirectory(dir, dest);
                    copied++;
                }

                if (copied > 0)
                    Log("INFO Migrated legacy CK3Stabilizer state into CK3MPS: " + copied + " item(s).");
            }
            catch (Exception ex)
            {
                Log("WARN Legacy CK3Stabilizer state migration skipped: " + ex.Message);
            }
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), false);
            foreach (string dir in Directory.GetDirectories(sourceDir))
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }

        private string StabilizerFile(string name)
        {
            EnsureStabilizerRoot();
            return Path.Combine(stabilizerRoot, CompactStabilizerFileName(name));
        }

        private string CompactStabilizerFileName(string name)
        {
            return StabilizerFileNameUtilities.CompactName(name);
        }

        private void MoveLegacyStabilizerArtifacts()
        {
            try
            {
                if (!Directory.Exists(ck3Docs))
                    return;

                EnsureStabilizerRoot();

                foreach (string file in Directory.GetFiles(ck3Docs, "ck3_stabilizer_*", SearchOption.TopDirectoryOnly))
                    MoveLegacyArtifact(file);

                foreach (string dir in Directory.GetDirectories(ck3Docs, "ck3_stabilizer_*", SearchOption.TopDirectoryOnly))
                    MoveLegacyArtifact(dir);

                foreach (string dir in Directory.GetDirectories(ck3Docs, "_ck3_stabilizer_quarantine_*", SearchOption.TopDirectoryOnly))
                    MoveLegacyArtifact(dir);

                string paradoxRoot = Directory.GetParent(stabilizerRoot).FullName;
                foreach (string file in Directory.GetFiles(paradoxRoot, "ck3_stabilizer_*", SearchOption.TopDirectoryOnly))
                    MoveLegacyArtifact(file);

                foreach (string dir in Directory.GetDirectories(paradoxRoot, "ck3_stabilizer_*", SearchOption.TopDirectoryOnly))
                    if (!String.Equals(dir.TrimEnd(Path.DirectorySeparatorChar), stabilizerRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                        MoveLegacyArtifact(dir);

                foreach (string dir in Directory.GetDirectories(paradoxRoot, "_ck3_stabilizer_quarantine_*", SearchOption.TopDirectoryOnly))
                    MoveLegacyArtifact(dir);
            }
            catch (Exception ex)
            {
                Log("WARN Legacy stabilizer cleanup skipped: " + ex.Message);
            }
        }

        private void MoveLegacyArtifact(string path)
        {
            string dest = UniquePath(Path.Combine(stabilizerRoot, Path.GetFileName(path)));
            if (File.Exists(path))
                File.Move(path, dest);
            else if (Directory.Exists(path))
                Directory.Move(path, dest);
        }

        private void LogSection(string title)
        {
            Log("");
            AppendLogLine("------------------------------------------------------------", Color.FromArgb(120, 120, 120));
            AppendLogLine(title, Color.FromArgb(50, 50, 50));
            AppendLogLine("------------------------------------------------------------", Color.FromArgb(120, 120, 120));
        }

        private void Log(string message)
        {
            if (String.IsNullOrEmpty(message))
            {
                AppendLogLine("", logBox.ForeColor);
                return;
            }

            string clean = message.Replace("[OK]", "OK  ")
                .Replace("[FAIL]", "FAIL")
                .Replace("[INFO]", "INFO")
                .Replace("Warning:", "WARN ");
            string formatted = FormatLogLine(clean);
            if (ShouldSuppressLogLine(formatted))
                return;
            AppendLogLine(formatted, LogColorForLine(formatted, clean));
        }

        private bool ShouldSuppressLogLine(string formatted)
        {
            if (!String.Equals(logVerbosity, "Quiet", StringComparison.OrdinalIgnoreCase))
                return false;

            string text = (formatted ?? "").TrimStart();
            return text.StartsWith("INFO", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("SKIP", StringComparison.OrdinalIgnoreCase);
        }

        private void AppendLogLine(string text, Color color)
        {
            AppendLogLineTo(logBox, text, color);
            AppendLogLineTo(logTabBox, text, color);
            AppendLiveLogLine(text);
        }

        private static void AppendLogLineTo(RichTextBox box, string text, Color color)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = color;
            box.AppendText(text + Environment.NewLine);
            box.SelectionColor = box.ForeColor;
            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
        }

        private void ClearLogViews()
        {
            logBox.Clear();
            logTabBox.Clear();
        }

        private string LiveLogsFolder()
        {
            EnsureStabilizerRoot();
            return Path.Combine(stabilizerRoot, "LiveLogs");
        }

        private void InitializeLiveLogFile()
        {
            try
            {
                string folder = LiveLogsFolder();
                Directory.CreateDirectory(folder);
                liveLogFilePath = Path.Combine(folder, "live_log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("CK3MPS live log");
                sb.AppendLine("Started: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("App version: " + AppVersion);
                sb.AppendLine();
                File.WriteAllText(liveLogFilePath, sb.ToString(), Utf8NoBom);
            }
            catch
            {
                liveLogFilePath = "";
            }
        }

        private void AppendLiveLogLine(string text)
        {
            try
            {
                if (String.IsNullOrEmpty(liveLogFilePath))
                    return;
                File.AppendAllText(liveLogFilePath, (text ?? "") + Environment.NewLine, Utf8NoBom);
            }
            catch
            {
                // Live log file must never break the UI log path.
            }
        }

        private Color LogColorForLine(string formatted, string original)
        {
            string text = (formatted ?? "").TrimStart();
            string upper = (text + " " + (original ?? "")).ToUpperInvariant();

            if (upper.StartsWith("OK") || upper.StartsWith("RESULT| READY") || upper.Contains(" RESULT READY"))
                return Color.FromArgb(0, 128, 64);
            if (upper.StartsWith("FAIL") || upper.StartsWith("ERROR") || upper.Contains(" FAILED") || upper.Contains(" NOT READY"))
                return Color.FromArgb(192, 0, 0);
            if (upper.StartsWith("WARN") || upper.StartsWith("RISK") || upper.StartsWith("GUARD"))
                return Color.FromArgb(200, 104, 0);
            if (upper.StartsWith("SKIP"))
                return Color.FromArgb(120, 120, 120);
            if (upper.StartsWith("FILE") || upper.StartsWith("BACKUP") || upper.StartsWith("MOVE") || upper.StartsWith("CMD"))
                return Color.FromArgb(24, 100, 170);
            if (upper.StartsWith("INFO"))
                return Color.FromArgb(70, 70, 70);

            return logBox.ForeColor;
        }

        private void UpdatePathStatusIndicators()
        {
            bool gameExists = !String.IsNullOrEmpty(ck3Install) && Directory.Exists(ck3Install);
            bool settingsExists = !String.IsNullOrEmpty(ck3Docs) && Directory.Exists(ck3Docs);
            bool gameValid = Ck3PathUtilities.IsValidGameFolder(ck3Install);
            bool settingsValid = Ck3PathUtilities.IsValidSettingsFolder(ck3Docs);

            gamePathBox.Text = NullText(ck3Install);
            settingsPathBox.Text = NullText(ck3Docs);
            ApplyPathStatus(gamePathStatusLabel, gameExists, gameValid, ck3Install);
            ApplyPathStatus(settingsPathStatusLabel, settingsExists, settingsValid, ck3Docs);
            pathDetailsLabel.Text = "Game: " + PathValidationReason(ck3Install, true) + Environment.NewLine
                + "Settings/saves: " + PathValidationReason(ck3Docs, false);
        }

        private void ApplyPathStatus(Label label, bool exists, bool valid, string path)
        {
            label.Text = Ck3PathUtilities.ValidationText(exists, valid);
            label.ForeColor = valid ? Color.FromArgb(0, 128, 64) : Color.FromArgb(192, 0, 0);
            label.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            label.Tag = path;
        }

        private string PathValidationReason(string path, bool game)
        {
            if (String.IsNullOrEmpty(path))
                return "(empty)";
            if (!Directory.Exists(path))
                return "folder is missing: " + path;

            if (game)
            {
                string exe = Path.Combine(path, "binaries", "ck3.exe");
                return File.Exists(exe)
                    ? "valid, found binaries\\ck3.exe"
                    : "wrong folder, missing binaries\\ck3.exe";
            }

            List<string> markers = new List<string>();
            if (File.Exists(Path.Combine(path, "pdx_settings.txt"))) markers.Add("pdx_settings.txt");
            if (File.Exists(Path.Combine(path, "dlc_load.json"))) markers.Add("dlc_load.json");
            if (File.Exists(Path.Combine(path, "continue_game.json"))) markers.Add("continue_game.json");
            if (File.Exists(Path.Combine(path, "launcher-v2.sqlite"))) markers.Add("launcher-v2.sqlite");
            if (Directory.Exists(Path.Combine(path, "save games"))) markers.Add("save games");
            if (Directory.Exists(Path.Combine(path, "logs"))) markers.Add("logs");
            return markers.Count == 0
                ? "wrong folder, no CK3 settings/save markers found"
                : "valid, found " + String.Join(", ", markers.ToArray());
        }

        private void OpenPathIfExists(string path)
        {
            if (!String.IsNullOrEmpty(path) && Directory.Exists(path))
                Process.Start("explorer.exe", path);
        }

        private void BrowseForGameFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose the Crusader Kings III game install folder.";
                dialog.SelectedPath = Directory.Exists(ck3Install) ? ck3Install : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                ApplyGameFolder(dialog.SelectedPath);
                RefreshDerivedPaths();
                SaveAppConfig();
                UpdatePathStatusIndicators();
                Log((GameFolderValid() ? "OK   " : "WARN ") + "CK3 game folder selected manually: " + ck3Install);
            }
        }

        private void BrowseForSettingsFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose the Crusader Kings III settings and saves folder.";
                dialog.SelectedPath = Directory.Exists(ck3Docs) ? ck3Docs : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                ApplySettingsFolder(dialog.SelectedPath);
                RefreshDerivedPaths();
                SaveAppConfig();
                UpdatePathStatusIndicators();
                Log((SettingsFolderValid() ? "OK   " : "WARN ") + "CK3 settings/saves folder selected manually: " + ck3Docs);
            }
        }

        private void ApplyGameFolder(string selectedPath)
        {
            string path = Ck3PathUtilities.NormalizeGameFolderSelection(selectedPath);
            if (String.IsNullOrEmpty(path))
                return;

            ck3Install = path;
            ck3Bin = Path.Combine(ck3Install, "binaries");
            gamePathOverrideActive = true;
        }

        private void ApplySettingsFolder(string selectedPath)
        {
            string path = Ck3PathUtilities.NormalizeSettingsFolderSelection(selectedPath);
            if (String.IsNullOrEmpty(path))
                return;

            ck3Docs = path;
            settingsPathOverrideActive = true;
        }

        private string FormatLogLine(string message)
        {
            string trimmed = message.Trim();
            if (trimmed.Length == 0)
                return "";

            if (trimmed.StartsWith("Skipped:", StringComparison.OrdinalIgnoreCase))
                return "SKIP  | " + trimmed.Substring("Skipped:".Length).Trim();
            if (trimmed.StartsWith("Moved:", StringComparison.OrdinalIgnoreCase))
                return "MOVE  | " + trimmed.Substring("Moved:".Length).Trim();
            if (trimmed.StartsWith("Backed up:", StringComparison.OrdinalIgnoreCase))
                return "BACKUP| " + trimmed.Substring("Backed up:".Length).Trim();
            if (trimmed.StartsWith("Report written:", StringComparison.OrdinalIgnoreCase))
                return "FILE  | " + trimmed.Substring("Report written:".Length).Trim();

            string[] known = new[] { "OK", "FAIL", "WARN", "INFO", "CMD", "ERROR", "SKIP", "MOVE", "BACKUP", "FILE", "RISK", "GUARD", "RESULT" };
            foreach (string tag in known)
            {
                if (trimmed.StartsWith(tag, StringComparison.OrdinalIgnoreCase))
                {
                    string rest = trimmed.Substring(tag.Length).TrimStart(' ', '|', ':', '-');
                    return tag.PadRight(6) + "| " + rest;
                }
            }

            if (message.StartsWith("  "))
                return "      | " + trimmed;

            return "      | " + trimmed;
        }

        private string NullText(string value)
        {
            return String.IsNullOrEmpty(value) ? "(not found)" : value;
        }

        private string YesNo(bool value)
        {
            return value ? "yes" : "no";
        }
    }
}



