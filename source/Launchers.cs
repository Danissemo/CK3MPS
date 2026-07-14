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
        private void BackupSteamAndLauncherSettings()
        {
            BackupNamedFile(localConfig, Path.Combine(lastQuarantine, "steam_settings"));
            BackupNamedFile(sharedConfig, Path.Combine(lastQuarantine, "steam_settings"));
            BackupNamedFile(appManifest, Path.Combine(lastQuarantine, "steam_settings"));
            BackupNamedFile(Path.Combine(ck3Docs, "launcher-v2.sqlite"), Path.Combine(lastQuarantine, "paradox_launcher"));
            BackupNamedFile(Path.Combine(ck3Docs, "launcher-v2_backup.sqlite"), Path.Combine(lastQuarantine, "paradox_launcher"));
            BackupNamedFile(Path.Combine(ck3Docs, "dlc_load.json"), Path.Combine(lastQuarantine, "paradox_launcher"));
            BackupNamedFile(Path.Combine(ck3Docs, "pdx_settings.txt"), Path.Combine(lastQuarantine, "paradox_launcher"));

            string launcherRoaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paradox Interactive", "launcher-v2");
            BackupNamedFile(Path.Combine(launcherRoaming, "userSettings.json"), Path.Combine(lastQuarantine, "paradox_launcher"));
            Log("Steam and Paradox Launcher settings backed up.");
        }

        private void StabilizeSteamSettings()
        {
            EnsureNoAsync();
            RemoveDebugModeLaunchOption();
            DisableSteamCloudFlag();
            LogSteamOverlayHints();
        }

        private void RebuildParadoxLauncherDatabase()
        {
            string launcherDir = Path.Combine(lastQuarantine, "paradox_launcher");
            MoveToQuarantine(Path.Combine(ck3Docs, "launcher-v2.sqlite"), launcherDir);

            string localLauncher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paradox Interactive", "launcher-v2");
            string roamingLauncher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paradox Interactive", "launcher-v2");

            MoveToQuarantine(Path.Combine(localLauncher, "logs"), launcherDir);
            MoveToQuarantine(Path.Combine(roamingLauncher, "game-metadata"), launcherDir);
            MoveToQuarantine(Path.Combine(roamingLauncher, "telemetry-whitelist-cache"), launcherDir);

            Log("Paradox Launcher CK3 database/cache will rebuild on next launch.");
        }

        private void ForceNoMods()
        {
            string path = Path.Combine(ck3Docs, "dlc_load.json");
            ClearReadOnly(path);
            BackupFile(path);
            File.WriteAllText(path, "{\"enabled_mods\":[],\"disabled_dlcs\":[]}", Utf8NoBom);
            Log("No-mod/no-disabled-DLC dlc_load.json written.");
        }

        private void EnsureNoAsync()
        {
            if (String.IsNullOrEmpty(localConfig) || !File.Exists(localConfig))
            {
                Log("Steam localconfig.vdf not found. Set CK3 launch options manually: -noasync");
                return;
            }

            BackupFile(localConfig);
            string text = File.ReadAllText(localConfig, Encoding.UTF8);
            int appsIndex = text.IndexOf("\"apps\"", StringComparison.OrdinalIgnoreCase);
            int appIndex = appsIndex >= 0 ? text.IndexOf("\"1158310\"", appsIndex, StringComparison.OrdinalIgnoreCase) : -1;
            if (appIndex < 0)
            {
                Log("CK3 app block not found in Steam localconfig. Set launch options manually: -noasync");
                return;
            }

            int open = text.IndexOf('{', appIndex);
            int close = FindMatchingBrace(text, open);
            if (open < 0 || close < 0)
            {
                Log("Could not parse CK3 app block. Set launch options manually: -noasync");
                return;
            }

            string block = text.Substring(open + 1, close - open - 1);
            string normalized = NormalizeLaunchOptions(ExtractLaunchOptionsFromBlock(block), true);
            if (Regex.IsMatch(block, "\"LaunchOptions\"\\s+\"[^\"]*\"", RegexOptions.IgnoreCase))
                block = Regex.Replace(block, "\"LaunchOptions\"\\s+\"[^\"]*\"", "\"LaunchOptions\"\t\t\"" + EscapeVdfValue(normalized) + "\"", RegexOptions.IgnoreCase);
            else
                block = "\r\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"" + EscapeVdfValue(normalized) + "\"" + block;

            text = text.Substring(0, open + 1) + block + text.Substring(close);
            File.WriteAllText(localConfig, text, Encoding.UTF8);
            Log("Steam launch options normalized: " + normalized);
        }

        private void RemoveDebugModeLaunchOption()
        {
            if (String.IsNullOrEmpty(localConfig) || !File.Exists(localConfig))
            {
                Log("Steam localconfig.vdf not found. Could not check debug_mode.");
                return;
            }

            BackupFile(localConfig);
            string text = File.ReadAllText(localConfig, Encoding.UTF8);
            int appsIndex = text.IndexOf("\"apps\"", StringComparison.OrdinalIgnoreCase);
            int appIndex = appsIndex >= 0 ? text.IndexOf("\"1158310\"", appsIndex, StringComparison.OrdinalIgnoreCase) : -1;
            if (appIndex < 0)
            {
                Log("CK3 app block not found. Could not check debug_mode.");
                return;
            }

            int open = text.IndexOf('{', appIndex);
            int close = FindMatchingBrace(text, open);
            if (open < 0 || close < 0)
            {
                Log("Could not parse CK3 app block. Could not remove debug_mode.");
                return;
            }

            string block = text.Substring(open + 1, close - open - 1);
            Match m = Regex.Match(block, "\"LaunchOptions\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                Log("No LaunchOptions block found while checking debug_mode.");
                return;
            }

            string cleaned = NormalizeLaunchOptions(m.Groups[1].Value, true);

            block = Regex.Replace(block, "\"LaunchOptions\"\\s+\"[^\"]*\"", "\"LaunchOptions\"\t\t\"" + cleaned + "\"", RegexOptions.IgnoreCase);
            text = text.Substring(0, open + 1) + block + text.Substring(close);
            File.WriteAllText(localConfig, text, Encoding.UTF8);
            Log("Steam launch options checked: debug_mode removed, -noasync kept.");
        }

        private void DisableSteamCloudFlag()
        {
            if (String.IsNullOrEmpty(sharedConfig) || !File.Exists(sharedConfig))
            {
                Log("Steam sharedconfig.vdf not found. If Steam Cloud is enabled, disable it in CK3 Properties.");
                return;
            }

            BackupFile(sharedConfig);
            string text = File.ReadAllText(sharedConfig, Encoding.UTF8);
            int appIndex = text.IndexOf("\"1158310\"", StringComparison.OrdinalIgnoreCase);
            if (appIndex < 0)
            {
                Log("CK3 cloud block not found. If Steam Cloud is enabled, disable it in CK3 Properties.");
                return;
            }

            int open = text.IndexOf('{', appIndex);
            int close = FindMatchingBrace(text, open);
            if (open < 0 || close < 0)
            {
                Log("Could not parse cloud block. If Steam Cloud is enabled, disable it in CK3 Properties.");
                return;
            }

            string block = text.Substring(open + 1, close - open - 1);
            if (Regex.IsMatch(block, "\"cloudenabled\"\\s+\"[^\"]*\"", RegexOptions.IgnoreCase))
                block = Regex.Replace(block, "\"cloudenabled\"\\s+\"[^\"]*\"", "\"cloudenabled\"\t\t\"0\"", RegexOptions.IgnoreCase);
            else
                block = "\r\n\t\t\t\t\"cloudenabled\"\t\t\"0\"" + block;

            text = text.Substring(0, open + 1) + block + text.Substring(close);
            File.WriteAllText(sharedConfig, text, Encoding.UTF8);
            Log("Steam Cloud flag set to off for CK3 config.");
        }

        private string ExtractSteamLaunchOptions()
        {
            if (String.IsNullOrEmpty(localConfig) || !File.Exists(localConfig))
                return "";

            string text = File.ReadAllText(localConfig, Encoding.UTF8);
            int appsIndex = text.IndexOf("\"apps\"", StringComparison.OrdinalIgnoreCase);
            int appIndex = appsIndex >= 0 ? text.IndexOf("\"1158310\"", appsIndex, StringComparison.OrdinalIgnoreCase) : -1;
            if (appIndex < 0)
                return "";

            int open = text.IndexOf('{', appIndex);
            int close = FindMatchingBrace(text, open);
            if (open < 0 || close < 0)
                return "";

            return ExtractLaunchOptionsFromBlock(text.Substring(open + 1, close - open - 1));
        }

        private string ExtractLaunchOptionsFromBlock(string block)
        {
            Match m = Regex.Match(block, "\"LaunchOptions\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "";
        }

        private string NormalizeLaunchOptions(string options, bool requireNoAsync)
        {
            List<string> parts = new List<string>();
            bool hasNoAsync = false;
            foreach (string raw in Regex.Split(options ?? "", "\\s+"))
            {
                string token = raw.Trim();
                if (String.IsNullOrEmpty(token))
                    continue;
                if (String.Equals(token, "-noasync", StringComparison.OrdinalIgnoreCase))
                {
                    hasNoAsync = true;
                    continue;
                }
                if (IsRiskyLaunchOptionToken(token))
                    continue;
                parts.Add(token);
            }
            if (requireNoAsync || hasNoAsync)
                parts.Insert(0, "-noasync");
            return String.Join(" ", parts.ToArray()).Trim();
        }

        private bool IsRiskyLaunchOptionToken(string token)
        {
            string lower = (token ?? "").Trim().TrimStart('-').ToLowerInvariant();
            return lower == "debug_mode"
                || lower == "debug"
                || lower == "develop"
                || lower == "developer"
                || lower == "randomlog"
                || lower == "script_docs"
                || lower == "dx11"
                || lower == "d3d11"
                || lower == "directx11"
                || lower == "opengl"
                || lower == "force-d3d11";
        }

        private bool HasRiskyLaunchOptions()
        {
            foreach (string raw in Regex.Split(ExtractSteamLaunchOptions(), "\\s+"))
                if (IsRiskyLaunchOptionToken(raw))
                    return true;
            return false;
        }

        private string EscapeVdfValue(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private void LogSteamOverlayHints()
        {
            if (String.IsNullOrEmpty(localConfig) || !File.Exists(localConfig))
            {
                Log("Steam overlay settings not checked: localconfig.vdf not found.");
                return;
            }

            string text = File.ReadAllText(localConfig);
            int appIndex = text.IndexOf("\"1158310\"", StringComparison.OrdinalIgnoreCase);
            string block = "";
            if (appIndex >= 0)
            {
                int open = text.IndexOf('{', appIndex);
                int close = FindMatchingBrace(text, open);
                if (open >= 0 && close > open)
                    block = text.Substring(open, close - open + 1);
            }

            if (block.IndexOf("Overlay", StringComparison.OrdinalIgnoreCase) >= 0)
                Log("Steam overlay-related setting found in CK3 block. If OOS persists, disable Steam Overlay for CK3 in Steam UI.");
            else
                Log("Steam overlay setting not found in config. If OOS persists, disable Steam Overlay for CK3 manually.");

            Log("Steam Desktop Theatre/VR settings are global UI options; not changed automatically.");
        }

        private bool RemoveSteamLaunchOptionsOverride()
        {
            if (String.IsNullOrEmpty(localConfig) || !File.Exists(localConfig))
                return false;

            string text = File.ReadAllText(localConfig, Encoding.UTF8);
            int appsIndex = text.IndexOf("\"apps\"", StringComparison.OrdinalIgnoreCase);
            int appIndex = appsIndex >= 0 ? text.IndexOf("\"1158310\"", appsIndex, StringComparison.OrdinalIgnoreCase) : -1;
            if (appIndex < 0)
                return false;

            int open = text.IndexOf('{', appIndex);
            int close = FindMatchingBrace(text, open);
            if (open < 0 || close < 0)
                return false;

            string block = text.Substring(open + 1, close - open - 1);
            string updated = Regex.Replace(block, "\\r?\\n\\s*\"LaunchOptions\"\\s+\"[^\"]*\"", "", RegexOptions.IgnoreCase);
            if (updated == block)
                return false;

            BackupForRestore(localConfig, "Pre-default-restore backup of Steam localconfig launch options override: " + localConfig);
            text = text.Substring(0, open + 1) + updated + text.Substring(close);
            File.WriteAllText(localConfig, text, Encoding.UTF8);
            return true;
        }

        private bool RemoveSteamCloudOverride()
        {
            if (String.IsNullOrEmpty(sharedConfig) || !File.Exists(sharedConfig))
                return false;

            string text = File.ReadAllText(sharedConfig, Encoding.UTF8);
            int appIndex = text.IndexOf("\"1158310\"", StringComparison.OrdinalIgnoreCase);
            if (appIndex < 0)
                return false;

            int open = text.IndexOf('{', appIndex);
            int close = FindMatchingBrace(text, open);
            if (open < 0 || close < 0)
                return false;

            string block = text.Substring(open + 1, close - open - 1);
            string updated = Regex.Replace(block, "\\r?\\n\\s*\"cloudenabled\"\\s+\"[^\"]*\"", "", RegexOptions.IgnoreCase);
            if (updated == block)
                return false;

            BackupForRestore(sharedConfig, "Pre-default-restore backup of Steam sharedconfig cloud override: " + sharedConfig);
            text = text.Substring(0, open + 1) + updated + text.Substring(close);
            File.WriteAllText(sharedConfig, text, Encoding.UTF8);
            return true;
        }
    }
}

