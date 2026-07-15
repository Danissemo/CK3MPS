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
        private void WriteOosPreventionProtocol()
        {
            string path = StabilizerFile("ck3_stabilizer_oos_protocol.txt");
            string content = BuildOosPreventionProtocolText();
            WriteTextFileIfMeaningfullyChanged(
                path,
                content,
                "OK   OOS prevention protocol written: ",
                "INFO OOS prevention protocol already up to date: ",
                true);
        }

        private string BuildOosPreventionProtocolText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 OOS prevention protocol");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Before session");
            sb.AppendLine("- All players verify CK3 files in Steam.");
            sb.AppendLine("- All players use the same CK3 version, same DLC ownership state, no mods, no debug_mode, and -noasync.");
            sb.AppendLine("- Every player generates ck3_stabilizer_mp_parity_manifest.txt and compares version, build, launch options, mods, DLC, and file hashes.");
            sb.AppendLine("- Every player compares Local parity fingerprint; mismatch means stop before unpause.");
            sb.AppendLine("- Every player compares DLC loadout clean, DLC footprint fingerprint, and Steam Workshop fingerprint.");
            sb.AppendLine("- Host uses a local manual save, not cloud save.");
            sb.AppendLine("- Do not use autosave_exit, recovery, patched, backup, or desync-named saves as the serious MP baseline.");
            sb.AppendLine("- Everyone joins in lobby before unpause; avoid hotjoin into a running simulation.");
            sb.AppendLine("- Disable Steam overlay for CK3 if desyncs continue.");
            sb.AppendLine();
            sb.AppendLine("Windows/network");
            sb.AppendLine("- Prefer Ethernet over Wi-Fi for host.");
            sb.AppendLine("- If VPN is used, every player should intentionally use the same route.");
            sb.AppendLine("- Avoid multiple simultaneous gateways during CK3: Ethernet plus Wi-Fi plus VPN can route traffic unpredictably.");
            sb.AppendLine("- Keep firewall enabled, with CK3 allowed inbound and outbound.");
            sb.AppendLine();
            sb.AppendLine("In game");
            sb.AppendLine("- Autosave off, cloud save off, save-on-exit off.");
            sb.AppendLine("- Speed 1-2 for the first in-game month after loading.");
            sb.AppendLine("- Do not change UI presets, game rules, renderer, or DLC/mod state mid-session.");
            sb.AppendLine("- For stability testing, avoid landless-heavy starts, Great Steppe, Japan/East Asia, Dynastic Cycle-heavy, and Iranian Intermezzo starts.");
            sb.AppendLine();
            sb.AppendLine("After OOS");
            sb.AppendLine("- Stop immediately; do not hotjoin-loop.");
            sb.AppendLine("- Collect host and client OOS folders.");
            sb.AppendLine("- Generate ck3_stabilizer_evidence_pack_index.txt on host and client before more retries.");
            sb.AppendLine("- Check oos_metadata_1.txt first for OOS type and machine IDs.");
            sb.AppendLine("- Run the OOS metadata analyzer and compare its action plan against the newest OOS folder.");
            sb.AppendLine("- Roll back to the earliest clean save before relation/modifier divergence.");
            return sb.ToString();
        }

        private void SetRegistryDword(RegistryKey root, string subKey, string name, int value)
        {
            try
            {
                if (RegistryValueSerializedEquals(root, subKey, name, "DWord:" + value))
                {
                    Log("INFO Registry DWORD already set: " + subKey + "\\" + name);
                    return;
                }
                RecordRegistryBeforeChange(root, subKey, name, "Before registry DWORD change: " + subKey + "\\" + name, "DWord:" + value);
                using (RegistryKey key = root.CreateSubKey(subKey))
                {
                    if (key != null)
                        key.SetValue(name, value, RegistryValueKind.DWord);
                }
                Log("OK   Registry DWORD set: " + subKey + "\\" + name);
            }
            catch (Exception ex)
            {
                Log("WARN  Registry DWORD not set: " + subKey + "\\" + name + " | " + ex.Message);
            }
        }

        private void SetRegistryString(RegistryKey root, string subKey, string name, string value)
        {
            try
            {
                if (RegistryValueSerializedEquals(root, subKey, name, "String:" + value))
                {
                    Log("INFO Registry string already set: " + subKey + "\\" + name);
                    return;
                }
                RecordRegistryBeforeChange(root, subKey, name, "Before registry string change: " + subKey + "\\" + name, "String:" + value);
                using (RegistryKey key = root.CreateSubKey(subKey))
                {
                    if (key != null)
                        key.SetValue(name, value, RegistryValueKind.String);
                }
                Log("OK   Registry string set: " + subKey + "\\" + name);
            }
            catch (Exception ex)
            {
                Log("WARN  Registry string not set: " + subKey + "\\" + name + " | " + ex.Message);
            }
        }

        private bool RegistryValueSerializedEquals(RegistryKey root, string subKey, string name, string expectedSerializedValue)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(subKey, false))
                {
                    if (key == null)
                        return false;

                    object value = key.GetValue(name, null);
                    if (value == null)
                        return false;

                    string current = RestoreManifestUtilities.SerializeRegistryValue(value, key.GetValueKind(name));
                    return String.Equals(current, expectedSerializedValue, StringComparison.Ordinal);
                }
            }
            catch
            {
                return false;
            }
        }

        private bool RegistryDwordEquals(RegistryKey root, string subKey, string name, int expected)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(subKey, false))
                {
                    if (key == null)
                        return false;
                    object value = key.GetValue(name);
                    if (value == null)
                        return false;
                    return Convert.ToInt32(value) == expected;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool RegistryStringContains(RegistryKey root, string subKey, string name, string needle)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(subKey, false))
                {
                    if (key == null)
                        return false;
                    object value = key.GetValue(name);
                    return value != null && value.ToString().IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private void WriteStabilityReport()
        {
            WritePortableTransferNotes();
            WriteRuntimeVerificationReport();
            WritePreSessionPlan();
            WriteSessionVerdictReport();
            string report = StabilizerFile("ck3_stabilizer_last_report.txt");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3MPS compact report");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Result: " + (lastReadinessFailures == 0 ? "READY" : "CHECK FINAL SUMMARY"));
            sb.AppendLine("Files folder: " + stabilizerRoot);
            sb.AppendLine("Quarantine: " + NullText(lastQuarantine));
            sb.AppendLine();
            sb.AppendLine("Key files:");
            sb.AppendLine("mp_parity=" + StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt"));
            sb.AppendLine("risk=" + StabilizerFile("ck3_stabilizer_oos_risk_score.txt"));
            sb.AppendLine("protocol=" + StabilizerFile("ck3_stabilizer_oos_protocol.txt"));
            sb.AppendLine("evidence=" + StabilizerFile("ck3_stabilizer_evidence_pack_index.txt"));
            sb.AppendLine();
            sb.AppendLine("Important lines:");
            foreach (string line in SnapshotRunLogLines())
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("WARN", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("RESULT", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine(trimmed);
            }
            File.WriteAllText(report, sb.ToString(), Encoding.UTF8);
            Log("Report written: " + report);
        }

        private void WritePortableTransferNotes()
        {
            try
            {
                string path = StabilizerFile("ck3_stabilizer_portable_notes.txt");
                WriteTextFileIfMeaningfullyChanged(
                    path,
                    BuildPortableTransferNotesText(),
                    "FILE Portable transfer notes written: ",
                    "INFO Portable transfer notes already up to date: ",
                    true);
            }
            catch (Exception ex)
            {
                Log("WARN Portable notes could not be written: " + ex.Message);
            }
        }

        private string BuildPortableTransferNotesText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3MPS portable notes");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("This exe is standalone for normal use on another Windows PC.");
            sb.AppendLine("Run as administrator once if firewall/registry/adapter steps should be applied fully.");
            sb.AppendLine();
            sb.AppendLine("Detected on this PC");
            sb.AppendLine("- Steam root: " + NullText(steamRoot));
            sb.AppendLine("- CK3 install: " + NullText(ck3Install));
            sb.AppendLine("- App manifest: " + NullText(appManifest));
            sb.AppendLine("- Local config: " + NullText(localConfig));
            sb.AppendLine("- Shared config: " + NullText(sharedConfig));
            sb.AppendLine("- Steam libraries: " + String.Join("; ", DetectSteamLibraries().ToArray()));
            sb.AppendLine("- CK3 currently running: " + YesNo(ProcessRunningExact("ck3")));
            sb.AppendLine("- Paradox Launcher currently running: " + YesNo(ProcessRunningContains("dowser") || ProcessRunningContains("paradox launcher")));
            sb.AppendLine();
            sb.AppendLine("Before MP on another PC");
            sb.AppendLine("- Run Maximum once.");
            sb.AppendLine("- Open ck3_stabilizer_mp_parity_manifest.txt and compare Local parity fingerprint with every player.");
            sb.AppendLine("- If CK3 is installed in a custom Steam library, this version scans libraryfolders.vdf automatically.");
            sb.AppendLine("- Do not copy this PC's Documents CK3 folder blindly to another player; compare reports instead.");
            return sb.ToString();
        }

        private void WriteRuntimeVerificationReport()
        {
            try
            {
                string path = StabilizerFile("ck3_stabilizer_runtime_verification.txt");
                WriteTextFileIfMeaningfullyChanged(
                    path,
                    BuildRuntimeVerificationReportText(),
                    "FILE Runtime verification report written: ",
                    "INFO Runtime verification report already up to date: ",
                    true);
            }
            catch (Exception ex)
            {
                Log("WARN Runtime verification report could not be written: " + ex.Message);
            }
        }

        private string BuildRuntimeVerificationReportText()
        {
            string debugLog = Path.Combine(ck3Docs, "logs", "debug.log");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 runtime verification");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Current process state");
            sb.AppendLine("- CK3 running: " + YesNo(ProcessRunningExact("ck3")));
            sb.AppendLine("- Paradox Launcher running: " + YesNo(ProcessRunningContains("dowser") || ProcessRunningContains("paradox launcher")));
            sb.AppendLine();
            sb.AppendLine("Last known runtime signals from debug.log");
            sb.AppendLine("- Reached frontend/main menu: " + YesNo(LogContains(debugLog, "Setting idler 'Frontend'")));
            sb.AppendLine("- Login succeeded: " + YesNo(LogContains(debugLog, "Login succeeded")));
            sb.AppendLine("- Quit from inside game: " + YesNo(LogContains(debugLog, "Quit from inside game")));
            sb.AppendLine("- Startup duration line: " + LastLogLineContaining(debugLog, "Total startup duration"));
            sb.AppendLine("- Last CK3 log time: " + FileWriteTimeText(debugLog));
            sb.AppendLine("- pdx_settings time: " + FileWriteTimeText(Path.Combine(ck3Docs, "pdx_settings.txt")));
            sb.AppendLine("- Launch after current profile: " + YesNo(RuntimeLogExistsAfterSettings()));
            sb.AppendLine("- Runtime profile status: " + RuntimeProfileStatusText());
            sb.AppendLine("- Last renderer signal: " + LastRendererSignal());
            sb.AppendLine("- Last texture telemetry: " + LastLogLineContaining(debugLog, "[telemetry] texture_quality:"));
            sb.AppendLine("- Last shadow telemetry: " + LastLogLineContaining(debugLog, "[telemetry] shadowmap_resolution:"));
            sb.AppendLine();
            sb.AppendLine("Profile drift check");
            sb.AppendLine("- Core stable settings now: " + YesNo(StableCriticalSettingsOk()));
            sb.AppendLine("- Full exact settings now: " + YesNo(StableSettingsOk()));
            sb.AppendLine("- No active mods now: " + YesNo(NoActiveMods()));
            sb.AppendLine("- No disabled DLCs now: " + YesNo(NoDisabledDlcs()));
            sb.AppendLine("- Launch options stable now: " + YesNo(HasNoAsync() && !HasRiskyLaunchOptions()));
            sb.AppendLine("- Save launch hygiene stable now: " + YesNo(SaveLaunchHygieneOk()));
            sb.AppendLine("- Expected profile hashes match: " + YesNo(File.Exists(StabilizerFile("ck3_stabilizer_expected_profile_hashes.txt")) && ExpectedProfileSnapshotMatches()));
            return sb.ToString();
        }

        private void WritePreSessionPlan()
        {
            try
            {
                string path = StabilizerFile("ck3_stabilizer_pre_session_plan.txt");
                WriteTextFileIfMeaningfullyChanged(
                    path,
                    BuildPreSessionPlanText(),
                    "FILE Pre-session plan written: ",
                    "INFO Pre-session plan already up to date: ",
                    true);
            }
            catch (Exception ex)
            {
                Log("WARN Pre-session plan could not be written: " + ex.Message);
            }
        }

        private string BuildPreSessionPlanText()
        {
            string bestClean = FindBestCleanManualSave();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 MP pre-session plan");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Start decision");
            if (ActiveContinueSaveNameSuspicious())
                sb.AppendLine("- DO NOT start serious MP from current Continue: " + NullText(DetectActiveSaveTitle()));
            else if (String.IsNullOrEmpty(DetectActiveSaveTitle()))
                sb.AppendLine("- Continue pointer is absent. Use Load Game and choose the recommended clean manual save.");
            else
                sb.AppendLine("- Current Continue name is not suspicious: " + NullText(DetectActiveSaveTitle()));
            sb.AppendLine("- Best clean manual local save candidate: " + NullText(Path.GetFileName(bestClean)));
            sb.AppendLine("- Candidate path: " + NullText(bestClean));
            sb.AppendLine("- Candidate hash: " + FileHashOrMissing(bestClean));
            sb.AppendLine("- Candidate readable: " + YesNo(BestCleanSaveReadable()));
            sb.AppendLine("- Candidate version: " + NullText(ExtractSaveMetaValue(bestClean, "version")));
            sb.AppendLine("- Candidate date: " + NullText(ExtractSaveMetaValue(bestClean, "meta_date")));
            sb.AppendLine("- Candidate player: " + NullText(ExtractSaveMetaValue(bestClean, "meta_player_name")));
            sb.AppendLine("- Candidate title: " + NullText(ExtractSaveMetaValue(bestClean, "meta_title_name")));
            sb.AppendLine();
            sb.AppendLine("Before unpause");
            sb.AppendLine("- Every player sends ck3_stabilizer_mp_parity_manifest.txt.");
            sb.AppendLine("- Compare Local parity fingerprint: " + BuildLocalParityFingerprint());
            sb.AppendLine("- Confirm all players have -noasync, no debug/developer launch options, no active mods, no disabled DLC mismatch, same Steam build and public branch.");
            sb.AppendLine("- Host loads a local manual save, creates a fresh lobby, waits for everyone, then unpauses.");
            sb.AppendLine("- Stay speed 1-2 for the first in-game month.");
            sb.AppendLine();
            sb.AppendLine("Stop conditions");
            sb.AppendLine("- Fingerprint mismatch.");
            sb.AppendLine("- Any player has active mods, disabled DLC mismatch, or debug/developer launch options.");
            sb.AppendLine("- Host is about to load autosave/recovery/backup/desync-like save.");
            sb.AppendLine("- CK3 or launcher rewrites profile and expected hashes no longer match.");
            return sb.ToString();
        }

        private void WriteSessionVerdictReport()
        {
            try
            {
                string path = StabilizerFile("ck3_stabilizer_session_verdict.txt");
                WriteTextFileIfMeaningfullyChanged(
                    path,
                    BuildSessionVerdictReportText(),
                    "FILE Session verdict report written: ",
                    "INFO Session verdict report already up to date: ",
                    true);
            }
            catch (Exception ex)
            {
                Log("WARN Session verdict report could not be written: " + ex.Message);
            }
        }

        private string BuildSessionVerdictReportText()
        {
            List<string> blockers = BuildSessionBlockers();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 MP session verdict");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Verdict: " + (blockers.Count == 0 ? "GO" : "NO-GO"));
            sb.AppendLine();
            if (blockers.Count == 0)
            {
                sb.AppendLine("No local blockers detected. Start from a fresh lobby and compare parity manifests before unpause.");
            }
            else
            {
                sb.AppendLine("Blockers");
                foreach (string blocker in blockers)
                    sb.AppendLine("- " + blocker);
            }
            sb.AppendLine();
            sb.AppendLine("Recommended clean save: " + NullText(Path.GetFileName(FindBestCleanManualSave())));
            sb.AppendLine("Local parity fingerprint: " + BuildLocalParityFingerprint());
            return sb.ToString();
        }

        private List<string> BuildSessionBlockers()
        {
            List<string> blockers = new List<string>();
            if (ActiveContinueSaveNameSuspicious())
                blockers.Add("Current Continue is autosave/recovery/backup/desync-like: " + NullText(DetectActiveSaveTitle()));
            if (!StableCriticalSettingsOk())
                blockers.Add("Core stable pdx_settings profile is not currently applied.");
            if (!NoActiveMods())
                blockers.Add("dlc_load.json is not confirmed clean.");
            if (!NoDisabledDlcs())
                blockers.Add("One or more DLCs are disabled in dlc_load.json; every MP player must match DLC state.");
            if (!HasNoAsync() || HasRiskyLaunchOptions())
                blockers.Add("Steam launch options are not stable for MP.");
            if (!ExpectedProfileOkForReadiness())
                blockers.Add("Current profile no longer matches expected stable profile hashes.");
            if (RuntimeProfileLooksBadAfterSettings())
                blockers.Add("Last CK3 launch after stabilization did not confirm the Vulkan profile. Restart CK3 after applying or check launch options.");
            if (!ActiveSaveVersionOk())
                blockers.Add("Active continue save version differs from installed CK3 version.");
            if (!BestCleanSaveReadable())
                blockers.Add("Recommended clean manual save cannot be read safely.");
            if (!BestCleanSaveVersionOk())
                blockers.Add("Recommended clean manual save version differs from installed CK3 version.");
            return blockers;
        }

        private string FindBestCleanManualSave()
        {
            string saveDir = Path.Combine(ck3Docs, "save games");
            if (!Directory.Exists(saveDir))
                return "";

            FileInfo[] saves = new DirectoryInfo(saveDir).GetFiles("*.ck3");
            Array.Sort(saves, delegate (FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
            foreach (FileInfo save in saves)
            {
                if (!IsSuspiciousSaveName(save.Name))
                    return save.FullName;
            }
            return saves.Length > 0 ? saves[0].FullName : "";
        }

        private bool BestCleanSaveReadable()
        {
            string save = FindBestCleanManualSave();
            return SaveHeaderLooksReadable(save);
        }

        private bool BestCleanSaveVersionOk()
        {
            string save = FindBestCleanManualSave();
            string saveVersion = ExtractSaveMetaValue(save, "version");
            string installed = DetectInstalledVersion();
            if (String.IsNullOrEmpty(saveVersion) || String.IsNullOrEmpty(installed))
                return false;
            return String.Equals(saveVersion, installed, StringComparison.OrdinalIgnoreCase);
        }

        private bool SaveHeaderLooksReadable(string path)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path))
                return false;
            string header = ReadFilePrefixText(path, 131072);
            return header.StartsWith("SAV", StringComparison.OrdinalIgnoreCase)
                && header.IndexOf("meta_data", StringComparison.OrdinalIgnoreCase) >= 0
                && header.IndexOf("save_game_version", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string ExtractSaveMetaValue(string path, string key)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path))
                return "";
            string header = ReadFilePrefixText(path, 262144);
            Match quoted = Regex.Match(header, "(?im)^\\s*" + Regex.Escape(key) + "\\s*=\\s*\"([^\"]*)\"");
            if (quoted.Success)
                return quoted.Groups[1].Value.Trim();
            Match raw = Regex.Match(header, "(?im)^\\s*" + Regex.Escape(key) + "\\s*=\\s*([^\\s\\r\\n{}]+)");
            return raw.Success ? raw.Groups[1].Value.Trim() : "";
        }

        private string ReadFilePrefixText(string path, int maxBytes)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    int length = (int)Math.Min(maxBytes, stream.Length);
                    byte[] buffer = new byte[length];
                    int read = stream.Read(buffer, 0, length);
                    return Encoding.UTF8.GetString(buffer, 0, read);
                }
            }
            catch
            {
                return "";
            }
        }

        private bool LogContains(string path, string needle)
        {
            return File.Exists(path) && ReadTextShared(path).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string LastLogLineContaining(string path, string needle)
        {
            if (!File.Exists(path))
                return "(missing)";
            string result = "(not found)";
            foreach (string line in ReadTextShared(path).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                if (line.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    result = line.Trim();
            return result;
        }

        private string ReadTextShared(string path)
        {
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
            catch
            {
                return "";
            }
        }

        private void WriteCheckOnlyReport()
        {
            try
            {
                string report = StabilizerFile("ck3_stabilizer_check_only_report.txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("CK3MPS compact check");
                sb.AppendLine("Stabilizer: " + AppVersion);
                sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Result: " + (lastReadinessFailures == 0 ? "READY" : "NOT READY"));
                sb.AppendLine("Failed readiness checks: " + lastReadinessFailures);
                sb.AppendLine();
                foreach (string line in SnapshotRunLogLines())
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
                        || trimmed.StartsWith("WARN", StringComparison.OrdinalIgnoreCase)
                        || trimmed.StartsWith("RESULT", StringComparison.OrdinalIgnoreCase))
                        sb.AppendLine(trimmed);
                }
                File.WriteAllText(report, sb.ToString(), Encoding.UTF8);
                Log("FILE Check-only report written: " + report);
            }
            catch (Exception ex)
            {
                Log("WARN Check-only report could not be written: " + ex.Message);
            }
        }

        private void RunReadinessChecks(bool includeRestorePointCheck)
        {
            int failed = 0;
            lastReadinessFailures = 0;
            Log("Readiness check: ordered by the checklist.");

            if (includeRestorePointCheck)
                failed += CheckStepResult(0, WindowsRestorePointInfrastructureOk());
            else
                Log("INFO Readiness skipped Windows restore point infrastructure in Check Only mode.");
            failed += CheckStepResult(1, Directory.Exists(ck3Docs) && !IsGameRunning() && VersionParityBaselineOk() && SteamUpdateComplete());
            failed += CheckStepResult(2, !String.IsNullOrEmpty(GetKnownQuarantine()) && Directory.Exists(GetKnownQuarantine()));
            failed += CheckStepResult(3, NetworkBaselineOk());
            failed += CheckStepResult(4, HasAnyActiveNetworkRoute() && NetworkBaselineOk());
            failed += CheckStepResult(5, FirewallRulesPresent());
            failed += CheckStepResult(6, WindowsGameNetworkProfileOk());
            failed += CheckStepResult(7, PowerAdapterProfileOk());
            failed += CheckStepResult(8, WindowsAppsAndServicesOk());
            failed += CheckStepResult(9, OnlineServicesOk());
            failed += CheckStepResult(10, SteamAndLauncherBackupSourcesOk());
            failed += CheckStepResult(11, HasNoAsync() && !HasRiskyLaunchOptions() && SteamCloudDisabledOrUnknownQuiet());
            failed += CheckStepResult(12, !File.Exists(Path.Combine(ck3Docs, "launcher-v2.sqlite")) || DlcLoadProfileClean());
            failed += CheckStepResult(13, !ProcessRunningContains("dowser") && !ProcessRunningContains("paradox launcher") && !ProcessRunningExact("ck3"));
            failed += CheckStepResult(14, DlcLoadProfileClean() && !HasUtf8Bom(Path.Combine(ck3Docs, "dlc_load.json")));
            failed += CheckStepResult(15, StableCriticalSettingsOk() && !HasUtf8Bom(Path.Combine(ck3Docs, "pdx_settings.txt")));
            failed += CheckStepResult(16, File.Exists(StabilizerFile("ck3_stabilizer_runtime_verification.txt")) && File.Exists(StabilizerFile("ck3_stabilizer_settings_guard.txt")) && !RuntimeProfileLooksBadAfterSettings());
            failed += CheckStepResult(17, File.Exists(StabilizerFile("ck3_stabilizer_in_game_mp_settings.txt")));
            failed += CheckStepResult(18, PlayerStateNonCritical());
            failed += CheckStepResult(19, ReportsClean());
            failed += CheckStepResult(20, CacheFoldersClean());
            failed += CheckStepResult(21, CountFiles(Path.Combine(ck3Docs, "mod"), "*.mod") == 0);
            failed += CheckStepResult(22, !String.IsNullOrEmpty(ck3Bin) && Directory.Exists(ck3Bin) && CountSuspectBinaries() == 0);
            failed += CheckStepResult(23, ActiveSaveVersionOk() && SaveLaunchHygieneOk() && BestCleanSaveReadable() && BestCleanSaveVersionOk());
            failed += CheckStepResult(24, Ck3DocumentsCleanupOk());
            failed += CheckStepResult(25, File.Exists(StabilizerFile("ck3_stabilizer_latest_oos_summary.txt")) || String.IsNullOrEmpty(FindLatestOosMetadataFile()));
            failed += CheckStepResult(26, File.Exists(StabilizerFile("ck3_stabilizer_evidence_pack_index.txt")));
            failed += CheckStepResult(27, File.Exists(StabilizerFile("ck3_stabilizer_oos_protocol.txt")));
            failed += CheckStepResult(28, ParityManifestComplete() && File.Exists(StabilizerFile("ck3_stabilizer_oos_risk_score.txt")));

            if (failed == 0)
                Log("OK   Final readiness summary | all checklist checks passed");
            else
                Log("INFO Final readiness summary | checklist failed checks: " + failed);

            SetProgressValueSafe(Int32.MaxValue);
            SetStatusText(failed == 0 ? "READY for stable CK3 MP profile." : "Not ready. Failed checks before final summary: " + failed);
            lastReadinessFailures = failed;
            Log(failed == 0 ? "RESULT READY." : "RESULT NOT READY. Failed checks before final summary: " + failed);
        }

        private int CheckStepResult(int index, bool ok)
        {
            string name = StepTitle(index);
            return Check(name, ok);
        }

        private int Check(string name, bool ok)
        {
            Log((ok ? "OK   " : "FAIL ") + name);
            return ok ? 0 : 1;
        }

        private bool IsGameRunning()
        {
            foreach (Process p in Process.GetProcesses())
            {
                string n = "";
                try { n = p.ProcessName.ToLowerInvariant(); } catch { }
                if (n == "ck3" || n.Contains("dowser") || n.Contains("paradox launcher"))
                    return true;
            }
            return false;
        }

        private bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private string DetectSteamRoot()
        {
            string[] roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
            };
            foreach (string root in roots)
                if (Directory.Exists(root))
                    return root;
            foreach (string library in DetectSteamLibraries())
            {
                string parent = Directory.GetParent(library) != null ? Directory.GetParent(library).FullName : "";
                if (Directory.Exists(Path.Combine(library, "steamapps")))
                    return library;
                if (Directory.Exists(Path.Combine(parent, "steamapps")))
                    return parent;
            }
            return "";
        }

        private string DetectManifest()
        {
            foreach (string library in DetectSteamLibraries())
            {
                string candidate = Path.Combine(library, "steamapps", "appmanifest_1158310.acf");
                if (File.Exists(candidate))
                    return candidate;
            }
            return "";
        }

        private string DetectInstallPath()
        {
            if (!String.IsNullOrEmpty(appManifest) && File.Exists(appManifest))
            {
                string steamApps = Path.GetDirectoryName(appManifest);
                string text = File.ReadAllText(appManifest);
                Match m = Regex.Match(text, "\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string path = Path.Combine(steamApps, "common", m.Groups[1].Value);
                    if (Directory.Exists(path))
                        return path;
                }
            }
            foreach (string library in DetectSteamLibraries())
            {
                string fallback = Path.Combine(library, "steamapps", "common", "Crusader Kings III");
                if (Directory.Exists(fallback))
                    return fallback;
            }
            return "";
        }

        private string DetectLocalConfig()
        {
            string userData = Path.Combine(DetectSteamRoot(), "userdata");
            if (!Directory.Exists(userData))
                return "";
            foreach (string file in Directory.GetFiles(userData, "localconfig.vdf", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                if (text.Contains("\"1158310\""))
                    return file;
            }
            return "";
        }

        private string DetectSharedConfig()
        {
            string userData = Path.Combine(DetectSteamRoot(), "userdata");
            if (!Directory.Exists(userData))
                return "";
            foreach (string file in Directory.GetFiles(userData, "sharedconfig.vdf", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                if (text.Contains("\"1158310\""))
                    return file;
            }
            return "";
        }

        private List<string> DetectSteamLibraries()
        {
            List<string> libraries = new List<string>();
            AddUniqueExistingDirectory(libraries, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            AddUniqueExistingDirectory(libraries, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
            AddUniqueExistingDirectory(libraries, @"E:\SteamLibrary");

            List<string> roots = new List<string>(libraries);
            foreach (string root in roots)
            {
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf))
                    continue;

                try
                {
                    string text = File.ReadAllText(vdf);
                    foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
                        AddUniqueExistingDirectory(libraries, m.Groups[1].Value.Replace(@"\\", @"\"));
                }
                catch { }
            }

            return libraries;
        }

        private void AddUniqueExistingDirectory(List<string> list, string path)
        {
            if (String.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;

            string full = Path.GetFullPath(path).TrimEnd('\\');
            foreach (string existing in list)
                if (String.Equals(Path.GetFullPath(existing).TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase))
                    return;
            list.Add(full);
        }

        private string DetectInstalledVersion()
        {
            if (String.IsNullOrEmpty(ck3Install))
                return "";

            string path = Path.Combine(ck3Install, "launcher", "launcher-settings.json");
            if (!File.Exists(path))
                return "";

            string text = File.ReadAllText(path);
            Match m = Regex.Match(text, "\"rawVersion\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "";
        }

        private string DetectActiveSaveVersion()
        {
            string path = Path.Combine(ck3Docs, "continue_game.json");
            if (!File.Exists(path))
                return "";

            string text = File.ReadAllText(path);
            Match m = Regex.Match(text, "\"rawGameVersion\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "";
        }

        private string DetectActiveSaveTitle()
        {
            string path = Path.Combine(ck3Docs, "continue_game.json");
            if (!File.Exists(path))
                return "";

            string text = File.ReadAllText(path);
            Match m = Regex.Match(text, "\"title\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "";
        }

        private string DetectBuildId()
        {
            if (String.IsNullOrEmpty(appManifest) || !File.Exists(appManifest))
                return "";

            string text = File.ReadAllText(appManifest);
            Match m = Regex.Match(text, "\"buildid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "";
        }

        private string DetectSteamBranch()
        {
            if (String.IsNullOrEmpty(appManifest) || !File.Exists(appManifest))
                return "";

            string text = File.ReadAllText(appManifest);
            Match m = Regex.Match(text, "\"betakey\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            if (!m.Success || String.IsNullOrWhiteSpace(m.Groups[1].Value))
                return "public";
            return m.Groups[1].Value.Trim();
        }

        private int CountDlcFootprintFiles()
        {
            if (String.IsNullOrEmpty(ck3Install) || !Directory.Exists(ck3Install))
                return 0;

            int total = 0;
            string[] dirs = new[]
            {
                Path.Combine(ck3Install, "dlc"),
                Path.Combine(ck3Install, "game", "dlc")
            };

            foreach (string dir in dirs)
                if (Directory.Exists(dir))
                    total += Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
            return total;
        }

        private int CountSteamWorkshopItems()
        {
            foreach (string library in DetectSteamLibraries())
            {
                string dir = Path.Combine(library, "steamapps", "workshop", "content", "1158310");
                if (Directory.Exists(dir))
                    return Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly).Length;
            }
            return 0;
        }

        private string BuildDlcFootprintFingerprint()
        {
            List<string> items = new List<string>();
            if (!String.IsNullOrEmpty(ck3Install) && Directory.Exists(ck3Install))
            {
                foreach (string dir in new[] { Path.Combine(ck3Install, "dlc"), Path.Combine(ck3Install, "game", "dlc") })
                {
                    if (!Directory.Exists(dir))
                        continue;
                    foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        items.Add(RelativePath(dir, file) + ":" + new FileInfo(file).Length);
                }
            }
            return HashStringList(items);
        }

        private string BuildSteamWorkshopFingerprint()
        {
            List<string> items = new List<string>();
            foreach (string library in DetectSteamLibraries())
            {
                string dir = Path.Combine(library, "steamapps", "workshop", "content", "1158310");
                if (!Directory.Exists(dir))
                    continue;
                foreach (string sub in Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                    items.Add(Path.GetFileName(sub));
            }
            return HashStringList(items);
        }

        private string HashStringList(List<string> items)
        {
            items.Sort(StringComparer.OrdinalIgnoreCase);
            string seed = String.Join("\n", items.ToArray());
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private string RelativePath(string root, string path)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(fullRoot.Length);
            return Path.GetFileName(path);
        }

        private string BuildLocalParityFingerprint()
        {
            string seed = String.Join("|", new[]
            {
                DetectInstalledVersion(),
                DetectBuildId(),
                DetectSteamBranch(),
                FileHashOrMissing(Path.Combine(ck3Bin, "ck3.exe")),
                FileHashOrMissing(Path.Combine(ck3Install, "launcher", "launcher-settings.json")),
                FileHashOrMissing(Path.Combine(ck3Docs, "dlc_load.json")),
                FileHashOrMissing(Path.Combine(ck3Docs, "pdx_settings.txt")),
                CountDlcFootprintFiles().ToString(),
                BuildDlcFootprintFingerprint(),
                CountSteamWorkshopItems().ToString(),
                BuildSteamWorkshopFingerprint()
            });

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private bool VersionParityBaselineOk()
        {
            string installed = DetectInstalledVersion();
            string activeSave = DetectActiveSaveVersion();

            if (String.IsNullOrEmpty(installed) && String.IsNullOrEmpty(activeSave))
                return false;
            if (String.IsNullOrEmpty(installed) || String.IsNullOrEmpty(activeSave))
                return true;

            return String.Equals(installed, activeSave, StringComparison.OrdinalIgnoreCase);
        }

        private bool HasNoAsync()
        {
            if (String.IsNullOrEmpty(localConfig) || !File.Exists(localConfig))
                return false;
            string text = File.ReadAllText(localConfig);
            int appsIndex = text.IndexOf("\"apps\"", StringComparison.OrdinalIgnoreCase);
            int appIndex = appsIndex >= 0 ? text.IndexOf("\"1158310\"", appsIndex, StringComparison.OrdinalIgnoreCase) : -1;
            if (appIndex < 0)
                return false;
            int open = text.IndexOf('{', appIndex);
            int close = FindMatchingBrace(text, open);
            if (open < 0 || close < 0)
                return false;
            string block = text.Substring(open, close - open + 1);
            return Regex.IsMatch(block, "\"LaunchOptions\"\\s+\"[^\"]*-noasync[^\"]*\"", RegexOptions.IgnoreCase);
        }

        private bool HasDebugModeLaunchOption()
        {
            if (String.IsNullOrEmpty(localConfig) || !File.Exists(localConfig))
                return false;
            string text = File.ReadAllText(localConfig);
            int appsIndex = text.IndexOf("\"apps\"", StringComparison.OrdinalIgnoreCase);
            int appIndex = appsIndex >= 0 ? text.IndexOf("\"1158310\"", appsIndex, StringComparison.OrdinalIgnoreCase) : -1;
            if (appIndex < 0)
                return false;
            int open = text.IndexOf('{', appIndex);
            int close = FindMatchingBrace(text, open);
            if (open < 0 || close < 0)
                return false;
            string block = text.Substring(open, close - open + 1);
            return Regex.IsMatch(block, "\"LaunchOptions\"\\s+\"[^\"]*debug_mode[^\"]*\"", RegexOptions.IgnoreCase);
        }

        private bool SteamUpdateComplete()
        {
            if (String.IsNullOrEmpty(appManifest) || !File.Exists(appManifest))
                return false;
            string text = File.ReadAllText(appManifest);
            long bytesToDownload = ReadManifestLong(text, "BytesToDownload");
            long bytesDownloaded = ReadManifestLong(text, "BytesDownloaded");
            long bytesToStage = ReadManifestLong(text, "BytesToStage");
            long bytesStaged = ReadManifestLong(text, "BytesStaged");
            return Regex.IsMatch(text, "\"StateFlags\"\\s+\"4\"")
                && Regex.IsMatch(text, "\"TargetBuildID\"\\s+\"0\"")
                && bytesDownloaded >= bytesToDownload
                && bytesStaged >= bytesToStage;
        }

        private long ReadManifestLong(string text, string key)
        {
            Match m = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s+\"(\\d+)\"");
            if (!m.Success)
                return 0;
            long value;
            return Int64.TryParse(m.Groups[1].Value, out value) ? value : 0;
        }

        private bool NoActiveMods()
        {
            string path = Path.Combine(ck3Docs, "dlc_load.json");
            return File.Exists(path) && Regex.IsMatch(File.ReadAllText(path), "\"enabled_mods\"\\s*:\\s*\\[\\s*\\]", RegexOptions.IgnoreCase);
        }

        private bool NoDisabledDlcs()
        {
            string path = Path.Combine(ck3Docs, "dlc_load.json");
            if (!File.Exists(path))
                return false;

            string text = File.ReadAllText(path);
            return !Regex.IsMatch(text, "\"disabled_dlcs\"\\s*:", RegexOptions.IgnoreCase)
                || Regex.IsMatch(text, "\"disabled_dlcs\"\\s*:\\s*\\[\\s*\\]", RegexOptions.IgnoreCase);
        }

        private bool DlcLoadProfileClean()
        {
            return NoActiveMods() && NoDisabledDlcs();
        }

        private bool IsReadOnly(string path)
        {
            if (!File.Exists(path))
                return false;

            return (File.GetAttributes(path) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
        }

        private bool HasUtf8Bom(string path)
        {
            if (!File.Exists(path))
                return false;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            }
            catch
            {
                return false;
            }
        }

        private void ClearReadOnly(string path)
        {
            SetReadOnly(path, false);
        }

        private void SetReadOnly(string path, bool readOnly)
        {
            if (!File.Exists(path))
                return;

            try
            {
                FileAttributes attrs = File.GetAttributes(path);
                if (readOnly)
                    attrs |= FileAttributes.ReadOnly;
                else
                    attrs &= ~FileAttributes.ReadOnly;
                File.SetAttributes(path, attrs);
            }
            catch (Exception ex)
            {
                Log("WARN Could not " + (readOnly ? "lock" : "unlock") + " file: " + path + " | " + ex.Message);
            }
        }

        private bool ParityManifestComplete()
        {
            string path = StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt");
            if (!File.Exists(path))
                return false;

            string text = File.ReadAllText(path, Encoding.UTF8);
            return text.IndexOf("Local parity fingerprint:", StringComparison.OrdinalIgnoreCase) >= 0
                && text.IndexOf("DLC footprint fingerprint:", StringComparison.OrdinalIgnoreCase) >= 0
                && text.IndexOf("Steam Workshop fingerprint:", StringComparison.OrdinalIgnoreCase) >= 0
                && text.IndexOf("DLC loadout clean:", StringComparison.OrdinalIgnoreCase) >= 0
                && text.IndexOf("Best clean manual save candidate:", StringComparison.OrdinalIgnoreCase) >= 0
                && text.IndexOf("Active continue title:", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool StableCriticalSettingsOk()
        {
            // Only deterministic MP readiness settings are hard failures here.
            // User-selected graphics quality is reported separately as WARN/INFO.
            string path = Path.Combine(ck3Docs, "pdx_settings.txt");
            if (!File.Exists(path))
                return false;
            string text = File.ReadAllText(path);
            return SectionSettingMatches(text, "game", "autosave", "version=0\\s*value=\"NO_AUTOSAVE\"")
                && SectionSettingMatches(text, "game", "cloud_save", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "game", "save_on_exit", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "game", "file_transfer_speed", "version=0\\s*value=\"OPTION_HIGH\"")
                && SectionSettingMatches(text, "Graphics", "renderer", "version=0\\s*value=\"Vulkan\"")
                && SectionSettingMatches(text, "Graphics", "display_mode", "version=0\\s*value=\"fullscreen\"")
                && SectionSettingMatches(text, "Graphics", "vsync", "version=0\\s*enabled=yes")
                && SectionSettingMatches(text, "Graphics", "adaptive_framerate", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "setting_framerate_cap", "version=0\\s*value=\"60\"")
                && SectionSettingMatches(text, "System", "language", "version=0\\s*value=\"l_english\"");
        }

        private void LogStableSettingsDetail()
        {
            string path = Path.Combine(ck3Docs, "pdx_settings.txt");
            if (!File.Exists(path))
            {
                Log("FAIL  pdx_settings.txt is missing.");
                return;
            }

            string text = File.ReadAllText(path);
            Log("INFO  Core profile: " + (StableCriticalSettingsOk() ? "OK" : "not fully applied"));
            LogCoreSetting(text, "game", "autosave", "version=0\\s*value=\"NO_AUTOSAVE\"");
            LogCoreSetting(text, "game", "cloud_save", "version=0\\s*enabled=no");
            LogCoreSetting(text, "game", "save_on_exit", "version=0\\s*enabled=no");
            LogCoreSetting(text, "game", "file_transfer_speed", "version=0\\s*value=\"OPTION_HIGH\"");
            LogCoreSetting(text, "Graphics", "renderer", "version=0\\s*value=\"Vulkan\"");
            LogCoreSetting(text, "Graphics", "setting_framerate_cap", "version=0\\s*value=\"60\"");
            LogGraphicsProfileDetail(text);

            if (!StableSettingsOk())
                Log("INFO  Full exact profile differs from Stability Low. This is acceptable when critical MP settings are applied.");
        }

        private void LogGraphicsProfileDetail(string text)
        {
            string profile = CurrentGraphicsProfile();
            bool ok = GraphicsProfileOk(text, profile);
            Log("INFO  Graphics profile selected: " + profile + " | " + (ok ? "matching" : "differs"));
            if (!ok)
                Log("WARN  Graphics profile differs from selection. This does not block OOS readiness unless critical MP settings fail.");
        }

        private bool GraphicsProfileOk(string text, string profile)
        {
            if (String.Equals(profile, "Keep current", StringComparison.OrdinalIgnoreCase))
                return true;
            if (String.Equals(profile, "Quality", StringComparison.OrdinalIgnoreCase))
                return SectionSettingMatches(text, "Graphics", "texture_quality", "version=1\\s*value=\"ultra\"")
                    && SectionSettingMatches(text, "Graphics", "shadowmap_resolution", "version=2\\s*value=\"4096x4096\"")
                    && SectionSettingMatches(text, "Graphics", "refraction_quality", "version=1\\s*value=\"high\"");
            if (String.Equals(profile, "Balanced", StringComparison.OrdinalIgnoreCase))
                return SectionSettingMatches(text, "Graphics", "texture_quality", "version=1\\s*value=\"medium\"")
                    && SectionSettingMatches(text, "Graphics", "shadowmap_resolution", "version=2\\s*value=\"2048x2048\"")
                    && SectionSettingMatches(text, "Graphics", "refraction_quality", "version=1\\s*value=\"medium\"")
                    && SectionSettingMatches(text, "Graphics", "advanced_shaders", "version=0\\s*enabled=yes");
            return SectionSettingMatches(text, "Graphics", "texture_quality", "version=1\\s*value=\"low\"")
                && SectionSettingMatches(text, "Graphics", "shadowmap_resolution", "version=2\\s*value=\"disabled\"")
                && SectionSettingMatches(text, "Graphics", "refraction_quality", "version=1\\s*value=\"disabled\"");
        }

        private void LogCoreSetting(string text, string section, string key, string pattern)
        {
            if (!SectionSettingMatches(text, section, key, pattern))
                Log("FAIL  Missing core setting: " + section + "." + key);
        }

        private bool StableSettingsOk()
        {
            string path = Path.Combine(ck3Docs, "pdx_settings.txt");
            if (!File.Exists(path))
                return false;
            string text = File.ReadAllText(path);
            return StableCriticalSettingsOk()
                && SectionSettingMatches(text, "game", "rich_presence", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "anisotropic_filtering", "version=0\\s*value=\"x4\"")
                && SectionSettingMatches(text, "Graphics", "portrait_multi_sampling", "version=0\\s*value=\"x2\"")
                && SectionSettingMatches(text, "Graphics", "terrain_smoothing", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "ssao", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "depthoffield", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "lensflare", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "secondary_lensflare", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "bloom_enabled", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "mesh_lod_bias", "version=1\\s*value=\"low\"")
                && SectionSettingMatches(text, "Graphics", "mesh_lod_fade", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "mapobject_quality", "version=0\\s*value=\"off\"")
                && SectionSettingMatches(text, "Graphics", "animated_portraits", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "court_scene_low_priority_characters", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "royal_court_anim_camera_idle", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "royal_court_anim_camera_transition", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "portraits_ssao", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "portraits_bloom", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "advanced_shaders", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "winter_particle_effects", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "cloud_shadow_enabled", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "tree_dithering_enabled", "version=0\\s*enabled=no")
                && SectionSettingMatches(text, "Graphics", "anti_aliasing", "version=0\\s*value=\"DISABLED\"")
                && SectionSettingMatches(text, "Audio", "audio_debug_log_level", "version=0\\s*value=\"error\"");
        }

        private bool ActiveSaveVersionOk()
        {
            if (!File.Exists(Path.Combine(ck3Docs, "continue_game.json")))
                return true;
            return VersionParityBaselineOk();
        }

        private bool NetworkBaselineOk()
        {
            return PingOk("1.1.1.1") || PingOk("8.8.8.8");
        }

        private bool OnlineServicesOk()
        {
            return TcpOk("api.paradox-interactive.com", 443) || TcpOk("store.steampowered.com", 443);
        }

        private bool TcpOk(string host, int port)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult result = client.BeginConnect(host, port, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(1800);
                    if (!success)
                        return false;
                    client.EndConnect(result);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private void CheckTcpAndLog(string label, string host, int port)
        {
            Stopwatch sw = Stopwatch.StartNew();
            bool ok = TcpOk(host, port);
            sw.Stop();
            if (ok)
                Log("TCP " + label + " " + host + ":" + port + " OK in " + sw.ElapsedMilliseconds + "ms");
            else
                Log("TCP " + label + " " + host + ":" + port + " FAILED");
        }

        private bool PingOk(string host)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(host, 1200);
                    return reply != null && reply.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        private void PingAndLog(string label, string host)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    long total = 0;
                    int ok = 0;
                    int fail = 0;
                    long max = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        PingReply reply = ping.Send(host, 1200);
                        if (reply != null && reply.Status == IPStatus.Success)
                        {
                            ok++;
                            total += reply.RoundtripTime;
                            if (reply.RoundtripTime > max)
                                max = reply.RoundtripTime;
                        }
                        else
                        {
                            fail++;
                        }
                    }

                    if (ok > 0)
                    {
                        long avg = total / ok;
                        Log("Ping " + label + ": ok=" + ok + " fail=" + fail + " avg=" + avg + "ms max=" + max + "ms");
                        if (fail > 0 || max > 120)
                            Log("Warning: unstable/high ping to " + label + ".");
                    }
                    else
                    {
                        Log("Ping " + label + ": failed.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Ping " + label + " failed: " + ex.Message);
            }
        }

        private string RunCommand(string file, string args, bool ignoreFailure)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = file;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                using (Process p = Process.Start(psi))
                {
                    StringBuilder output = new StringBuilder();
                    StringBuilder error = new StringBuilder();
                    p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                            output.AppendLine(e.Data);
                    };
                    p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                            error.AppendLine(e.Data);
                    };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!WaitForProcessResponsive(p, 10000))
                    {
                        try { p.Kill(); } catch { }
                        if (!ignoreFailure)
                            Log("WARN " + file + " timed out.");
                        return "";
                    }
                    p.WaitForExit();
                    string combined = (output.ToString() + "\r\n" + error.ToString()).Trim();
                    if (!String.IsNullOrEmpty(combined))
                    {
                        Log("CMD  " + file + " " + args);
                        foreach (string line in combined.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                            Log("  " + line.Trim());
                    }

                    if (p.ExitCode != 0 && !ignoreFailure)
                        Log("WARN " + file + " exited with code " + p.ExitCode);
                    return combined;
                }
            }
            catch (Exception ex)
            {
                if (!ignoreFailure)
                    Log("WARN " + file + " failed: " + ex.Message);
                return "";
            }
        }

        private string RunCommandQuiet(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = file;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                using (Process p = Process.Start(psi))
                {
                    StringBuilder output = new StringBuilder();
                    StringBuilder error = new StringBuilder();
                    p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                            output.AppendLine(e.Data);
                    };
                    p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                            error.AppendLine(e.Data);
                    };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!WaitForProcessResponsive(p, 10000))
                    {
                        try { p.Kill(); } catch { }
                        return "";
                    }
                    p.WaitForExit();
                    return (output.ToString() + "\r\n" + error.ToString()).Trim();
                }
            }
            catch
            {
                return "";
            }
        }

        private bool WaitForProcessResponsive(Process process, int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (true)
            {
                if (process.WaitForExit(50))
                    return true;

                FlushPendingUiLogLines();
                if (!InvokeRequired)
                    Application.DoEvents();
                if (sw.ElapsedMilliseconds >= timeoutMs)
                    return false;
            }
        }

        private bool SteamCloudDisabledOrUnknown()
        {
            if (String.IsNullOrEmpty(sharedConfig) || !File.Exists(sharedConfig))
            {
                Log("[INFO] Steam sharedconfig.vdf not found, Steam Cloud flag cannot be read from file.");
                return true;
            }

            string text = File.ReadAllText(sharedConfig);
            int appIndex = text.IndexOf("\"1158310\"", StringComparison.OrdinalIgnoreCase);
            if (appIndex < 0)
            {
                Log("[INFO] CK3 block not found in sharedconfig.vdf, Steam Cloud flag cannot be read from file.");
                return true;
            }

            int open = text.IndexOf('{', appIndex);
            int close = FindMatchingBrace(text, open);
            if (open < 0 || close < open)
                return false;

            string block = text.Substring(open, close - open + 1);
            Match m = Regex.Match(block, "\"cloudenabled\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                Log("[INFO] Steam Cloud flag is not visible in CK3 sharedconfig block.");
                return true;
            }

            return m.Groups[1].Value == "0";
        }

        private bool SteamCloudDisabledOrUnknownQuiet()
        {
            if (String.IsNullOrEmpty(sharedConfig) || !File.Exists(sharedConfig))
                return true;

            string text = File.ReadAllText(sharedConfig);
            int appIndex = text.IndexOf("\"1158310\"", StringComparison.OrdinalIgnoreCase);
            if (appIndex < 0)
                return true;

            int open = text.IndexOf('{', appIndex);
            int close = FindMatchingBrace(text, open);
            if (open < 0 || close < open)
                return false;

            string block = text.Substring(open, close - open + 1);
            Match m = Regex.Match(block, "\"cloudenabled\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            if (!m.Success)
                return true;

            return m.Groups[1].Value == "0";
        }

        private bool CacheFoldersClean()
        {
            string localLauncher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paradox Interactive", "launcher-v2", "chromium-data");
            string[] cacheDirs = new[]
            {
                Path.Combine(ck3Docs, "shadercache"),
                Path.Combine(ck3Docs, ".launcher-cache"),
                Path.Combine(localLauncher, "Cache"),
                Path.Combine(localLauncher, "GPUCache"),
                Path.Combine(localLauncher, "DawnGraphiteCache"),
                Path.Combine(localLauncher, "DawnWebGPUCache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paradox Interactive", "launcher-v2", "cache")
            };

            bool clean = true;
            int dirtyFolders = 0;
            int totalItems = 0;
            StringBuilder detail = new StringBuilder();
            detail.AppendLine("CK3MPS cache diagnostics");
            detail.AppendLine("Stabilizer: " + AppVersion);
            detail.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            detail.AppendLine();

            foreach (string dir in cacheDirs)
            {
                int count = CountItems(dir);
                detail.AppendLine((count > 0 ? "items" : "clean") + " | " + count.ToString().PadLeft(4) + " | " + dir);
                if (count > 0)
                {
                    clean = false;
                    dirtyFolders++;
                    totalItems += count;
                }
            }
            if (clean)
            {
                Log("INFO Cache folders are clean.");
                WriteCacheDiagnostics(detail.ToString());
                return true;
            }

            string marker = StabilizerFile("ck3_stabilizer_cache_cleanup.txt");
            detail.AppendLine();
            detail.AppendLine("Cleanup marker: " + (File.Exists(marker) ? marker : "(missing)"));

            if (File.Exists(marker))
            {
                Log("INFO Cache regenerated after launch: expected (" + dirtyFolders + " folders, " + totalItems + " items).");
                WriteCacheDiagnostics(detail.ToString());
                return true;
            }

            Log("WARN Cache contains files without a stabilizer cleanup marker (" + dirtyFolders + " folders, " + totalItems + " items).");
            WriteCacheDiagnostics(detail.ToString());
            return false;
        }

        private void WriteCacheDiagnostics(string text)
        {
            try
            {
                string path = StabilizerFile("ck3_stabilizer_cache_diagnostics.txt");
                File.WriteAllText(path, text, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log("WARN Cache diagnostics could not be written: " + ex.Message);
            }
        }

        private bool FirewallRulesPresent()
        {
            string inbound = RunCommandQuiet("netsh.exe", "advfirewall firewall show rule name=\"CK3MPS - CK3 Inbound\"");
            string outbound = RunCommandQuiet("netsh.exe", "advfirewall firewall show rule name=\"CK3MPS - CK3 Outbound\"");
            if (FirewallRuleOutputLooksPresent(inbound) && FirewallRuleOutputLooksPresent(outbound))
                return true;

            string legacyInbound = RunCommandQuiet("netsh.exe", "advfirewall firewall show rule name=\"CK3 Stabilizer - CK3 Inbound\"");
            string legacyOutbound = RunCommandQuiet("netsh.exe", "advfirewall firewall show rule name=\"CK3 Stabilizer - CK3 Outbound\"");
            return FirewallRuleOutputLooksPresent(legacyInbound) && FirewallRuleOutputLooksPresent(legacyOutbound);
        }

        private bool FirewallRuleOutputLooksPresent(string output)
        {
            if (String.IsNullOrEmpty(output))
                return false;
            if (output.IndexOf("No rules match", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (output.IndexOf("CK3MPS - CK3", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return output.IndexOf("Enabled:", StringComparison.OrdinalIgnoreCase) >= 0
                || output.IndexOf("Program:", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private int CountSuspectBinaries()
        {
            if (String.IsNullOrEmpty(ck3Bin) || !Directory.Exists(ck3Bin))
                return 0;
            int count = 0;
            foreach (string name in suspectBinaryFiles)
                if (File.Exists(Path.Combine(ck3Bin, name)))
                    count++;
            return count;
        }

        private int CountFiles(string dir, string pattern)
        {
            return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern).Length : 0;
        }

        private int CountDirectories(string dir)
        {
            return Directory.Exists(dir) ? Directory.GetDirectories(dir).Length : 0;
        }

        private int CountItems(string dir)
        {
            return Directory.Exists(dir) ? Directory.GetFileSystemEntries(dir).Length : 0;
        }

    }
}



