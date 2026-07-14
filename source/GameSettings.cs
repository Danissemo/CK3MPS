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
        private void StabilizePdxSettings()
        {
            ApplyStablePdxSettings(true, "pdx_settings.txt stabilized.");
        }

        private void ApplyInGameMpSettings()
        {
            if (ProcessRunningExact("ck3") || ProcessRunningContains("dowser") || ProcessRunningContains("paradox launcher"))
                Log("WARN  CK3 or Paradox Launcher is running. These settings are safest when applied before launching the game.");
            ApplyStablePdxSettings(true, "In-game settings file rewritten with MP stability profile.");
            WriteSettingsGuardReport("after manual apply");
            Log("In-game settings applied: autosave off, cloud save off, save-on-exit off, Vulkan, fullscreen, VSync on, adaptive FPS off, 60 FPS cap, English, graphics profile=" + CurrentGraphicsProfile() + ".");
            Log("INFO These settings are read by CK3 on launch. Restart CK3 after applying; keep the stabilizer open during first launch if you want rollback guard protection.");
        }

        private void ApplyStablePdxSettings(bool backup, string successMessage)
        {
            string path = Path.Combine(ck3Docs, "pdx_settings.txt");
            ClearReadOnly(path);
            string text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
            if (backup)
                BackupFile(path);

            text = ApplyStablePdxSettingsToText(text);
            File.WriteAllText(path, text, Utf8NoBom);
            Log("OK   " + successMessage + " Graphics profile: " + CurrentGraphicsProfile() + ".");
        }

        private string ApplyStablePdxSettingsToText(string text)
        {
            if (String.IsNullOrWhiteSpace(text))
                text = "# CK3 settings rebuilt by CK3MPS\r\n";

            text = SetSectionSettingBlock(text, "game", "autosave", "version=0\r\n\t\tvalue=\"NO_AUTOSAVE\"");
            text = SetSectionSettingBlock(text, "game", "debug_saves", "version=0\r\n\t\tvalue=3");
            text = SetSectionSettingBlock(text, "game", "save_on_exit", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "game", "cloud_save", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "game", "rich_presence", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "game", "file_transfer_speed", "version=0\r\n\t\tvalue=\"OPTION_HIGH\"");

            text = SetSectionSettingBlock(text, "Graphics", "renderer", "version=0\r\n\t\tvalue=\"Vulkan\"");
            text = SetSectionSettingBlock(text, "Graphics", "display_mode", "version=0\r\n\t\tvalue=\"fullscreen\"");
            text = SetSectionSettingBlock(text, "Graphics", "vsync", "version=0\r\n\t\tenabled=yes");
            text = SetSectionSettingBlock(text, "Graphics", "adaptive_framerate", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "setting_framerate_cap", "version=0\r\n\t\tvalue=\"60\"");
            text = ApplyGraphicsProfileToText(text);

            text = SetSectionSettingBlock(text, "System", "language", "version=0\r\n\t\tvalue=\"l_english\"");
            text = SetSectionSettingBlock(text, "Audio", "audio_debug_log_level", "version=0\r\n\t\tvalue=\"error\"");
            return text;
        }

        private string CurrentGraphicsProfile()
        {
            string value = Convert.ToString(graphicsProfileBox.SelectedItem);
            return String.IsNullOrEmpty(value) ? "Stability Low" : value;
        }

        private string ApplyGraphicsProfileToText(string text)
        {
            string profile = CurrentGraphicsProfile();
            if (String.Equals(profile, "Keep current", StringComparison.OrdinalIgnoreCase))
                return text;

            if (String.Equals(profile, "Quality", StringComparison.OrdinalIgnoreCase))
            {
                text = SetSectionSettingBlock(text, "Graphics", "quality", "version=0\r\n\t\tvalue=\"high\"");
                text = SetSectionSettingBlock(text, "Graphics", "texture_quality", "version=1\r\n\t\tvalue=\"ultra\"");
                text = SetSectionSettingBlock(text, "Graphics", "shadowmap_resolution", "version=2\r\n\t\tvalue=\"4096x4096\"");
                text = SetSectionSettingBlock(text, "Graphics", "refraction_quality", "version=1\r\n\t\tvalue=\"high\"");
                text = SetSectionSettingBlock(text, "Graphics", "anti_aliasing", "version=0\r\n\t\tvalue=\"FXAA\"");
                return text;
            }

            if (String.Equals(profile, "Balanced", StringComparison.OrdinalIgnoreCase))
            {
                text = SetSectionSettingBlock(text, "Graphics", "quality", "version=0\r\n\t\tvalue=\"medium\"");
                text = SetSectionSettingBlock(text, "Graphics", "texture_quality", "version=1\r\n\t\tvalue=\"medium\"");
                text = SetSectionSettingBlock(text, "Graphics", "anisotropic_filtering", "version=0\r\n\t\tvalue=\"x4\"");
                text = SetSectionSettingBlock(text, "Graphics", "portrait_multi_sampling", "version=0\r\n\t\tvalue=\"x2\"");
                text = SetSectionSettingBlock(text, "Graphics", "terrain_smoothing", "version=0\r\n\t\tenabled=yes");
                text = SetSectionSettingBlock(text, "Graphics", "bloom_enabled", "version=0\r\n\t\tenabled=no");
                text = SetSectionSettingBlock(text, "Graphics", "ssao", "version=0\r\n\t\tenabled=no");
                text = SetSectionSettingBlock(text, "Graphics", "refraction_quality", "version=1\r\n\t\tvalue=\"medium\"");
                text = SetSectionSettingBlock(text, "Graphics", "shadowmap_resolution", "version=2\r\n\t\tvalue=\"2048x2048\"");
                text = SetSectionSettingBlock(text, "Graphics", "mesh_lod_bias", "version=1\r\n\t\tvalue=\"medium\"");
                text = SetSectionSettingBlock(text, "Graphics", "anti_aliasing", "version=0\r\n\t\tvalue=\"FXAA\"");
                return text;
            }

            text = SetSectionSettingBlock(text, "Graphics", "quality", "version=0\r\n\t\tvalue=\"low\"");
            text = SetSectionSettingBlock(text, "Graphics", "texture_quality", "version=1\r\n\t\tvalue=\"low\"");
            text = SetSectionSettingBlock(text, "Graphics", "anisotropic_filtering", "version=0\r\n\t\tvalue=\"x4\"");
            text = SetSectionSettingBlock(text, "Graphics", "portrait_multi_sampling", "version=0\r\n\t\tvalue=\"x2\"");
            text = SetSectionSettingBlock(text, "Graphics", "terrain_smoothing", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "bloom_enabled", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "ssao", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "depthoffield", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "lensflare", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "secondary_lensflare", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "refraction_quality", "version=1\r\n\t\tvalue=\"disabled\"");
            text = SetSectionSettingBlock(text, "Graphics", "shadowmap_resolution", "version=2\r\n\t\tvalue=\"disabled\"");
            text = SetSectionSettingBlock(text, "Graphics", "mesh_lod_bias", "version=1\r\n\t\tvalue=\"low\"");
            text = SetSectionSettingBlock(text, "Graphics", "mesh_lod_fade", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "mapobject_quality", "version=0\r\n\t\tvalue=\"off\"");
            text = SetSectionSettingBlock(text, "Graphics", "animated_portraits", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "court_scene_low_priority_characters", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "royal_court_anim_camera_idle", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "royal_court_anim_camera_transition", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "portraits_ssao", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "portraits_bloom", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "advanced_shaders", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "winter_particle_effects", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "cloud_shadow_enabled", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "tree_dithering_enabled", "version=0\r\n\t\tenabled=no");
            text = SetSectionSettingBlock(text, "Graphics", "anti_aliasing", "version=0\r\n\t\tvalue=\"DISABLED\"");
            return text;
        }

        private void StartSettingsGuard()
        {
            settingsGuardActive = true;
            WriteExpectedProfileSnapshot("guard started");
            WriteSettingsGuardReport("guard started");
            WriteRuntimeVerificationReport();
            settingsGuardTimer.Stop();
            settingsGuardTimer.Start();
            Log("GUARD Settings guard is active while this app stays open. If CK3/Launcher rewrites settings, mods, launch options, or unsafe save pointers, the stable profile will be restored.");
        }

        private void RunSettingsGuardTick()
        {
            if (!settingsGuardActive)
                return;

            try
            {
                bool stableSettings = StableCriticalSettingsOk();
                bool stableMods = DlcLoadProfileClean();
                bool stableLaunch = HasNoAsync() && !HasRiskyLaunchOptions();
                bool stableSaves = SaveLaunchHygieneOk();
                if (stableSettings && stableMods && stableLaunch && stableSaves)
                    return;

                if ((DateTime.UtcNow - lastSettingsGuardRepairUtc).TotalSeconds < 20)
                    return;

                lastSettingsGuardRepairUtc = DateTime.UtcNow;
                Log("GUARD CK3/Launcher rollback detected. Restoring stable multiplayer profile.");
                if (!stableSettings)
                {
                    if (ProcessRunningExact("ck3"))
                        Log("GUARD CK3 is running; pdx_settings.txt restore will wait until CK3 exits.");
                    else
                        ApplyStablePdxSettings(false, "Guard restored pdx_settings.txt after rollback.");
                }
                if (!stableMods)
                {
                    if (ProcessRunningExact("ck3") || ProcessRunningContains("dowser") || ProcessRunningContains("paradox launcher"))
                        Log("GUARD CK3/Launcher is running; dlc_load.json restore will wait until it exits.");
                    else
                        ForceNoMods();
                }
                if (!stableLaunch)
                    StabilizeSteamSettings();
                if (!stableSaves)
                {
                    if (ProcessRunningExact("ck3"))
                        Log("GUARD Unsafe save pointer/list detected while CK3 is running; save-file quarantine will wait until CK3 exits.");
                    else
                        StabilizeSaveHygiene();
                }
                WriteExpectedProfileSnapshot("rollback repaired");
                WriteSettingsGuardReport("rollback repaired");
            }
            catch (Exception ex)
            {
                Log("WARN  Settings guard failed: " + ex.Message);
            }
        }

        private void CheckSettingsGuardReadOnly()
        {
            string report = StabilizerFile("ck3_stabilizer_settings_guard.txt");
            Check("Settings guard report exists", File.Exists(report));
            Check("Settings guard target profile is currently stable", StableProfileSemanticallyOk());
            if (File.Exists(StabilizerFile("ck3_stabilizer_expected_profile_hashes.txt")))
            {
                if (ExpectedProfileSnapshotMatches())
                    Check("Expected profile hashes still match", true);
                else if (StableProfileSemanticallyOk())
                    Log("INFO Expected profile hashes drifted, but semantic core profile is stable. This can happen after CK3 rewrites pdx_settings.txt on launch.");
                else
                    Log("WARN Expected profile hashes drifted and semantic core profile is not stable.");
            }
            Log("INFO Keep CK3MPS open during the first CK3 launch after stabilization so it can detect launcher/game rollback.");
        }

        private void CheckRuntimeProfileReadOnly()
        {
            string debugLog = Path.Combine(ck3Docs, "logs", "debug.log");
            Log("INFO Runtime profile status: " + RuntimeProfileStatusText());
            Log("INFO Last renderer signal: " + LastRendererSignal());
            Log("INFO Last texture telemetry: " + LastLogLineContaining(debugLog, "[telemetry] texture_quality:"));
            Log("INFO Last shadow telemetry: " + LastLogLineContaining(debugLog, "[telemetry] shadowmap_resolution:"));

            if (!RuntimeLogExistsAfterSettings())
            {
                Log("INFO Last CK3 launch log is older than the current applied profile. Launch CK3 once after Stabilize, then run Check Only again for runtime confirmation.");
                return;
            }

            Check("Last CK3 launch happened after current profile was applied", true);
            Check("Last CK3 launch did not use DX11 after stabilization", !RuntimeLogShowsDx11AfterSettings());
            if (RuntimeLogShowsHighGraphicsAfterSettings())
                Log("WARN  Last CK3 launch reported high graphics telemetry. This is allowed when Quality graphics profile is selected and critical MP settings are stable.");
            else
                Log("INFO Last CK3 launch graphics telemetry is not high-profile.");
        }

        private bool RuntimeLogExistsAfterSettings()
        {
            string debugLog = Path.Combine(ck3Docs, "logs", "debug.log");
            string settings = Path.Combine(ck3Docs, "pdx_settings.txt");
            if (!File.Exists(debugLog) || !File.Exists(settings))
                return false;
            return File.GetLastWriteTimeUtc(debugLog) > File.GetLastWriteTimeUtc(settings).AddSeconds(2);
        }

        private bool RuntimeProfileLooksBadAfterSettings()
        {
            return RuntimeLogExistsAfterSettings() && RuntimeLogShowsDx11AfterSettings();
        }

        private bool RuntimeLogShowsDx11AfterSettings()
        {
            string debugLog = Path.Combine(ck3Docs, "logs", "debug.log");
            if (!RuntimeLogExistsAfterSettings() || !File.Exists(debugLog))
                return false;
            string text = ReadTextShared(debugLog);
            return text.IndexOf("gfx_dx11_master_context", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("directx 11", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("d3d11", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool RuntimeLogShowsHighGraphicsAfterSettings()
        {
            string debugLog = Path.Combine(ck3Docs, "logs", "debug.log");
            if (!RuntimeLogExistsAfterSettings() || !File.Exists(debugLog))
                return false;

            string texture = LastLogLineContaining(debugLog, "[telemetry] texture_quality:");
            string shadow = LastLogLineContaining(debugLog, "[telemetry] shadowmap_resolution:");
            return texture.IndexOf("texture_quality:high", StringComparison.OrdinalIgnoreCase) >= 0
                || texture.IndexOf("texture_quality:medium", StringComparison.OrdinalIgnoreCase) >= 0
                || shadow.IndexOf("2048", StringComparison.OrdinalIgnoreCase) >= 0
                || shadow.IndexOf("4096", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string RuntimeProfileStatusText()
        {
            if (!File.Exists(Path.Combine(ck3Docs, "logs", "debug.log")))
                return "not verified: debug.log is missing";
            if (!RuntimeLogExistsAfterSettings())
                return "not verified yet: last CK3 launch is older than the applied profile";
            if (RuntimeProfileLooksBadAfterSettings())
                return "failed: last CK3 launch did not match Vulkan profile";
            return "confirmed: no DX11 signal after the applied profile";
        }

        private string LastRendererSignal()
        {
            string debugLog = Path.Combine(ck3Docs, "logs", "debug.log");
            string dx11 = LastLogLineContaining(debugLog, "gfx_dx11_master_context");
            if (dx11 != "(not found)" && dx11 != "(missing)")
                return dx11;
            string vulkan = LastLogLineContaining(debugLog, "vulkan");
            if (vulkan != "(not found)" && vulkan != "(missing)")
                return vulkan;
            return LastLogLineContaining(debugLog, "gfx_");
        }

        private string FileWriteTimeText(string path)
        {
            if (!File.Exists(path))
                return "(missing)";
            return File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void WriteSettingsGuardReport(string reason)
        {
            try
            {
                string report = StabilizerFile("ck3_stabilizer_settings_guard.txt");
                string settings = Path.Combine(ck3Docs, "pdx_settings.txt");
                string dlc = Path.Combine(ck3Docs, "dlc_load.json");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("CK3 settings guard");
                sb.AppendLine("Stabilizer: " + AppVersion);
                sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Reason: " + reason);
                sb.AppendLine();
                sb.AppendLine("Current status");
                sb.AppendLine("- pdx_settings core stable: " + YesNo(StableCriticalSettingsOk()));
                sb.AppendLine("- pdx_settings full exact profile: " + YesNo(StableSettingsOk()));
                sb.AppendLine("- dlc_load no active mods: " + YesNo(NoActiveMods()));
                sb.AppendLine("- dlc_load no disabled DLCs: " + YesNo(NoDisabledDlcs()));
                sb.AppendLine("- Steam launch options stable: " + YesNo(HasNoAsync() && !HasRiskyLaunchOptions()));
                sb.AppendLine("- Steam launch options: " + NullText(ExtractSteamLaunchOptions()));
                sb.AppendLine("- Save launch hygiene stable: " + YesNo(SaveLaunchHygieneOk()));
                sb.AppendLine("- Active continue title: " + NullText(DetectActiveSaveTitle()));
                sb.AppendLine("- Suspicious save names: " + CountSuspiciousSaveNames());
                sb.AppendLine("- CK3 running: " + YesNo(ProcessRunningExact("ck3")));
                sb.AppendLine("- Paradox Launcher running: " + YesNo(ProcessRunningContains("dowser") || ProcessRunningContains("paradox launcher")));
                sb.AppendLine();
                sb.AppendLine("Tracked files");
                sb.AppendLine("- pdx_settings.txt: " + FileTimeHashLine(settings));
                sb.AppendLine("- dlc_load.json: " + FileTimeHashLine(dlc));
                sb.AppendLine();
                sb.AppendLine("If these settings change after game launch, leave this app open and the guard will restore them automatically.");
                File.WriteAllText(report, sb.ToString(), Encoding.UTF8);
                Log("FILE Settings guard report written: " + report);
            }
            catch (Exception ex)
            {
                Log("WARN  Settings guard report could not be written: " + ex.Message);
            }
        }

        private void WriteExpectedProfileSnapshot(string reason)
        {
            try
            {
                string path = StabilizerFile("ck3_stabilizer_expected_profile_hashes.txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("CK3 expected stable profile hashes");
                sb.AppendLine("Stabilizer: " + AppVersion);
                sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Reason: " + reason);
                sb.AppendLine();
                sb.AppendLine("pdx_settings.sha256=" + FileHashOrMissing(Path.Combine(ck3Docs, "pdx_settings.txt")));
                sb.AppendLine("dlc_load.sha256=" + FileHashOrMissing(Path.Combine(ck3Docs, "dlc_load.json")));
                sb.AppendLine("steam_localconfig.sha256=" + FileHashOrMissing(localConfig));
                sb.AppendLine("steam_sharedconfig.sha256=" + FileHashOrMissing(sharedConfig));
                sb.AppendLine("launch_options=" + ExtractSteamLaunchOptions());
                sb.AppendLine("local_parity_fingerprint=" + BuildLocalParityFingerprint());
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                Log("FILE Expected profile hash snapshot written: " + path);
            }
            catch (Exception ex)
            {
                Log("WARN Expected profile snapshot could not be written: " + ex.Message);
            }
        }

        private bool ExpectedProfileSnapshotMatches()
        {
            string path = StabilizerFile("ck3_stabilizer_expected_profile_hashes.txt");
            if (!File.Exists(path))
                return false;

            string text = File.ReadAllText(path, Encoding.UTF8);
            return SnapshotValue(text, "pdx_settings.sha256") == FileHashOrMissing(Path.Combine(ck3Docs, "pdx_settings.txt"))
                && SnapshotValue(text, "dlc_load.sha256") == FileHashOrMissing(Path.Combine(ck3Docs, "dlc_load.json"))
                && SnapshotValue(text, "steam_localconfig.sha256") == FileHashOrMissing(localConfig)
                && SnapshotValue(text, "steam_sharedconfig.sha256") == FileHashOrMissing(sharedConfig)
                && String.Equals(SnapshotValue(text, "launch_options"), ExtractSteamLaunchOptions(), StringComparison.Ordinal)
                && String.Equals(SnapshotValue(text, "local_parity_fingerprint"), BuildLocalParityFingerprint(), StringComparison.OrdinalIgnoreCase);
        }

        private bool ExpectedProfileOkForReadiness()
        {
            string path = StabilizerFile("ck3_stabilizer_expected_profile_hashes.txt");
            return !File.Exists(path) || ExpectedProfileSnapshotMatches() || StableProfileSemanticallyOk();
        }

        private bool StableProfileSemanticallyOk()
        {
            return StableCriticalSettingsOk()
                && DlcLoadProfileClean()
                && HasNoAsync()
                && !HasRiskyLaunchOptions()
                && SaveLaunchHygieneOk();
        }

        private string SnapshotValue(string text, string key)
        {
            Match m = Regex.Match(text ?? "", "(?im)^" + Regex.Escape(key) + "=(.*?)\\s*$");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private void WriteStableGameRuleProfile()
        {
            string profile = StabilizerFile("ck3_stabilizer_in_game_mp_settings.txt");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CK3 in-game MP stability profile");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Already applied to pdx_settings.txt:");
            sb.AppendLine("- Autosave: Off");
            sb.AppendLine("- Cloud saves: Off");
            sb.AppendLine("- Save on exit: Off");
            sb.AppendLine("- Renderer: Vulkan");
            sb.AppendLine("- Display mode: Fullscreen");
            sb.AppendLine("- VSync: On");
            sb.AppendLine("- Adaptive framerate: Off");
            sb.AppendLine("- FPS cap: 60");
            sb.AppendLine("- Language: English");
            sb.AppendLine();
            sb.AppendLine("Set these manually when creating a new serious MP campaign:");
            sb.AppendLine("- Multiplayer murder schemes: No Players");
            sb.AppendLine("- AI Landless Adventurers: 25 or lower");
            sb.AppendLine("- Great Steppe: Off");
            sb.AppendLine("- Natural disaster earthquakes: Disabled");
            sb.AppendLine("- Natural disaster floods: Disabled");
            sb.AppendLine("- Prefer landed rulers for stability testing");
            sb.AppendLine("- Avoid landless adventurer-heavy, Great Steppe, Japan/East Asia, Dynastic Cycle-heavy, and Iranian Intermezzo starts when testing stability");
            sb.AppendLine();
            sb.AppendLine("During the session:");
            sb.AppendLine("- Everyone joins before unpause");
            sb.AppendLine("- No hotjoin loops after OOS");
            sb.AppendLine("- Speed 1-2 for the first month after load");
            sb.AppendLine("- Do not change UI presets/outliner/game settings mid-session");
            sb.AppendLine("- Host makes local manual saves");
            File.WriteAllText(profile, sb.ToString(), Encoding.UTF8);
            Log("In-game MP settings profile written: " + profile);
        }
    }
}

