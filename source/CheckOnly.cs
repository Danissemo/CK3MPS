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
        private void RunOptionalStep(int index, string label, Action action, bool alwaysRun)
        {
            if (alwaysRun)
                SetStepChecked(index, true);

            if (!alwaysRun && !IsStepChecked(index))
            {
                Log("Skipped: " + StepTitle(index));
                return;
            }

            RunStep(index, label, action);
        }

        private void RunCoreStabilizeStep(int index, string label, Action action, bool shouldRun)
        {
            if (!shouldRun)
                return;

            RunStep(index, label, action);
        }

        private void RunPlannedStabilizeStep(int index, string label, Action action)
        {
            if (!IsStepChecked(index))
            {
                Log("Skipped: " + StepTitle(index));
                return;
            }

            if (!ShouldRunSelectedStabilizeStep(index))
            {
                Log("INFO No change needed: " + StepTitle(index) + " | " + GetStabilizeStepSkipReason(index));
                return;
            }

            RunStep(index, label, action);
        }

        private void RunCheckStep(int index, bool advanceProgress)
        {
            string item = StepTitle(index);
            statusLabel.Text = "Checking: " + item;
            LogSection(item);
            Application.DoEvents();

            switch (index)
            {
                case 0:
                    CheckWindowsRestorePointReadOnly();
                    break;
                case 1:
                    CheckBasePathsReadOnly();
                    break;
                case 2:
                    CheckQuarantineReadOnly();
                    break;
                case 3:
                    Log("[INFO] DNS cache flush is a write action, so Check only does not run it.");
                    Check("Network ping baseline", NetworkBaselineOk());
                    break;
                case 4:
                    RunNetworkDiagnostics();
                    break;
                case 5:
                    CheckFirewallRulesReadOnly();
                    break;
                case 6:
                    CheckWindowsGameNetworkProfileReadOnly();
                    break;
                case 7:
                    CheckPowerAdapterProfileReadOnly();
                    break;
                case 8:
                    CheckOverlaysAndVpn();
                    Check("Core Windows network services healthy", RequiredWindowsNetworkServicesOk());
                    Check("Windows power plan is not Power Saver", !PowerSaverPlanActive());
                    break;
                case 9:
                    CheckOnlineServices();
                    break;
                case 10:
                    CheckSteamAndLauncherBackupSources();
                    break;
                case 11:
                    CheckSteamSettingsReadOnly();
                    break;
                case 12:
                    CheckParadoxLauncherReadOnly();
                    break;
                case 13:
                    CheckLauncherRuntimeHygiene();
                    break;
                case 14:
                    Check("No active mods in dlc_load.json", NoActiveMods());
                    Check("No disabled DLCs in dlc_load.json", NoDisabledDlcs());
                    Check("dlc_load.json has no UTF-8 BOM", !HasUtf8Bom(Path.Combine(ck3Docs, "dlc_load.json")));
                    break;
                case 15:
                    Check("Core stable pdx_settings.txt", StableCriticalSettingsOk());
                    LogStableSettingsDetail();
                    Check("pdx_settings.txt has no UTF-8 BOM", !HasUtf8Bom(Path.Combine(ck3Docs, "pdx_settings.txt")));
                    break;
                case 16:
                    WriteRuntimeVerificationReport();
                    Check("Runtime verification report exists", File.Exists(StabilizerFile("ck3_stabilizer_runtime_verification.txt")));
                    CheckRuntimeProfileReadOnly();
                    Check("Settings guard report exists", File.Exists(StabilizerFile("ck3_stabilizer_settings_guard.txt")));
                    break;
                case 17:
                    WriteStableGameRuleProfile();
                    Check("Stable game-rule profile exists", File.Exists(StabilizerFile("ck3_stabilizer_in_game_mp_settings.txt")));
                    break;
                case 18:
                    CheckPlayerStateReadOnly();
                    break;
                case 19:
                    CheckReportsCleanReadOnly();
                    break;
                case 20:
                    Check("CK3 and launcher caches clean or regenerated after cleanup", CacheFoldersClean());
                    break;
                case 21:
                    Check("No local .mod descriptors", CountFiles(Path.Combine(ck3Docs, "mod"), "*.mod") == 0);
                    break;
                case 22:
                    CheckSuspectBinariesReadOnly();
                    break;
                case 23:
                    CheckSaveHygiene();
                    Check("Active continue is not autosave/recovery/desync-like", !ActiveContinueSaveNameSuspicious());
                    Check("Active continue save version matches installed CK3", ActiveSaveVersionOk());
                    break;
                case 24:
                    CheckCk3DocumentsCleanupReadOnly();
                    break;
                case 25:
                    AnalyzeLatestOosReport();
                    CheckLatestOosReportReadOnly();
                    break;
                case 26:
                    WriteOosEvidencePack();
                    CheckOosEvidencePackReadOnly();
                    break;
                case 27:
                    WriteOosPreventionProtocol();
                    Check("OOS prevention protocol exists", File.Exists(StabilizerFile("ck3_stabilizer_oos_protocol.txt")));
                    break;
                case 28:
                    WriteMultiplayerParityManifest();
                    Check("MP parity manifest exists", File.Exists(StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt")));
                    Check("MP parity manifest contains required comparison fields", ParityManifestComplete());
                    Check("OOS risk score report exists", File.Exists(StabilizerFile("ck3_stabilizer_oos_risk_score.txt")));
                    break;
            }

            if (advanceProgress)
                progress.Value = Math.Min(progress.Maximum, progress.Value + 1);
            Application.DoEvents();
        }

        private void CheckBasePaths()
        {
            if (!SettingsFolderValid())
                throw new InvalidOperationException("CK3 settings/saves folder is invalid: " + ck3Docs);

            EnsureStabilizerRoot();
            MoveLegacyStabilizerArtifacts();
            UpdatePathStatusIndicators();
            Log("OK   CK3 settings/saves folder valid: " + ck3Docs);
            if (!GameFolderValid())
                Log("WARN CK3 game folder invalid or not found. Steam/binary checks will be skipped.");
            else
                Log("OK   CK3 game folder valid: " + ck3Install);
        }

        private void CheckBasePathsReadOnly()
        {
            EnsureStabilizerRoot();
            MoveLegacyStabilizerArtifacts();
            Check("CK3 settings/saves folder valid", SettingsFolderValid());
            Check("CK3 install folder valid", GameFolderValid());
            Check("CK3 is not currently running", !IsGameRunning());
            Log("INFO Stabilizer files folder: " + stabilizerRoot);
            Log("INFO Installed CK3 version: " + NullText(DetectInstalledVersion()));
            Log("INFO Active save version: " + NullText(DetectActiveSaveVersion()));
            Log("INFO Steam build: " + NullText(DetectBuildId()));
            Check("Installed/save version parity", VersionParityBaselineOk());
            Check("Steam update complete", SteamUpdateComplete());
        }

        private void CheckQuarantineReadOnly()
        {
            Check("CK3 Documents folder is available for quarantine", Directory.Exists(ck3Docs));
            Log("[INFO] Check only does not create a new quarantine folder.");
            string latest = GetKnownQuarantine();
            Check("Latest quarantine folder exists", !String.IsNullOrEmpty(latest) && Directory.Exists(latest));
            if (!String.IsNullOrEmpty(latest))
                Log("INFO Latest quarantine: " + latest);
        }

        private void CheckSteamAndLauncherBackupSources()
        {
            Check("Steam localconfig.vdf found", !String.IsNullOrEmpty(localConfig) && File.Exists(localConfig));
            Check("Steam sharedconfig.vdf found", !String.IsNullOrEmpty(sharedConfig) && File.Exists(sharedConfig));
            Check("Steam CK3 appmanifest found", !String.IsNullOrEmpty(appManifest) && File.Exists(appManifest));
            Check("CK3 dlc_load.json found", File.Exists(Path.Combine(ck3Docs, "dlc_load.json")));
            Check("CK3 pdx_settings.txt found", File.Exists(Path.Combine(ck3Docs, "pdx_settings.txt")));
            Check("Paradox Launcher userSettings.json found", File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paradox Interactive", "launcher-v2", "userSettings.json")));
        }

        private bool SteamAndLauncherBackupSourcesOk()
        {
            return !String.IsNullOrEmpty(localConfig) && File.Exists(localConfig)
                && !String.IsNullOrEmpty(sharedConfig) && File.Exists(sharedConfig)
                && !String.IsNullOrEmpty(appManifest) && File.Exists(appManifest)
                && File.Exists(Path.Combine(ck3Docs, "dlc_load.json"))
                && File.Exists(Path.Combine(ck3Docs, "pdx_settings.txt"));
        }

        private void CheckSteamSettingsReadOnly()
        {
            Check("Steam launch option -noasync", HasNoAsync());
            Check("No risky renderer/debug launch options", !HasRiskyLaunchOptions());
            Check("Steam Cloud flag disabled when visible in config", SteamCloudDisabledOrUnknown());
            LogSteamOverlayHints();
        }

        private void CheckParadoxLauncherReadOnly()
        {
            string launcherDb = Path.Combine(ck3Docs, "launcher-v2.sqlite");
            if (File.Exists(launcherDb))
                Log("[INFO] launcher-v2.sqlite exists. Stabilize can rebuild it by moving it to quarantine.");
            else
                Log("[OK] launcher-v2.sqlite already absent; launcher will rebuild it.");

            Check("No active mods in launcher profile", NoActiveMods());
            Check("No disabled DLCs in launcher profile", NoDisabledDlcs());
        }

        private void CheckReportsCleanReadOnly()
        {
            Check("OOS folder clean", CountDirectories(Path.Combine(ck3Docs, "oos")) == 0);
            Check("Crashes folder clean", CountItems(Path.Combine(ck3Docs, "crashes")) == 0);
            Check("Dumps folder clean", CountItems(Path.Combine(ck3Docs, "dumps")) == 0);
            Check("Exceptions folder clean", CountItems(Path.Combine(ck3Docs, "exceptions")) == 0);
        }

        private bool ReportsClean()
        {
            return CountDirectories(Path.Combine(ck3Docs, "oos")) == 0
                && CountItems(Path.Combine(ck3Docs, "crashes")) == 0
                && CountItems(Path.Combine(ck3Docs, "dumps")) == 0
                && CountItems(Path.Combine(ck3Docs, "exceptions")) == 0;
        }

        private void CheckFirewallRulesReadOnly()
        {
            string exe = String.IsNullOrEmpty(ck3Bin) ? "" : Path.Combine(ck3Bin, "ck3.exe");
            Check("ck3.exe found for firewall rules", File.Exists(exe));
            Check("CK3 firewall allow rules present", FirewallRulesPresent());
            if (!IsAdministrator())
                Log("[INFO] Program is not elevated. Stabilize can add firewall rules only when run as administrator.");
        }

        private void CreateQuarantine()
        {
            EnsureStabilizerRoot();
            lastQuarantine = Path.Combine(stabilizerRoot, "quarantine");
            currentRestoreRunId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(lastQuarantine);
            Directory.CreateDirectory(Path.Combine(lastQuarantine, "user_state"));
            Directory.CreateDirectory(Path.Combine(lastQuarantine, "reports"));
            Directory.CreateDirectory(Path.Combine(lastQuarantine, "cache"));
            Directory.CreateDirectory(Path.Combine(lastQuarantine, "mods"));
            Directory.CreateDirectory(Path.Combine(lastQuarantine, "binaries"));
            Directory.CreateDirectory(Path.Combine(lastQuarantine, "settings_backup"));
            Directory.CreateDirectory(Path.Combine(lastQuarantine, "steam_settings"));
            Directory.CreateDirectory(Path.Combine(lastQuarantine, "paradox_launcher"));
            InitializeRestoreManifest();
            Log("Quarantine: " + lastQuarantine);
        }

        private string GetKnownQuarantine()
        {
            if (!String.IsNullOrEmpty(lastQuarantine) && Directory.Exists(lastQuarantine))
                return lastQuarantine;
            return FindLatestQuarantine();
        }

        private string FindLatestQuarantine()
        {
            if (!Directory.Exists(stabilizerRoot))
                return "";

            string fixedQuarantine = Path.Combine(stabilizerRoot, "quarantine");
            if (Directory.Exists(fixedQuarantine))
                return fixedQuarantine;

            string[] dirs = Directory.GetDirectories(stabilizerRoot, "_ck3_stabilizer_quarantine_*", SearchOption.TopDirectoryOnly);
            if (dirs.Length == 0)
                return "";

            Array.Sort(dirs, delegate (string a, string b)
            {
                return Directory.GetLastWriteTimeUtc(b).CompareTo(Directory.GetLastWriteTimeUtc(a));
            });
            return dirs[0];
        }

    }
}



