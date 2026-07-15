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
        private void ClearPlayerState()
        {
            MoveToQuarantine(Path.Combine(ck3Docs, "player"), Path.Combine(lastQuarantine, "user_state"));
        }

        private void CheckPlayerStateReadOnly()
        {
            string playerDir = Path.Combine(ck3Docs, "player");
            if (!Directory.Exists(playerDir))
            {
                Check("Player UI state absent or already cleared", true);
                return;
            }

            int files = CountItems(playerDir);
            Log("WARN  Player UI state folder exists. CK3 commonly recreates it after launch; this does not block OOS readiness.");
            Log("INFO  Player UI state items: " + files);
            Check("Player UI state is non-critical", PlayerStateNonCritical());
        }

        private bool PlayerStateNonCritical()
        {
            string playerDir = Path.Combine(ck3Docs, "player");
            if (!Directory.Exists(playerDir))
                return true;

            // CK3 recreates these UI preference files after launch. They are not simulation inputs,
            // so their presence should not block MP readiness after a successful cleanup.
            foreach (string file in Directory.GetFiles(playerDir, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file).ToLowerInvariant();
                if (name == "outliner.txt" || name == "character_interactions.txt")
                    continue;

                try
                {
                    FileInfo info = new FileInfo(file);
                    if (info.Length > 1024 * 1024)
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        private void ArchiveReports()
        {
            string reportDir = Path.Combine(lastQuarantine, "reports");
            MoveChildren(Path.Combine(ck3Docs, "oos"), reportDir);
            MoveChildren(Path.Combine(ck3Docs, "crashes"), reportDir);
            MoveChildren(Path.Combine(ck3Docs, "dumps"), reportDir);
            MoveChildren(Path.Combine(ck3Docs, "exceptions"), reportDir);
        }

        private void ClearCaches()
        {
            string cacheDir = Path.Combine(lastQuarantine, "cache");
            MoveToQuarantine(Path.Combine(ck3Docs, "shadercache"), cacheDir);
            MoveToQuarantine(Path.Combine(ck3Docs, ".launcher-cache"), cacheDir);

            string localLauncher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paradox Interactive", "launcher-v2", "chromium-data");
            MoveToQuarantine(Path.Combine(localLauncher, "Cache"), cacheDir);
            MoveToQuarantine(Path.Combine(localLauncher, "GPUCache"), cacheDir);
            MoveToQuarantine(Path.Combine(localLauncher, "DawnGraphiteCache"), cacheDir);
            MoveToQuarantine(Path.Combine(localLauncher, "DawnWebGPUCache"), cacheDir);

            string roamingCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paradox Interactive", "launcher-v2", "cache");
            MoveToQuarantine(roamingCache, cacheDir);
            WriteCacheCleanupMarker();
        }

        private void CleanCk3DocumentsFolder()
        {
            EnsureStabilizerRoot();
            MoveLegacyStabilizerArtifacts();
            string cleanupDir = Path.Combine(lastQuarantine, "ck3_folder_cleanup");
            Directory.CreateDirectory(cleanupDir);

            MoveToQuarantine(Path.Combine(ck3Docs, ".launcher-cache"), cleanupDir);
            MoveToQuarantine(Path.Combine(ck3Docs, "shadercache"), cleanupDir);
            MoveToQuarantine(Path.Combine(ck3Docs, "logs"), cleanupDir);
            MoveToQuarantine(Path.Combine(ck3Docs, "newsfeed"), cleanupDir);
            MoveToQuarantine(Path.Combine(ck3Docs, "oos"), cleanupDir);
            MoveToQuarantine(Path.Combine(ck3Docs, "crashes"), cleanupDir);
            MoveToQuarantine(Path.Combine(ck3Docs, "dumps"), cleanupDir);
            MoveToQuarantine(Path.Combine(ck3Docs, "exceptions"), cleanupDir);

            MoveCk3DirectoriesByPattern("desync_*", cleanupDir);
            MoveCk3DirectoriesByPattern("mp_stability_hardening_*", cleanupDir);
            MoveCk3DirectoriesByPattern("oos_archive_*", cleanupDir);
            MoveCk3DirectoriesByPattern("modded_saves_quarantine*", cleanupDir);
            MoveToQuarantine(Path.Combine(ck3Docs, "playsets_backup"), cleanupDir);

            foreach (string fileName in new[]
            {
                "game_data.json",
                "mods_registry.json",
                "content_load.json",
                "launcher-v2.sqlite",
                "launcher-v2_backup.sqlite"
            })
                MoveToQuarantine(Path.Combine(ck3Docs, fileName), cleanupDir);

            if (Directory.Exists(ck3Docs))
            {
                foreach (string file in Directory.GetFiles(ck3Docs, "ck3_stabilizer_*", SearchOption.TopDirectoryOnly))
                    MoveToQuarantine(file, cleanupDir);
                foreach (string dir in Directory.GetDirectories(ck3Docs, "ck3_stabilizer_*", SearchOption.TopDirectoryOnly))
                    MoveToQuarantine(dir, cleanupDir);
                foreach (string dir in Directory.GetDirectories(ck3Docs, "_ck3_stabilizer_quarantine_*", SearchOption.TopDirectoryOnly))
                    MoveToQuarantine(dir, cleanupDir);
            }

            ForceNoMods();
            StabilizePdxSettings();
            WriteFolderCleanupReport();
            Log("OK   CK3 Documents cleanup finished. Saves were not touched.");
        }

        private void MoveCk3DirectoriesByPattern(string pattern, string destDir)
        {
            if (!Directory.Exists(ck3Docs))
                return;
            foreach (string dir in Directory.GetDirectories(ck3Docs, pattern, SearchOption.TopDirectoryOnly))
                MoveToQuarantine(dir, destDir);
        }

        private void CheckCk3DocumentsCleanupReadOnly()
        {
            Check("CK3 save games folder preserved", Directory.Exists(Path.Combine(ck3Docs, "save games")));
            Check("No stabilizer files inside CK3 game folder", NoLegacyStabilizerArtifactsInCk3Docs());
            Check("Nonessential CK3 folder clutter removed or harmless", Ck3DocumentsCleanupOk());
            Log("INFO Cleanup keeps save games, pdx_settings.txt and dlc_load.json.");
        }

        private bool Ck3DocumentsCleanupOk()
        {
            if (!Directory.Exists(ck3Docs))
            {
                Log("WARN CK3 documents folder is missing.");
                return false;
            }
            if (!NoLegacyStabilizerArtifactsInCk3Docs())
            {
                Log("WARN Legacy stabilizer files are still inside the CK3 game documents folder.");
                return false;
            }
            if (Directory.GetDirectories(ck3Docs, "desync_*", SearchOption.TopDirectoryOnly).Length > 0)
            {
                Log("WARN Old desync folders are still inside the CK3 game documents folder.");
                return false;
            }
            if (Directory.GetDirectories(ck3Docs, "mp_stability_hardening_*", SearchOption.TopDirectoryOnly).Length > 0)
            {
                Log("WARN Old MP hardening folders are still inside the CK3 game documents folder.");
                return false;
            }
            if (Directory.GetDirectories(ck3Docs, "oos_archive_*", SearchOption.TopDirectoryOnly).Length > 0)
            {
                Log("WARN Old OOS archive folders are still inside the CK3 game documents folder.");
                return false;
            }
            if (Directory.GetDirectories(ck3Docs, "modded_saves_quarantine*", SearchOption.TopDirectoryOnly).Length > 0)
            {
                Log("WARN Old modded-save quarantine folders are still inside the CK3 game documents folder.");
                return false;
            }

            string playsetsBackup = Path.Combine(ck3Docs, "playsets_backup");
            if (Directory.Exists(playsetsBackup))
            {
                int playsetsBackupItems = CountItems(playsetsBackup);
                if (playsetsBackupItems > 0)
                {
                    Log("WARN playsets_backup still contains items: " + playsetsBackupItems);
                    return false;
                }

                // The launcher can recreate this empty folder. Empty residue is harmless; real files are not.
                Log("INFO Empty playsets_backup folder is harmless launcher residue.");
            }

            return true;
        }

        private bool NoLegacyStabilizerArtifactsInCk3Docs()
        {
            if (!Directory.Exists(ck3Docs))
                return false;
            return Directory.GetFiles(ck3Docs, "ck3_stabilizer_*", SearchOption.TopDirectoryOnly).Length == 0
                && Directory.GetDirectories(ck3Docs, "ck3_stabilizer_*", SearchOption.TopDirectoryOnly).Length == 0
                && Directory.GetDirectories(ck3Docs, "_ck3_stabilizer_quarantine_*", SearchOption.TopDirectoryOnly).Length == 0;
        }

        private void WriteFolderCleanupReport()
        {
            string path = StabilizerFile("ck3_stabilizer_folder_cleanup.txt");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 folder cleanup");
            sb.AppendLine("Stabilizer: " + AppVersion);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("CK3 folder: " + ck3Docs);
            sb.AppendLine("Save games kept: " + YesNo(Directory.Exists(Path.Combine(ck3Docs, "save games"))));
            sb.AppendLine("Quarantine: " + NullText(lastQuarantine));
            sb.AppendLine("Moved: launcher cache, shader cache, logs, OOS/crash folders, launcher databases and generated launcher metadata.");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Log("FILE Folder cleanup report written: " + path);
        }

        private void WriteCacheCleanupMarker()
        {
            try
            {
                string path = StabilizerFile("ck3_stabilizer_cache_cleanup.txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("CK3 cache cleanup");
                sb.AppendLine("Stabilizer: " + AppVersion);
                sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Quarantine: " + NullText(lastQuarantine));
                sb.AppendLine();
                sb.AppendLine("Cache folders may be regenerated by CK3 and Paradox Launcher after the next launch.");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                Log("FILE Cache cleanup marker written: " + path);
            }
            catch (Exception ex)
            {
                Log("WARN Cache cleanup marker could not be written: " + ex.Message);
            }
        }

        private void QuarantineModDescriptors()
        {
            string modDir = Path.Combine(ck3Docs, "mod");
            if (!Directory.Exists(modDir))
            {
                Log("No local mod descriptor folder.");
                return;
            }

            foreach (string file in Directory.GetFiles(modDir, "*.mod"))
                MoveToQuarantine(file, Path.Combine(lastQuarantine, "mods"));
        }

        private void QuarantineLoaderFiles()
        {
            if (String.IsNullOrEmpty(ck3Bin) || !Directory.Exists(ck3Bin))
            {
                Log("CK3 binaries folder not found, loader check skipped.");
                return;
            }

            int count = CountSuspectBinaries();
            if (count == 0)
            {
                Log("OK   No non-vanilla loader files found.");
                return;
            }

            Log("WARN  Non-vanilla loader files found: " + count + ".");
            Log("WARN  Stabilizer will not move Steam API/loader files automatically because partial removal can crash CK3.");
            Log("INFO  For strict vanilla MP, use Steam > CK3 > Properties > Installed Files > Verify integrity.");
            WriteBinaryInspectionReport();
        }

        private void CheckSuspectBinariesReadOnly()
        {
            if (String.IsNullOrEmpty(ck3Bin) || !Directory.Exists(ck3Bin))
            {
                Check("CK3 binaries folder detected", false);
                return;
            }

            int count = CountSuspectBinaries();
            if (count == 0)
            {
                Check("No non-vanilla loader files", true);
                return;
            }

            Log("WARN  Non-vanilla loader files detected: " + count + ".");
            Log("INFO  They are not moved automatically; use Steam file verification for a safe vanilla repair.");
            Check("No non-vanilla loader files", false);
            WriteBinaryInspectionReport();
        }

        private void WriteBinaryInspectionReport()
        {
            try
            {
                string report = StabilizerFile("ck3_stabilizer_binary_inspection.txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("CK3 binary inspection");
                sb.AppendLine("Stabilizer: " + AppVersion);
                sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();
                sb.AppendLine("CK3 binaries: " + NullText(ck3Bin));
                foreach (string name in suspectBinaryFiles)
                {
                    string path = Path.Combine(ck3Bin, name);
                    if (File.Exists(path))
                        sb.AppendLine("- found: " + name + " | " + FileHashOrMissing(path));
                }
                sb.AppendLine();
                sb.AppendLine("Safe repair: Steam > Crusader Kings III > Properties > Installed Files > Verify integrity.");
                File.WriteAllText(report, sb.ToString(), Encoding.UTF8);
                Log("FILE Binary inspection report written: " + report);
            }
            catch (Exception ex)
            {
                Log("WARN Binary inspection report could not be written: " + ex.Message);
            }
        }

    }
}



