using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
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
            AutoDetectPaths();
            DeleteLegacyPathOverrides();
            SaveAppConfig();
            UpdatePathStatusIndicators();
            Log("INFO Paths reset to automatic detection.");
        }

        private string AppConfigFile()
        {
            if (portableMode)
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CK3MPS.settings.ini");
            return Path.Combine(stabilizerRoot, "settings.ini");
        }

        private string LegacyPathOverridesFile()
        {
            return Path.Combine(stabilizerRoot, "paths.ini");
        }

        private void LoadAppConfig()
        {
            try
            {
                LoadLegacyPathOverrides();
                LoadConfigFile(AppConfigFile());
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
                    ApplySettingsFolder(value);
                else if (String.Equals(key, "ck3Install", StringComparison.OrdinalIgnoreCase) && Directory.Exists(value))
                    ApplyGameFolder(value);
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
                    ApplySettingsFolder(value);
                else if (String.Equals(key, "ck3Install", StringComparison.OrdinalIgnoreCase) && Directory.Exists(value))
                    ApplyGameFolder(value);
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
            EnsureStabilizerRoot();
            File.WriteAllLines(AppConfigFile(), new[]
            {
                "ck3Docs=" + ck3Docs,
                "ck3Install=" + ck3Install,
                "updateCheckOnStartup=" + updateCheckOnStartup,
                "portableMode=" + portableMode,
                "logVerbosity=" + logVerbosity
            }, Encoding.UTF8);
        }

        private void UpdateSettingsUi()
        {
            updateOnStartupBox.Checked = updateCheckOnStartup;
            portableModeBox.Checked = portableMode;
            if (!logVerbosityBox.Items.Contains(logVerbosity))
                logVerbosity = "Normal";
            logVerbosityBox.SelectedItem = logVerbosity;
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
