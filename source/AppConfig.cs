using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private void AutoDetectPaths()
        {
            ck3Docs = Ck3PathUtilities.DefaultSettingsFolder();
            steamRoot = DetectSteamRoot();
            appManifest = DetectManifest();
            ck3Install = DetectInstallPath();
            settingsPathOverrideActive = false;
            gamePathOverrideActive = false;
            RefreshDerivedPaths();
        }

        private void RefreshDerivedPaths()
        {
            ck3Bin = String.IsNullOrEmpty(ck3Install) ? "" : Path.Combine(ck3Install, "binaries");
            localConfig = DetectLocalConfig();
            sharedConfig = DetectSharedConfig();
        }

        private void ResetPathsToAutoDetect()
        {
            string oldGame = ck3Install;
            string oldSettings = ck3Docs;
            AutoDetectPaths();
            DeleteLegacyPathOverrides();
            SaveAppConfig();
            UpdatePathStatusIndicators();
            statusLabel.Text = BuildResetSummary(oldGame, ck3Install, oldSettings, ck3Docs, true, true);
            Log("INFO Paths reset to automatic detection.");
        }

        private void RefreshStabilizerRoot()
        {
            stabilizerRoot = RuntimeModeUtilities.ResolveStabilizerRoot(nonPortableStabilizerRoot, portableStabilizerRoot, portableMode);
        }

        private string AppConfigFile()
        {
            return Path.Combine(stabilizerRoot, "settings.ini");
        }

        private string PortableAppConfigFile()
        {
            return Path.Combine(portableStabilizerRoot, "settings.ini");
        }

        private string LegacyPortableAppConfigFile()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CK3MPS.settings.ini");
        }

        private string NonPortableAppConfigFile()
        {
            return Path.Combine(nonPortableStabilizerRoot, "settings.ini");
        }

        private string LegacyPathOverridesFile()
        {
            return Path.Combine(nonPortableStabilizerRoot, "paths.ini");
        }

        private void LoadAppConfig()
        {
            try
            {
                LoadLegacyPathOverrides();
                if (File.Exists(PortableAppConfigFile()))
                    LoadConfigFile(PortableAppConfigFile());
                else if (File.Exists(LegacyPortableAppConfigFile()))
                    LoadConfigFile(LegacyPortableAppConfigFile());
                else
                    LoadConfigFile(NonPortableAppConfigFile());
                RefreshStabilizerRoot();
            }
            catch
            {
                // Settings are optional. Bad config should not block startup or recovery.
            }
        }

        private void LoadConfigFile(string path)
        {
            if (!File.Exists(path))
                return;

            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();

                if (String.Equals(key, "ck3Docs", StringComparison.OrdinalIgnoreCase) && Directory.Exists(value))
                {
                    ApplySettingsFolder(value);
                    settingsPathOverrideActive = true;
                }
                else if (String.Equals(key, "ck3Install", StringComparison.OrdinalIgnoreCase) && Directory.Exists(value))
                {
                    ApplyGameFolder(value);
                    gamePathOverrideActive = true;
                }
                else if (String.Equals(key, "updateCheckOnStartup", StringComparison.OrdinalIgnoreCase))
                    updateCheckOnStartup = ParseBool(value, true);
                else if (String.Equals(key, "portableMode", StringComparison.OrdinalIgnoreCase))
                    portableMode = ParseBool(value, false);
                else if (String.Equals(key, "logVerbosity", StringComparison.OrdinalIgnoreCase) && !String.IsNullOrEmpty(value))
                    logVerbosity = value;
            }
        }

        private void LoadLegacyPathOverrides()
        {
            string path = LegacyPathOverridesFile();
            if (!File.Exists(path))
                return;

            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (String.IsNullOrEmpty(value))
                    continue;

                if (String.Equals(key, "ck3Docs", StringComparison.OrdinalIgnoreCase) && Directory.Exists(value))
                {
                    ApplySettingsFolder(value);
                    settingsPathOverrideActive = true;
                }
                else if (String.Equals(key, "ck3Install", StringComparison.OrdinalIgnoreCase) && Directory.Exists(value))
                {
                    ApplyGameFolder(value);
                    gamePathOverrideActive = true;
                }
            }
        }

        private void DeleteLegacyPathOverrides()
        {
            string path = LegacyPathOverridesFile();
            if (File.Exists(path))
                File.Delete(path);
        }

        private bool ParseBool(string value, bool fallback)
        {
            if (String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
                return true;
            if (String.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
                return false;
            return fallback;
        }

        private void SaveAppConfig()
        {
            RefreshStabilizerRoot();
            EnsureStabilizerRoot();
            string targetPath = AppConfigFile();
            List<string> otherPaths = new List<string>
            {
                PortableAppConfigFile(),
                NonPortableAppConfigFile(),
                LegacyPortableAppConfigFile()
            };

            File.WriteAllLines(targetPath, new[]
            {
                settingsPathOverrideActive ? "ck3Docs=" + ck3Docs : "",
                gamePathOverrideActive ? "ck3Install=" + ck3Install : "",
                "updateCheckOnStartup=" + updateCheckOnStartup,
                "portableMode=" + portableMode,
                "logVerbosity=" + logVerbosity
            }, Encoding.UTF8);

            foreach (string path in otherPaths)
                if (!String.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                    File.Delete(path);
        }

        private async Task SetPortableModeAsync(bool enabled)
        {
            if (portableMode == enabled && String.Equals(stabilizerRoot, RuntimeModeUtilities.ResolveStabilizerRoot(nonPortableStabilizerRoot, portableStabilizerRoot, enabled), StringComparison.OrdinalIgnoreCase))
                return;

            string oldRoot = stabilizerRoot;
            string oldLiveLogPath = liveLogFilePath;

            portableModeChangeInProgress = true;
            portableModeBox.Enabled = false;
            try
            {
                portableMode = enabled;
                RefreshStabilizerRoot();
                string newRoot = stabilizerRoot;
                statusLabel.Text = "Moving CK3MPS state for portable mode...";

                await Task.Run(delegate { MoveStabilizerRootContents(oldRoot, newRoot); });

                RelinkLiveLogPath(oldRoot, newRoot, oldLiveLogPath);
                SaveAppConfig();

                statusLabel.Text = enabled
                    ? "Portable mode enabled. CK3MPS state was moved next to the exe."
                    : "Portable mode disabled. CK3MPS state was moved back to Documents.";
                Log("INFO Portable mode " + (enabled ? "enabled" : "disabled") + ". State root: " + newRoot);
                LogVerbose("Portable mode migration: " + oldRoot + " -> " + newRoot);
                LogVerbose("Settings file: " + AppConfigFile());
            }
            catch
            {
                portableMode = !enabled;
                RefreshStabilizerRoot();
                UpdateSettingsUi();
                statusLabel.Text = "Portable mode change failed.";
                throw;
            }
            finally
            {
                portableModeChangeInProgress = false;
                portableModeBox.Enabled = true;
            }
        }

        private void MoveStabilizerRootContents(string sourceRoot, string targetRoot)
        {
            if (String.IsNullOrEmpty(sourceRoot) || String.IsNullOrEmpty(targetRoot)
                || String.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(sourceRoot))
                return;

            Directory.CreateDirectory(targetRoot);

            foreach (string file in Directory.GetFiles(sourceRoot, "*", SearchOption.TopDirectoryOnly))
                MovePathWithMerge(file, Path.Combine(targetRoot, Path.GetFileName(file)));

            foreach (string dir in Directory.GetDirectories(sourceRoot, "*", SearchOption.TopDirectoryOnly))
                MovePathWithMerge(dir, Path.Combine(targetRoot, Path.GetFileName(dir)));

            TryDeleteIfEmpty(sourceRoot);
        }

        private void MovePathWithMerge(string sourcePath, string targetPath)
        {
            if (File.Exists(sourcePath))
            {
                string targetDir = Path.GetDirectoryName(targetPath);
                if (!String.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);
                File.Copy(sourcePath, targetPath, true);
                File.Delete(sourcePath);
                return;
            }

            if (!Directory.Exists(sourcePath))
                return;

            Directory.CreateDirectory(targetPath);
            foreach (string file in Directory.GetFiles(sourcePath, "*", SearchOption.TopDirectoryOnly))
                MovePathWithMerge(file, Path.Combine(targetPath, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(sourcePath, "*", SearchOption.TopDirectoryOnly))
                MovePathWithMerge(dir, Path.Combine(targetPath, Path.GetFileName(dir)));
            TryDeleteIfEmpty(sourcePath);
        }

        private void TryDeleteIfEmpty(string path)
        {
            if (!Directory.Exists(path))
                return;

            if (Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly).Length != 0)
                return;
            if (Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly).Length != 0)
                return;

            Directory.Delete(path, false);
        }

        private void RelinkLiveLogPath(string oldRoot, string newRoot, string oldLiveLogPath)
        {
            if (!String.IsNullOrEmpty(oldLiveLogPath)
                && !String.IsNullOrEmpty(oldRoot)
                && !String.IsNullOrEmpty(newRoot)
                && oldLiveLogPath.StartsWith(oldRoot, StringComparison.OrdinalIgnoreCase))
            {
                string relative = oldLiveLogPath.Substring(oldRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidate = Path.Combine(newRoot, relative);
                if (File.Exists(candidate))
                {
                    liveLogFilePath = candidate;
                    return;
                }
            }

            InitializeLiveLogFile();
        }

        private string BuildResetSummary(string oldGame, string newGame, string oldSettings, string newSettings, bool includeGame, bool includeSettings)
        {
            List<string> parts = new List<string>();
            if (includeGame)
                parts.Add(String.Equals(oldGame, newGame, StringComparison.OrdinalIgnoreCase)
                    ? "game folder was already auto-detected"
                    : "game folder reset to detected Steam path");
            if (includeSettings)
                parts.Add(String.Equals(oldSettings, newSettings, StringComparison.OrdinalIgnoreCase)
                    ? "settings folder was already using Documents default"
                    : "settings folder reset to Documents default");
            return Char.ToUpperInvariant(parts[0][0]) + parts[0].Substring(1) + (parts.Count > 1 ? "; " + parts[1] + "." : ".");
        }

        private void UpdateSettingsUi()
        {
            updatingSettingsUi = true;
            try
            {
                updateOnStartupBox.Checked = updateCheckOnStartup;
                portableModeBox.Checked = portableMode;
                if (!logVerbosityBox.Items.Contains(logVerbosity))
                    logVerbosity = "Normal";
                logVerbosityBox.SelectedItem = logVerbosity;
            }
            finally
            {
                updatingSettingsUi = false;
            }
        }

        private bool GameFolderValid()
        {
            return Ck3PathUtilities.IsValidGameFolder(ck3Install);
        }

        private bool SettingsFolderValid()
        {
            return Ck3PathUtilities.IsValidSettingsFolder(ck3Docs);
        }

        private bool ValidateBeforeRun()
        {
            UpdatePathStatusIndicators();
            LogSection("Pre-run validation");
            Check("CK3 game folder contains binaries\\ck3.exe", GameFolderValid());
            Check("CK3 settings/saves folder looks valid", SettingsFolderValid());
            Check("CK3 and launcher are closed", !IsGameRunning());
            Check("Steam localconfig.vdf found", !String.IsNullOrEmpty(localConfig) && File.Exists(localConfig));
            Check("Steam sharedconfig.vdf found", !String.IsNullOrEmpty(sharedConfig) && File.Exists(sharedConfig));
            Check("Steam CK3 appmanifest found", !String.IsNullOrEmpty(appManifest) && File.Exists(appManifest));

            if (!SettingsFolderValid())
            {
                MessageBox.Show("The selected settings/saves folder does not look like a CK3 profile folder. Open the Paths tab and choose the Crusader Kings III folder under Documents.", "CK3MPS path validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!GameFolderValid())
            {
                DialogResult result = MessageBox.Show("The CK3 game folder was not validated because binaries\\ck3.exe was not found. Continue anyway? Steam/binary checks may be skipped.", "CK3MPS path validation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                return result == DialogResult.Yes;
            }

            return true;
        }

        private string HistoryFile()
        {
            EnsureStabilizerRoot();
            return Path.Combine(stabilizerRoot, "run_history.txt");
        }

        private void AppendRunHistory(string mode, string result)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    + " | mode=" + mode
                    + " | preset=" + NullText(Convert.ToString(presetBox.SelectedItem))
                    + " | result=" + result
                    + " | readiness_failures=" + lastReadinessFailures
                    + " | game=" + NullText(ck3Install)
                    + " | settings=" + NullText(ck3Docs);
                File.AppendAllText(HistoryFile(), line + Environment.NewLine, Encoding.UTF8);
                RefreshHistoryView();
            }
            catch (Exception ex)
            {
                Log("WARN Run history could not be updated: " + ex.Message);
            }
        }

        private void RefreshHistoryView()
        {
            if (historyBox == null)
                return;

            string path = HistoryFile();
            if (!File.Exists(path))
            {
                historyBox.Text = "(no run history yet)";
                return;
            }

            List<string> lines = new List<string>(File.ReadAllLines(path, Encoding.UTF8));
            int skip = Math.Max(0, lines.Count - 200);
            historyBox.Text = String.Join(Environment.NewLine, lines.GetRange(skip, lines.Count - skip).ToArray());
        }

        private void ExportSupportPackage()
        {
            try
            {
                EnsureStabilizerRoot();
                string exportDir = Path.Combine(stabilizerRoot, "support_package_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(exportDir);

                WriteSupportPackageSummary(exportDir);
                CopyIfExists(HistoryFile(), Path.Combine(exportDir, "run_history.txt"));
                CopyIfExists(StabilizerFile("ck3_stabilizer_last_report.txt"), Path.Combine(exportDir, "last_report.txt"));
                CopyIfExists(StabilizerFile("ck3_stabilizer_check_only_report.txt"), Path.Combine(exportDir, "check_only_report.txt"));
                CopyIfExists(StabilizerFile("ck3_stabilizer_runtime_verification.txt"), Path.Combine(exportDir, "runtime_verification.txt"));
                CopyIfExists(StabilizerFile("ck3_stabilizer_latest_oos_summary.txt"), Path.Combine(exportDir, "latest_oos_summary.txt"));
                CopyIfExists(StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt"), Path.Combine(exportDir, "mp_parity_manifest.txt"));
                CopyIfExists(StabilizerFile("ck3_stabilizer_oos_risk_score.txt"), Path.Combine(exportDir, "oos_risk_score.txt"));
                CopyIfExists(StabilizerFile("ck3_stabilizer_evidence_pack_index.txt"), Path.Combine(exportDir, "evidence_pack_index.txt"));

                Log("FILE Support package exported: " + exportDir);
                Process.Start("explorer.exe", exportDir);
            }
            catch (Exception ex)
            {
                Log("ERROR Support package export failed: " + ex.Message);
                MessageBox.Show(ex.Message, "CK3MPS support package", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WriteSupportPackageSummary(string exportDir)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3MPS support package");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("App version: " + AppVersion);
            sb.AppendLine("Game folder: " + NullText(ck3Install));
            sb.AppendLine("Game folder valid: " + YesNo(GameFolderValid()));
            sb.AppendLine("Settings/saves folder: " + NullText(ck3Docs));
            sb.AppendLine("Settings/saves valid: " + YesNo(SettingsFolderValid()));
            sb.AppendLine("Steam root: " + NullText(steamRoot));
            sb.AppendLine("App manifest: " + NullText(appManifest));
            sb.AppendLine("Local config: " + NullText(localConfig));
            sb.AppendLine("Shared config: " + NullText(sharedConfig));
            sb.AppendLine("Installed CK3 version: " + NullText(DetectInstalledVersion()));
            sb.AppendLine("Active save version: " + NullText(DetectActiveSaveVersion()));
            sb.AppendLine("Steam build: " + NullText(DetectBuildId()));
            sb.AppendLine("CK3 running: " + YesNo(IsGameRunning()));
            sb.AppendLine("Last readiness failures: " + lastReadinessFailures);
            File.WriteAllText(Path.Combine(exportDir, "summary.txt"), sb.ToString(), Encoding.UTF8);
        }

        private void CopyIfExists(string source, string dest)
        {
            if (!String.IsNullOrEmpty(source) && File.Exists(source))
                File.Copy(source, dest, true);
        }
    }
}
