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
        private void StabilizeSaveHygiene()
        {
            CheckSaveHygiene();

            string continuePath = Path.Combine(ck3Docs, "continue_game.json");
            if (!File.Exists(continuePath))
            {
                Log("OK   No active Continue pointer to neutralize.");
            }
            else if (!ActiveContinueSaveNameSuspicious())
            {
                Log("OK   Active Continue pointer is not autosave/recovery/desync-like.");
            }
            else
            {
                string destDir = Path.Combine(lastQuarantine, "saves");
                MoveToQuarantine(continuePath, destDir);
                Log("OK   Suspicious Continue pointer neutralized. CK3 should no longer offer the unsafe Continue button.");
            }

            QuarantineSuspiciousSaveFiles();
            QuarantineSuspiciousSteamCloudSaves();
            WriteCleanSaveLaunchNote();
            Log("INFO Load the recommended clean manual save from the Load Game menu: " + NullText(Path.GetFileName(FindBestCleanManualSave())));
        }

        private void QuarantineSuspiciousSaveFiles()
        {
            string saveDir = Path.Combine(ck3Docs, "save games");
            if (!Directory.Exists(saveDir))
            {
                Log("WARN  Save folder missing; suspicious save quarantine skipped.");
                return;
            }

            string bestClean = FindBestCleanManualSave();
            if (String.IsNullOrEmpty(bestClean) || IsSuspiciousSaveName(Path.GetFileName(bestClean)))
            {
                Log("WARN  No clean manual save candidate found; suspicious save files were left in place.");
                return;
            }

            int moved = 0;
            string destDir = Path.Combine(lastQuarantine, "saves", "suspicious");
            foreach (string file in Directory.GetFiles(saveDir, "*.ck3", SearchOption.TopDirectoryOnly))
            {
                if (!IsSuspiciousSaveName(Path.GetFileName(file)))
                    continue;
                if (String.Equals(Path.GetFullPath(file), Path.GetFullPath(bestClean), StringComparison.OrdinalIgnoreCase))
                    continue;
                MoveToQuarantine(file, destDir);
                moved++;
            }

            if (moved == 0)
                Log("OK   No suspicious save files visible in active save list.");
            else
                Log("OK   Suspicious save files moved out of active save list: " + moved);
        }

        private void QuarantineSuspiciousSteamCloudSaves()
        {
            int suspicious = CountSuspiciousSteamCloudSaveNames();
            if (suspicious == 0)
            {
                Log("OK   No suspicious Steam Cloud remote save files found.");
                return;
            }

            if (ProcessRunningContains("steam"))
            {
                Log("WARN  Suspicious Steam Cloud remote saves found, but Steam is running. Close Steam and run Maximum again to quarantine them safely.");
                return;
            }

            int moved = 0;
            string destDir = Path.Combine(lastQuarantine, "saves", "steam_cloud_remote");
            foreach (string dir in DetectSteamCloudSaveDirs())
            {
                foreach (string file in Directory.GetFiles(dir, "*.ck3", SearchOption.TopDirectoryOnly))
                {
                    if (!IsSuspiciousSaveName(Path.GetFileName(file)))
                        continue;
                    MoveToQuarantine(file, destDir);
                    moved++;
                }
            }

            Log("OK   Suspicious Steam Cloud remote save files moved to quarantine: " + moved);
        }

        private void WriteCleanSaveLaunchNote()
        {
            try
            {
                string path = StabilizerFile("ck3_stabilizer_clean_save_note.txt");
                WriteTextFileIfMeaningfullyChanged(
                    path,
                    BuildCleanSaveLaunchNoteText(),
                    "FILE Clean save launch note written: ",
                    "INFO Clean save launch note already up to date: ",
                    true);
            }
            catch (Exception ex)
            {
                Log("WARN Clean save launch note could not be written: " + ex.Message);
            }
        }

        private string BuildCleanSaveLaunchNoteText()
        {
            string bestClean = FindBestCleanManualSave();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 clean save launch note");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Do not use Continue for serious MP if it points to autosave/recovery/backup/desync-like state.");
            sb.AppendLine("Maximum moves autosave/recovery/backup/desync-like save files to quarantine when a clean manual save exists.");
            sb.AppendLine("Maximum also reports Steam Cloud remote save cache and quarantines suspicious remote saves when Steam is closed.");
            sb.AppendLine("Recommended clean manual save: " + NullText(Path.GetFileName(bestClean)));
            sb.AppendLine("Recommended clean manual save path: " + NullText(bestClean));
            sb.AppendLine("Recommended clean manual save hash: " + FileHashOrMissing(bestClean));
            sb.AppendLine("Recommended clean manual save readable: " + YesNo(BestCleanSaveReadable()));
            sb.AppendLine("Recommended clean manual save version: " + NullText(ExtractSaveMetaValue(bestClean, "version")));
            sb.AppendLine("Recommended clean manual save date: " + NullText(ExtractSaveMetaValue(bestClean, "meta_date")));
            sb.AppendLine("Recommended clean manual save player: " + NullText(ExtractSaveMetaValue(bestClean, "meta_player_name")));
            sb.AppendLine("Recommended clean manual save title: " + NullText(ExtractSaveMetaValue(bestClean, "meta_title_name")));
            sb.AppendLine();
            sb.AppendLine("Start flow");
            sb.AppendLine("- Open CK3.");
            sb.AppendLine("- Use Load Game, not Continue.");
            sb.AppendLine("- Load the recommended local manual save.");
            sb.AppendLine("- Create a fresh multiplayer lobby and wait for all players before unpausing.");
            return sb.ToString();
        }

        private void CheckLatestOosReportReadOnly()
        {
            string latest = FindLatestOosMetadataFile();
            if (String.IsNullOrEmpty(latest))
            {
                Log("OK   No OOS metadata found in active OOS folder or latest quarantine.");
                return;
            }

            Log("INFO Latest OOS metadata: " + latest);
            LogOosMetadataSummary(latest);
            Check("Latest OOS metadata can be parsed", true);
        }

        private void AnalyzeLatestOosReport()
        {
            string latest = FindLatestOosMetadataFile();
            string output = StabilizerFile("ck3_stabilizer_latest_oos_summary.txt");
            List<string> signalLines;
            string summary = BuildLatestOosSummaryText(latest, out signalLines);
            WriteTextFileIfMeaningfullyChanged(
                output,
                summary,
                String.IsNullOrEmpty(latest) ? "OK   No OOS metadata found. Empty OOS summary written: " : "OK   Latest OOS summary written: ",
                String.IsNullOrEmpty(latest) ? "INFO No OOS metadata summary change needed: " : "INFO Latest OOS summary already up to date: ",
                true);
            WriteOosHistoryTimeline();

            if (String.IsNullOrEmpty(latest))
                return;

            LogOosMetadataSummary(latest);
            foreach (string line in signalLines)
                Log("INFO " + line);
            foreach (string line in BuildOosActionPlan(signalLines))
                Log("INFO Action: " + line);
        }

        private string BuildLatestOosSummaryText(string latest, out List<string> signalLines)
        {
            signalLines = new List<string>();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 latest OOS summary");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            if (String.IsNullOrEmpty(latest))
            {
                sb.AppendLine("No OOS metadata found in active OOS folder or latest quarantine.");
                return sb.ToString();
            }

            sb.AppendLine("Metadata: " + latest);
            sb.AppendLine();
            foreach (string line in ExtractOosSummaryLines(latest))
                sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("Nearby log analysis");
            signalLines = new List<string>(AnalyzeOosSiblingLogs(latest));
            foreach (string line in signalLines)
                sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("Suggested action");
            foreach (string line in BuildOosActionPlan(signalLines))
                sb.AppendLine(line);
            return sb.ToString();
        }

        private string FindLatestOosMetadataFile()
        {
            List<string> files = new List<string>();
            string activeOos = Path.Combine(ck3Docs, "oos");
            if (Directory.Exists(activeOos))
                files.AddRange(Directory.GetFiles(activeOos, "oos_metadata_*.txt", SearchOption.AllDirectories));

            if (!String.IsNullOrEmpty(lastQuarantine))
            {
                string quarantineReports = Path.Combine(lastQuarantine, "reports");
                if (Directory.Exists(quarantineReports))
                    files.AddRange(Directory.GetFiles(quarantineReports, "oos_metadata_*.txt", SearchOption.AllDirectories));
            }

            if (Directory.Exists(ck3Docs))
            {
                foreach (string quarantine in Directory.GetDirectories(stabilizerRoot, "_ck3_stabilizer_quarantine_*"))
                {
                    string reports = Path.Combine(quarantine, "reports");
                    if (Directory.Exists(reports))
                        files.AddRange(Directory.GetFiles(reports, "oos_metadata_*.txt", SearchOption.AllDirectories));
                }
            }

            if (files.Count == 0)
                return "";

            files.Sort(delegate (string a, string b)
            {
                return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
            });
            return files[0];
        }

        private List<string> FindAllOosMetadataFiles()
        {
            List<string> files = new List<string>();
            string activeOos = Path.Combine(ck3Docs, "oos");
            if (Directory.Exists(activeOos))
                files.AddRange(Directory.GetFiles(activeOos, "oos_metadata_*.txt", SearchOption.AllDirectories));

            if (Directory.Exists(ck3Docs))
            {
                foreach (string quarantine in Directory.GetDirectories(stabilizerRoot, "_ck3_stabilizer_quarantine_*"))
                {
                    string reports = Path.Combine(quarantine, "reports");
                    if (Directory.Exists(reports))
                        files.AddRange(Directory.GetFiles(reports, "oos_metadata_*.txt", SearchOption.AllDirectories));
                }
            }

            files.Sort(delegate (string a, string b)
            {
                return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
            });
            return files;
        }

        private void WriteOosHistoryTimeline()
        {
            string path = StabilizerFile("ck3_stabilizer_oos_history.txt");
            WriteTextFileIfMeaningfullyChanged(
                path,
                BuildOosHistoryTimelineText(),
                "FILE OOS history timeline written: ",
                "INFO OOS history timeline already up to date: ",
                true);
        }

        private string BuildOosHistoryTimelineText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 OOS history timeline");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            List<string> files = FindAllOosMetadataFiles();
            sb.AppendLine("OOS metadata files found: " + files.Count);
            sb.AppendLine();
            sb.AppendLine("Type summary");
            foreach (string line in BuildOosTypeSummary(files))
                sb.AppendLine("- " + line);
            sb.AppendLine();
            foreach (string file in files)
            {
                sb.AppendLine(Path.GetFileName(file) + " | " + File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Path: " + file);
                foreach (string line in ExtractOosSummaryLines(file))
                    sb.AppendLine("- " + line);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private IEnumerable<string> BuildOosTypeSummary(List<string> files)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                string type = ExtractMetadataValue(file, "oos_type");
                if (String.IsNullOrEmpty(type))
                    type = "(unknown)";
                if (!counts.ContainsKey(type))
                    counts[type] = 0;
                counts[type]++;
            }

            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, int> kv in counts)
                lines.Add(kv.Key + ": " + kv.Value);
            if (lines.Count == 0)
                lines.Add("(none)");
            lines.Sort(StringComparer.OrdinalIgnoreCase);
            return lines;
        }

        private string ExtractMetadataValue(string path, string key)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                Match m = Regex.Match(text, "(?im)^\\s*" + Regex.Escape(key) + "\\s*[:=]\\s*(.+?)\\s*$");
                return m.Success ? m.Groups[1].Value.Trim() : "";
            }
            catch
            {
                return "";
            }
        }

        private void LogOosMetadataSummary(string path)
        {
            foreach (string line in ExtractOosSummaryLines(path))
                Log("INFO " + line);
        }

        private IEnumerable<string> ExtractOosSummaryLines(string path)
        {
            List<string> lines = new List<string>();
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                AddMatchedOosLine(lines, text, "oos_type");
                AddMatchedOosLine(lines, text, "oos_machine_id");
                AddMatchedOosLine(lines, text, "local_machine_id");
                AddMatchedOosLine(lines, text, "game_version");
                AddMatchedOosLine(lines, text, "checksum");
                AddMatchedOosLine(lines, text, "date");
                if (lines.Count == 0)
                    lines.Add("Metadata found, but common OOS fields were not detected.");
            }
            catch (Exception ex)
            {
                lines.Add("Could not read metadata: " + ex.Message);
            }
            return lines;
        }

        private IEnumerable<string> AnalyzeOosSiblingLogs(string metadataPath)
        {
            List<string> results = new List<string>();
            string dir = Path.GetDirectoryName(metadataPath);
            if (String.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                results.Add("OOS folder not found for sibling log analysis.");
                return results;
            }

            string combined = "";
            foreach (string pattern in new[] { "error_*.txt", "game_*.txt", "multiplayer_*.log", "multiplayer_*.txt" })
            {
                foreach (string file in Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        combined += "\n--- " + Path.GetFileName(file) + " ---\n";
                        combined += File.ReadAllText(file, Encoding.UTF8);
                    }
                    catch { }
                }
            }

            if (String.IsNullOrWhiteSpace(combined))
            {
                results.Add("No sibling error/game/multiplayer logs found next to metadata.");
                return results;
            }

            AddSignal(results, combined, "Relations", "Relation-state OOS signal found.");
            AddSignal(results, combined, "Modifiers", "Modifier-state OOS signal found.");
            AddSignal(results, combined, "Failed context switch", "Script context failure found.");
            AddSignal(results, combined, "target character was null", "Null character target found.");
            AddSignal(results, combined, "has_relation_", "Relation trigger errors found.");
            AddSignal(results, combined, "opinion trigger", "Opinion/relation trigger errors found.");
            AddSignal(results, combined, "Completed MP save transfer", "Hotjoin/save-transfer completed before later issue.");
            AddSignal(results, combined, "save transfer", "Save-transfer text found in multiplayer logs.");
            AddSignal(results, combined, "hotjoin", "Hotjoin text found in OOS logs.");
            AddSignal(results, combined, "checksum", "Checksum text found in OOS logs.");
            AddSignal(results, combined, "checksum differs", "Checksum mismatch text found in OOS logs.");
            AddSignal(results, combined, "mismatch", "Mismatch text found in OOS logs.");
            AddSignal(results, combined, "data mismatch", "Data mismatch text found in OOS logs.");
            AddSignal(results, combined, "out of sync", "Explicit out-of-sync text found in logs.");
            AddSignal(results, combined, "desync", "Explicit desync text found in logs.");
            AddSignal(results, combined, "random", "Random-related text found in logs.");
            AddSignal(results, combined, "seed", "Seed-related text found in logs.");
            AddSignal(results, combined, "malformed token", "Malformed token text found in logs.");
            AddSignal(results, combined, "debug_mode", "debug_mode text found in OOS logs.");

            if (results.Count == 0)
                results.Add("No known high-signal OOS patterns found in sibling logs.");
            return results;
        }

        private void AddSignal(List<string> results, string text, string needle, string message)
        {
            if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                results.Add(message);
        }

        private IEnumerable<string> BuildOosActionPlan(List<string> signals)
        {
            List<string> actions = new List<string>();
            string joined = String.Join("\n", signals.ToArray()).ToLowerInvariant();

            if (joined.Contains("relation") || joined.Contains("context") || joined.Contains("null character") || joined.Contains("opinion"))
            {
                actions.Add("Treat this as state divergence, not just an internet issue.");
                actions.Add("Stop the session instead of hotjoin-looping.");
                actions.Add("Roll back to the earliest clean host save before the divergence.");
                actions.Add("Have every player clear player UI state and use the same pdx_settings/game rules before retrying.");
            }

            if (joined.Contains("modifier"))
            {
                actions.Add("Verify all players use identical game version, DLC state, no mods and no debug_mode.");
                actions.Add("Prefer a clean local host save over a cloud/auto/recovery save.");
            }

            if (joined.Contains("hotjoin") || joined.Contains("save-transfer"))
                actions.Add("Avoid hotjoin into a running simulation; load lobby, wait for everyone, then unpause.");

            if (joined.Contains("checksum") || joined.Contains("mismatch") || joined.Contains("debug_mode"))
                actions.Add("Force debug_mode off, verify Steam files and compare the MP parity manifest with every player.");

            if (joined.Contains("random") || joined.Contains("seed"))
                actions.Add("Random/seed text means every player should use -noasync and avoid high speed immediately after loading.");

            if (joined.Contains("malformed token"))
                actions.Add("Malformed token text often points to a stale/corrupted local config or cache; rebuild launcher DB and clear CK3/launcher caches before retry.");

            if (actions.Count == 0)
            {
                actions.Add("Collect host and client OOS folders and compare MP parity manifests.");
                actions.Add("If OOS repeats at the same date, roll back to an earlier clean save.");
            }

            return actions;
        }

        private void AddMatchedOosLine(List<string> lines, string text, string key)
        {
            Match m = Regex.Match(text, "(?im)^\\s*" + Regex.Escape(key) + "\\s*[:=]\\s*(.+?)\\s*$");
            if (m.Success)
                lines.Add(key + ": " + m.Groups[1].Value.Trim());
        }

        private void WriteMultiplayerParityManifest()
        {
            string path = StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt");
            WriteTextFileIfMeaningfullyChanged(
                path,
                BuildMultiplayerParityManifestText(),
                "OK   MP parity manifest written: ",
                "INFO MP parity manifest already up to date: ",
                true);
            WriteOosRiskScoreReport();
        }

        private string BuildMultiplayerParityManifestText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 multiplayer parity manifest");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Compare this file with every player before a serious MP session.");
            sb.AppendLine();
            sb.AppendLine("Game");
            sb.AppendLine("- CK3 Documents: " + ck3Docs);
            sb.AppendLine("- CK3 install: " + NullText(ck3Install));
            sb.AppendLine("- Steam libraries detected: " + String.Join("; ", DetectSteamLibraries().ToArray()));
            sb.AppendLine("- Installed version: " + NullText(DetectInstalledVersion()));
            sb.AppendLine("- Active save version: " + NullText(DetectActiveSaveVersion()));
            sb.AppendLine("- Installed/save version parity: " + YesNo(VersionParityBaselineOk()));
            sb.AppendLine("- Steam build: " + NullText(DetectBuildId()));
            sb.AppendLine("- Steam branch: " + NullText(DetectSteamBranch()));
            sb.AppendLine("- Steam update complete: " + YesNo(SteamUpdateComplete()));
            sb.AppendLine("- Local parity fingerprint: " + BuildLocalParityFingerprint());
            sb.AppendLine();
            sb.AppendLine("Launch and checksum risks");
            sb.AppendLine("- Launch options: " + NullText(ExtractSteamLaunchOptions()));
            sb.AppendLine("- -noasync: " + YesNo(HasNoAsync()));
            sb.AppendLine("- risky launch options absent: " + YesNo(!HasRiskyLaunchOptions()));
            sb.AppendLine("- Steam Cloud disabled or not visible: " + YesNo(SteamCloudDisabledOrUnknown()));
            sb.AppendLine("- Steam Cloud remote save files: " + CountSteamCloudSaveFiles());
            sb.AppendLine("- Steam Cloud remote suspicious saves: " + CountSuspiciousSteamCloudSaveNames());
            sb.AppendLine("- Steam Cloud remote fingerprint: " + BuildSteamCloudSaveFingerprint());
            sb.AppendLine("- Non-vanilla loader files: " + CountSuspectBinaries());
            sb.AppendLine("- Settings guard report: " + YesNo(File.Exists(StabilizerFile("ck3_stabilizer_settings_guard.txt"))));
            sb.AppendLine();
            sb.AppendLine("Mods and launcher");
            sb.AppendLine("- No active mods: " + YesNo(NoActiveMods()));
            sb.AppendLine("- No disabled DLCs: " + YesNo(NoDisabledDlcs()));
            sb.AppendLine("- DLC loadout clean: " + YesNo(DlcLoadProfileClean()));
            sb.AppendLine("- Local .mod descriptors: " + CountFiles(Path.Combine(ck3Docs, "mod"), "*.mod"));
            sb.AppendLine("- Launcher DB present: " + YesNo(File.Exists(Path.Combine(ck3Docs, "launcher-v2.sqlite"))));
            sb.AppendLine("- Local .mod descriptor names: " + ListFileNames(Path.Combine(ck3Docs, "mod"), "*.mod"));
            sb.AppendLine("- dlc_load enabled_mods: " + ExtractJsonArraySummary(Path.Combine(ck3Docs, "dlc_load.json"), "enabled_mods"));
            sb.AppendLine("- dlc_load disabled_dlcs: " + ExtractJsonArraySummary(Path.Combine(ck3Docs, "dlc_load.json"), "disabled_dlcs"));
            sb.AppendLine("- DLC footprint files: " + CountDlcFootprintFiles());
            sb.AppendLine("- DLC footprint fingerprint: " + BuildDlcFootprintFingerprint());
            sb.AppendLine("- Steam Workshop CK3 items: " + CountSteamWorkshopItems());
            sb.AppendLine("- Steam Workshop fingerprint: " + BuildSteamWorkshopFingerprint());
            sb.AppendLine();
            sb.AppendLine("Saves and settings");
            sb.AppendLine("- Active save version OK: " + YesNo(ActiveSaveVersionOk()));
            sb.AppendLine("- Active continue title: " + NullText(DetectActiveSaveTitle()));
            sb.AppendLine("- Active continue title suspicious: " + YesNo(ActiveContinueSaveNameSuspicious()));
            sb.AppendLine("- Core stable pdx_settings: " + YesNo(StableCriticalSettingsOk()));
            sb.AppendLine("- Full exact pdx_settings profile: " + YesNo(StableSettingsOk()));
            sb.AppendLine("- Player UI state folder present: " + YesNo(Directory.Exists(Path.Combine(ck3Docs, "player"))));
            sb.AppendLine("- Suspicious save names: " + CountSuspiciousSaveNames());
            string latestSave = FindLatestLocalSave();
            sb.AppendLine("- Latest local save: " + NullText(Path.GetFileName(latestSave)));
            sb.AppendLine("- Latest local save size: " + FileSizeOrMissing(latestSave));
            sb.AppendLine("- Latest local save hash: " + FileHashOrMissing(latestSave));
            string bestClean = FindBestCleanManualSave();
            sb.AppendLine("- Best clean manual save candidate: " + NullText(Path.GetFileName(bestClean)));
            sb.AppendLine("- Best clean manual save hash: " + FileHashOrMissing(bestClean));
            sb.AppendLine("- Best clean manual save readable: " + YesNo(BestCleanSaveReadable()));
            sb.AppendLine("- Best clean manual save version: " + NullText(ExtractSaveMetaValue(bestClean, "version")));
            sb.AppendLine("- Best clean manual save version OK: " + YesNo(BestCleanSaveVersionOk()));
            sb.AppendLine("- Best clean manual save date: " + NullText(ExtractSaveMetaValue(bestClean, "meta_date")));
            sb.AppendLine("- Best clean manual save player: " + NullText(ExtractSaveMetaValue(bestClean, "meta_player_name")));
            sb.AppendLine("- Best clean manual save title: " + NullText(ExtractSaveMetaValue(bestClean, "meta_title_name")));
            sb.AppendLine("- Latest OOS metadata: " + NullText(FindLatestOosMetadataFile()));
            sb.AppendLine();
            sb.AppendLine("Network");
            sb.AppendLine(BuildNetworkRouteSummary());
            sb.AppendLine();
            sb.AppendLine("File hashes");
            sb.AppendLine("- ck3.exe: " + FileHashOrMissing(Path.Combine(ck3Bin, "ck3.exe")));
            sb.AppendLine("- launcher-settings.json: " + FileHashOrMissing(Path.Combine(ck3Install, "launcher", "launcher-settings.json")));
            sb.AppendLine("- appmanifest_1158310.acf: " + FileHashOrMissing(appManifest));
            sb.AppendLine("- dlc_load.json: " + FileHashOrMissing(Path.Combine(ck3Docs, "dlc_load.json")));
            sb.AppendLine("- pdx_settings.txt: " + FileHashOrMissing(Path.Combine(ck3Docs, "pdx_settings.txt")));
            return sb.ToString();
        }

        private void WriteOosRiskScoreReport()
        {
            string path = StabilizerFile("ck3_stabilizer_oos_risk_score.txt");
            string content = BuildOosRiskScoreReportText();
            string level = ExtractRiskLevel(content);
            string score = ExtractRiskValue(content);
            WriteTextFileIfMeaningfullyChanged(
                path,
                content,
                "RISK  OOS risk score: " + level + " (" + score + "). Report: ",
                "INFO OOS risk score already up to date: ",
                true);
        }

        private string BuildOosRiskScoreReportText()
        {
            List<string> risks = new List<string>();
            int score = 0;

            AddRisk(risks, ref score, !HasNoAsync(), 15, "Steam launch option -noasync is missing.");
            AddRisk(risks, ref score, HasRiskyLaunchOptions(), 20, "Risky renderer/debug launch option is present.");
            AddRisk(risks, ref score, HasRiskyLaunchOptions(), 16, "Risky debug/developer launch option is present.");
            AddRisk(risks, ref score, !SteamCloudDisabledOrUnknownQuiet(), 10, "Steam Cloud still appears enabled in Steam config.");
            AddRisk(risks, ref score, !NoActiveMods(), 25, "dlc_load.json has active mods or cannot be confirmed clean.");
            AddRisk(risks, ref score, !NoDisabledDlcs(), 18, "dlc_load.json has disabled DLC entries; all players must intentionally match DLC state.");
            AddRisk(risks, ref score, CountFiles(Path.Combine(ck3Docs, "mod"), "*.mod") > 0, 10, "Local .mod descriptors are still visible.");
            AddRisk(risks, ref score, CountSuspectBinaries() > 0, 30, "Non-vanilla loader files are present in CK3 binaries.");
            AddRisk(risks, ref score, !StableCriticalSettingsOk(), 15, "Core stable MP settings are not currently applied.");
            AddRisk(risks, ref score, Directory.Exists(Path.Combine(ck3Docs, "player")), 8, "Player UI state folder exists and may preserve old local state.");
            AddRisk(risks, ref score, !ActiveSaveVersionOk(), 25, "Active continue save version differs from installed CK3 version.");
            AddRisk(risks, ref score, HasRiskyNetworkRouteProfile(), 8, "Multiple/Wi-Fi/VPN/PPPoE/mobile/proxy/CGNAT/IPv6-only/jitter/DNS route profile detected.");
            AddRisk(risks, ref score, !FirewallRulesPresent(), 6, "CK3 firewall allow rules are missing.");
            AddRisk(risks, ref score, !String.IsNullOrEmpty(FindLatestOosMetadataFile()), 5, "Previous OOS metadata exists; compare it before starting the next session.");
            AddRisk(risks, ref score, CountSuspiciousSaveNames() > 0, 8, "Recovery/autosave/cloud/desync-like saves are visible in the save list.");
            AddRisk(risks, ref score, CountSuspiciousSteamCloudSaveNames() > 0, 6, "Steam Cloud remote cache contains autosave/recovery/desync-like saves.");
            AddRisk(risks, ref score, ActiveContinueSaveNameSuspicious(), 18, "Active continue save name is autosave/recovery/backup/desync-like.");
            AddRisk(risks, ref score, !BestCleanSaveReadable(), 18, "Recommended clean manual save header could not be read.");
            AddRisk(risks, ref score, !BestCleanSaveVersionOk(), 18, "Recommended clean manual save version differs from installed CK3 version.");
            AddRisk(risks, ref score, !String.Equals(DetectSteamBranch(), "public", StringComparison.OrdinalIgnoreCase), 12, "Steam beta branch is not public/default.");
            AddRisk(risks, ref score, CountDlcFootprintFiles() == 0, 6, "DLC footprint could not be detected; compare DLC ownership/state manually.");
            AddRisk(risks, ref score, CountSteamWorkshopItems() > 0 && !NoActiveMods(), 12, "Workshop content exists while active mods are not confirmed clean.");
            AddRisk(risks, ref score, !ServiceRunningQuiet("W32Time"), 4, "Windows Time service is not running; online session/auth timing hygiene is weaker.");
            AddRisk(risks, ref score, !ServiceRunningQuiet("Dnscache"), 4, "DNS Client service is not running; name resolution can be less predictable.");
            AddRisk(risks, ref score, !ServiceRunningQuiet("NlaSvc"), 4, "Network Location Awareness service is not running; Windows network profile detection can be less predictable.");

            string level = score >= 60 ? "HIGH" : (score >= 25 ? "MEDIUM" : "LOW");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 OOS risk score");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Risk: " + level + " (" + score + ")");
            sb.AppendLine();
            if (risks.Count == 0)
            {
                sb.AppendLine("No major local OOS risk factors detected.");
            }
            else
            {
                sb.AppendLine("Reasons");
                foreach (string risk in risks)
                    sb.AppendLine("- " + risk);
            }
            sb.AppendLine();
            sb.AppendLine("Network route summary");
            sb.AppendLine(BuildNetworkRouteSummary());
            sb.AppendLine();
            sb.AppendLine("Next actions");
            foreach (string action in BuildRiskActionPlan(risks))
                sb.AppendLine("- " + action);
            sb.AppendLine("- Check ck3_stabilizer_session_verdict.txt before launching the serious MP session.");
            return sb.ToString();
        }

        private string ExtractRiskLevel(string text)
        {
            Match match = Regex.Match(text ?? "", @"(?im)^Risk:\s+([A-Z]+)\s+\(");
            return match.Success ? match.Groups[1].Value : "UNKNOWN";
        }

        private string ExtractRiskValue(string text)
        {
            Match match = Regex.Match(text ?? "", @"(?im)^Risk:\s+[A-Z]+\s+\((\d+)\)");
            return match.Success ? match.Groups[1].Value : "?";
        }

        private IEnumerable<string> BuildRiskActionPlan(List<string> risks)
        {
            List<string> actions = new List<string>();
            string joined = String.Join("\n", risks.ToArray()).ToLowerInvariant();
            if (joined.Contains("active continue save"))
                actions.Add("Load a clean manual local save instead of autosave/recovery/desync-like continue save. Suggested: " + NullText(Path.GetFileName(FindBestCleanManualSave())));
            if (joined.Contains("recommended clean manual save"))
                actions.Add("Pick a different clean manual save or open CK3 once and create a fresh local manual save before hosting.");
            if (joined.Contains("windows time"))
                actions.Add("Start Windows Time service or run Maximum as administrator once to let the stabilizer try a time resync.");
            if (joined.Contains("launch option"))
                actions.Add("Run Steam stabilization step and confirm launch options contain -noasync without debug/developer flags.");
            if (joined.Contains("workshop") || joined.Contains("mods"))
                actions.Add("Keep enabled_mods empty and compare Workshop/mod state with every player.");
            if (joined.Contains("disabled dlc"))
                actions.Add("Clear disabled_dlcs or make every player intentionally use the same DLC loadout before hosting.");
            if (joined.Contains("recovery/autosave") || joined.Contains("desync-like saves"))
                actions.Add("Do not pick autosave/recovery/desync-like saves from Load Game; use the clean manual save note.");
            if (joined.Contains("steam cloud remote"))
                actions.Add("Close Steam and run Maximum again so suspicious Steam Cloud remote saves can be quarantined.");
            if (joined.Contains("route"))
                actions.Add("Use one intentional network route during CK3; avoid Ethernet plus Wi-Fi plus VPN at the same time.");
            if (actions.Count == 0)
                actions.Add("Compare MP parity manifest with every player, then start from a fresh lobby before unpausing.");
            return actions;
        }

        private void AddRisk(List<string> risks, ref int score, bool condition, int weight, string message)
        {
            if (!condition)
                return;
            score += weight;
            risks.Add("+" + weight + " " + message);
        }

        private void WriteOosEvidencePack()
        {
            WritePortableTransferNotes();
            WriteExpectedProfileSnapshot("evidence pack");
            WriteRuntimeVerificationReport();
            WritePreSessionPlan();
            WriteSessionVerdictReport();
            WriteCleanSaveLaunchNote();

            string index = StabilizerFile("ck3_stabilizer_evidence_pack_index.txt");
            WriteTextFileIfMeaningfullyChanged(
                index,
                BuildOosEvidencePackIndexText(),
                "FILE OOS evidence pack index written: ",
                "INFO OOS evidence pack index already up to date: ",
                true);
        }

        private string BuildOosEvidencePackIndexText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 OOS evidence summary");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Folder: " + stabilizerRoot);
            sb.AppendLine();
            sb.AppendLine("Purpose");
            sb.AppendLine("- Keep this compact summary after an OOS so host/client facts can be compared.");
            sb.AppendLine("- Save files and game logs are fingerprinted/referenced, not copied.");
            sb.AppendLine();
            sb.AppendLine("Session facts");
            sb.AppendLine("- Installed version: " + NullText(DetectInstalledVersion()));
            sb.AppendLine("- Steam build: " + NullText(DetectBuildId()));
            sb.AppendLine("- Steam branch: " + NullText(DetectSteamBranch()));
            sb.AppendLine("- Active continue title: " + NullText(DetectActiveSaveTitle()));
            sb.AppendLine("- Active continue title suspicious: " + YesNo(ActiveContinueSaveNameSuspicious()));
            sb.AppendLine("- Active save version: " + NullText(DetectActiveSaveVersion()));
            sb.AppendLine("- Latest local save: " + NullText(FindLatestLocalSave()));
            sb.AppendLine("- Latest local save hash: " + FileHashOrMissing(FindLatestLocalSave()));
            sb.AppendLine("- DLC loadout clean: " + YesNo(DlcLoadProfileClean()));
            sb.AppendLine("- Steam Cloud remote save files: " + CountSteamCloudSaveFiles());
            sb.AppendLine("- Steam Cloud remote suspicious saves: " + CountSuspiciousSteamCloudSaveNames());
            sb.AppendLine("- Steam Cloud remote fingerprint: " + BuildSteamCloudSaveFingerprint());
            sb.AppendLine("- DLC footprint fingerprint: " + BuildDlcFootprintFingerprint());
            sb.AppendLine("- Steam Workshop fingerprint: " + BuildSteamWorkshopFingerprint());
            sb.AppendLine("- Local parity fingerprint: " + BuildLocalParityFingerprint());
            sb.AppendLine();
            sb.AppendLine("Reference files");
            sb.AppendLine("- continue_game.json: " + FileTimeHashLine(Path.Combine(ck3Docs, "continue_game.json")));
            sb.AppendLine("- dlc_load.json: " + FileTimeHashLine(Path.Combine(ck3Docs, "dlc_load.json")));
            sb.AppendLine("- pdx_settings.txt: " + FileTimeHashLine(Path.Combine(ck3Docs, "pdx_settings.txt")));
            sb.AppendLine("- appmanifest: " + FileTimeHashLine(appManifest));
            sb.AppendLine("- debug.log: " + FileTimeHashLine(Path.Combine(ck3Docs, "logs", "debug.log")));
            sb.AppendLine("- game.log: " + FileTimeHashLine(Path.Combine(ck3Docs, "logs", "game.log")));
            sb.AppendLine("- error.log: " + FileTimeHashLine(Path.Combine(ck3Docs, "logs", "error.log")));
            sb.AppendLine("- system.log: " + FileTimeHashLine(Path.Combine(ck3Docs, "logs", "system.log")));
            string latestOos = FindLatestOosMetadataFile();
            if (!String.IsNullOrEmpty(latestOos))
                sb.AppendLine("- latest OOS metadata: " + latestOos + " | " + FileTimeHashLine(latestOos));
            else
                sb.AppendLine("- latest OOS metadata: (none)");
            return sb.ToString();
        }

        private void CopyEvidenceFile(string source, string packDir, StringBuilder sb)
        {
            if (String.IsNullOrEmpty(source) || !File.Exists(source))
            {
                if (sb != null)
                    sb.AppendLine("- Missing: " + NullText(source));
                return;
            }

            try
            {
                Directory.CreateDirectory(packDir);
                string dest = UniquePath(Path.Combine(packDir, SafeFileName(Path.GetFileName(source))));
                File.Copy(source, dest, true);
                if (sb != null)
                    sb.AppendLine("- " + Path.GetFileName(source) + " -> " + dest + " | " + FileSizeOrMissing(source) + " | " + FileHashOrMissing(source));
            }
            catch (Exception ex)
            {
                if (sb != null)
                    sb.AppendLine("- Copy failed: " + source + " | " + ex.Message);
            }
        }

        private void CheckOosEvidencePackReadOnly()
        {
            string index = StabilizerFile("ck3_stabilizer_evidence_pack_index.txt");
            Check("OOS evidence pack index exists", File.Exists(index));
            Log("INFO Evidence pack is a diagnostic snapshot for host/client comparison after OOS.");
        }

        private bool ServiceRunningQuiet(string serviceName)
        {
            string output = RunCommandQuiet("sc.exe", "query \"" + serviceName + "\"");
            return output.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyWindowsGameNetworkProfile()
        {
            string ck3Exe = String.IsNullOrEmpty(ck3Bin) ? "" : Path.Combine(ck3Bin, "ck3.exe");

            RecordSystemSnapshot("Windows Time service before CK3MPS resync", "sc query W32Time", RunCommandQuiet("sc.exe", "query W32Time"));
            SetRegistryDword(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
            SetRegistryDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);
            SetRegistryDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2);
            SetRegistryDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1);
            SetRegistryDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 1);
            SetRegistryDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_EFSEFeatureFlags", 0);

            if (File.Exists(ck3Exe))
            {
                SetRegistryString(Registry.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", ck3Exe, "GpuPreference=2;");
                SetRegistryString(Registry.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", ck3Exe, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE");
            }
            else
            {
                Log("WARN  ck3.exe not found, GPU preference and fullscreen-optimization registry entries skipped.");
            }

            ApplyAdaptiveNetworkTcpProfile();
            RunCommand("sc.exe", "start W32Time", true);
            RunCommand("w32tm.exe", "/resync", true);

            if (IsAdministrator())
            {
                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xffffffff));
                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 10);
                Log("OK   HKLM multimedia network profile applied.");
            }
            else
            {
                Log("INFO HKLM multimedia profile skipped because the program is not running as administrator.");
            }
        }

        private void ApplyAdaptiveNetworkTcpProfile()
        {
            NetworkRouteProfile profile = AnalyzeNetworkRouteProfile(false);
            Log("INFO Adaptive network apply profile");
            Log("INFO Routes: gateway=" + profile.GatewayAdapters + " ipv6_gateway=" + profile.Ipv6GatewayAdapters + " physical=" + profile.PhysicalRoutes + " wifi=" + profile.WifiRoutes + " vpn=" + profile.VpnRoutes + " pppoe=" + profile.PppoeRoutes + " mobile=" + profile.MobileRoutes + " low_speed=" + profile.LowSpeedRoutes + " proxy=" + (profile.ProxyDetected ? "yes" : "no"));
            LogAdaptiveNetworkPlan(profile);
            RecordSystemSnapshot("TCP global settings before CK3MPS network profile", "netsh interface tcp show global", RunCommandQuiet("netsh.exe", "interface tcp show global"));

            if (!TcpGlobalSettingsNeedUpdate())
            {
                Log("OK   TCP global profile already matches: rss=enabled autotuninglevel=normal ecncapability=disabled timestamps=disabled.");
                return;
            }

            if (profile.HasPppoe || profile.HasIpv6OnlyOrDsLiteSignal)
            {
                RunCommand("netsh.exe", "interface tcp set global rss=enabled autotuninglevel=normal ecncapability=disabled timestamps=disabled", true);
                Log("INFO PPPoE/IPv6-DS-Lite profile applied: conservative TCP global settings only; MTU and adapter offload are not changed.");
                return;
            }

            if (profile.HasMultipleGateways || profile.HasVpn || profile.HasMobile || profile.ProxyDetected || profile.HasCgnatSignal)
            {
                RunCommand("netsh.exe", "interface tcp set global rss=enabled autotuninglevel=normal ecncapability=disabled timestamps=disabled", true);
                Log("INFO Multi-route/VPN/mobile/proxy/CGNAT profile applied: routes, proxy and router NAT are not changed automatically.");
                return;
            }

            if (profile.HasWifi || profile.HasLowSpeed || profile.HasJitterOrLoss || profile.HasDnsFilteringSignal)
            {
                RunCommand("netsh.exe", "interface tcp set global rss=enabled autotuninglevel=normal ecncapability=disabled timestamps=disabled", true);
                Log("INFO Wi-Fi/low-speed/jitter/DNS-filter profile applied: conservative TCP settings plus adapter power profile step.");
                return;
            }

            RunCommand("netsh.exe", "interface tcp set global rss=enabled autotuninglevel=normal ecncapability=disabled timestamps=disabled", true);
            Log("OK   Single-route Ethernet profile applied.");
        }

        private void CheckWindowsGameNetworkProfileReadOnly()
        {
            string ck3Exe = String.IsNullOrEmpty(ck3Bin) ? "" : Path.Combine(ck3Bin, "ck3.exe");
            Check("Windows Game DVR capture disabled", RegistryDwordEquals(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0)
                && RegistryDwordEquals(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0));
            Check("CK3 high-performance GPU preference set", File.Exists(ck3Exe) && RegistryStringContains(Registry.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", ck3Exe, "GpuPreference=2"));
            Check("CK3 fullscreen optimization profile set", File.Exists(ck3Exe) && RegistryStringContains(Registry.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", ck3Exe, "DISABLEDXMAXIMIZEDWINDOWEDMODE"));
            if (IsAdministrator())
                Check("HKLM multimedia network profile set", RegistryDwordEquals(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 10));
            else
                Log("INFO HKLM multimedia profile can only be checked fully when running as administrator.");
        }

        private bool WindowsGameNetworkProfileOk()
        {
            string ck3Exe = String.IsNullOrEmpty(ck3Bin) ? "" : Path.Combine(ck3Bin, "ck3.exe");
            bool userProfile = RegistryDwordEquals(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0)
                && RegistryDwordEquals(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0)
                && File.Exists(ck3Exe)
                && RegistryStringContains(Registry.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", ck3Exe, "GpuPreference=2")
                && RegistryStringContains(Registry.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", ck3Exe, "DISABLEDXMAXIMIZEDWINDOWEDMODE")
                && !TcpGlobalSettingsNeedUpdate();

            if (!IsAdministrator())
                return userProfile;

            return userProfile
                && RegistryDwordEquals(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 10);
        }

        private void ApplyPowerAdapterProfile()
        {
            NetworkRouteProfile profile = AnalyzeNetworkRouteProfile(false);
            RecordSystemSnapshot("Power scheme before CK3MPS adapter profile", "powercfg /getactivescheme", RunCommandQuiet("powercfg.exe", "/getactivescheme"));
            RecordSystemSnapshot("PCI Express power settings before CK3MPS adapter profile", "powercfg /query SCHEME_CURRENT SUB_PCIEXPRESS ASPM", RunCommandQuiet("powercfg.exe", "/query SCHEME_CURRENT SUB_PCIEXPRESS ASPM"));
            bool changed = false;
            if (PciExpressAspmNeedsUpdate())
            {
                RunCommand("powercfg.exe", "/setacvalueindex SCHEME_CURRENT SUB_PCIEXPRESS ASPM 0", true);
                changed = true;
            }
            else
                Log("OK   PCI Express ASPM AC value already set to 0.");

            if (PowerIdleAdapterNeedsUpdate())
            {
                RunCommand("powercfg.exe", "/setacvalueindex SCHEME_CURRENT 19cbb8fa-5279-450e-9fac-8a3d5fedd0c1 12bbebe6-58d6-4636-95bb-3217ef867c1a 0", true);
                changed = true;
            }
            else
                Log("OK   Adapter idle power AC value already set to 0.");

            if (changed)
                RunCommand("powercfg.exe", "/setactive SCHEME_CURRENT", true);
            else
                Log("OK   Current power scheme already has the selected CK3MPS adapter values.");

            if (profile.HasPppoe)
                Log("INFO PPPoE adapter profile: power stability applied, provider MTU/offload settings left unchanged.");
            if (profile.HasMobile)
                Log("INFO Mobile/tethering profile: power stability applied; NAT/CGNAT cannot be fixed locally.");
            if (profile.HasWifi)
                Log("INFO Wi-Fi profile: keep charger connected and avoid power saver; Ethernet is preferred for hosting.");
            if (profile.HasMultipleGateways)
                Log("WARN Multiple network routes remain active. Disable unused adapters/VPNs during CK3 if OOS/disconnects continue.");
            if (profile.ProxyDetected)
                Log("WARN Windows proxy remains active. Disable it for testing if Paradox/Steam auth is unstable.");
            Log("INFO Adapter power-management note: Windows does not expose every NIC setting safely here.");
            Log("INFO If disconnects continue, open the active adapter and disable 'Allow the computer to turn off this device to save power'.");
        }

        private void CheckPowerAdapterProfileReadOnly()
        {
            string scheme = RunCommand("powercfg.exe", "/getactivescheme", true);
            Check("Windows power scheme detected", !String.IsNullOrEmpty(scheme));
            Check("At least one active network route exists", HasAnyActiveNetworkRoute());
            RunCommand("powercfg.exe", "/query SCHEME_CURRENT SUB_PCIEXPRESS ASPM", true);
            Log("INFO Check only does not change power or adapter settings.");
        }

        private bool PowerAdapterProfileOk()
        {
            string scheme = RunCommandQuiet("powercfg.exe", "/getactivescheme");
            return !String.IsNullOrEmpty(scheme) && HasAnyActiveNetworkRoute() && !PciExpressAspmNeedsUpdate() && !PowerIdleAdapterNeedsUpdate();
        }

        private bool TcpGlobalSettingsNeedUpdate()
        {
            string output = RunCommandQuiet("netsh.exe", "interface tcp show global");
            if (String.IsNullOrEmpty(output))
                return true;

            return output.IndexOf("Receive-Side Scaling State", StringComparison.OrdinalIgnoreCase) < 0
                || output.IndexOf("enabled", StringComparison.OrdinalIgnoreCase) < 0
                || output.IndexOf("Receive Window Auto-Tuning Level", StringComparison.OrdinalIgnoreCase) < 0
                || output.IndexOf("normal", StringComparison.OrdinalIgnoreCase) < 0
                || output.IndexOf("ECN Capability", StringComparison.OrdinalIgnoreCase) < 0
                || output.IndexOf("disabled", StringComparison.OrdinalIgnoreCase) < 0
                || output.IndexOf("RFC 1323 Timestamps", StringComparison.OrdinalIgnoreCase) < 0
                || CountCaseInsensitive(output, "disabled") < 2;
        }

        private int CountCaseInsensitive(string text, string needle)
        {
            if (String.IsNullOrEmpty(text) || String.IsNullOrEmpty(needle))
                return 0;

            int count = 0;
            int index = 0;
            while (true)
            {
                index = text.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    return count;
                count++;
                index += needle.Length;
            }
        }

        private bool PciExpressAspmNeedsUpdate()
        {
            string output = RunCommandQuiet("powercfg.exe", "/query SCHEME_CURRENT SUB_PCIEXPRESS ASPM");
            return String.IsNullOrEmpty(output) || output.IndexOf("0x00000000", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private bool PowerIdleAdapterNeedsUpdate()
        {
            string output = RunCommandQuiet("powercfg.exe", "/query SCHEME_CURRENT 19cbb8fa-5279-450e-9fac-8a3d5fedd0c1 12bbebe6-58d6-4636-95bb-3217ef867c1a");
            return String.IsNullOrEmpty(output) || output.IndexOf("0x00000000", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private bool HasAnyActiveNetworkRoute()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                if (ni.OperationalStatus == OperationalStatus.Up && AdapterHasIpv4Gateway(ni))
                    return true;
            return false;
        }

        private bool HasRiskyNetworkRouteProfile()
        {
            NetworkRouteProfile profile = AnalyzeNetworkRouteProfile(false);
            return profile.HasMultipleGateways
                || profile.HasWifi
                || profile.HasVpn
                || profile.HasPppoe
                || profile.HasMobile
                || profile.HasLowSpeed
                || profile.HasIpv6OnlyOrDsLiteSignal
                || profile.HasCgnatSignal
                || profile.ProxyDetected
                || profile.HasJitterOrLoss
                || profile.HasDnsFilteringSignal;
        }

        private void CheckLauncherRuntimeHygiene()
        {
            string path = StabilizerFile("ck3_stabilizer_runtime_hygiene.txt");
            string snapshot = BuildRuntimeHygieneSnapshotText();
            WriteTextFileIfMeaningfullyChanged(
                path,
                snapshot,
                "FILE Runtime hygiene snapshot written: ",
                "INFO Runtime hygiene snapshot already up to date: ",
                true);
            LogTextSnapshot("Runtime hygiene", snapshot);
        }

        private string BuildRuntimeHygieneSnapshotText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3MPS runtime hygiene");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine((!ProcessRunningContains("dowser") && !ProcessRunningContains("paradox launcher") ? "OK   " : "WARN ") + "Paradox Launcher is " + (!ProcessRunningContains("dowser") && !ProcessRunningContains("paradox launcher") ? "closed" : "running"));
            sb.AppendLine((!ProcessRunningExact("ck3") ? "OK   " : "WARN ") + "CK3 is " + (!ProcessRunningExact("ck3") ? "closed" : "running"));
            sb.AppendLine("INFO Steam may stay open, but Steam overlay should be disabled for CK3 if OOS continues.");
            sb.AppendLine("INFO Keep only one launcher path for CK3: Steam -> Paradox Launcher -> vanilla CK3.");
            return sb.ToString();
        }

        private bool ProcessRunningExact(string name)
        {
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (String.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { }
            }
            return false;
        }

        private bool ProcessRunningContains(string needle)
        {
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.ProcessName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                catch { }
            }
            return false;
        }

    }
}



