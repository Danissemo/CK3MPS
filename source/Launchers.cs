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
            if (!EnsureSteamClosedForConfigMutation("Apply Settings"))
                return;

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
            string target = "{\"enabled_mods\":[],\"disabled_dlcs\":[]}";
            string current = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Trim() : "";
            if (File.Exists(path) && !HasUtf8Bom(path) && String.Equals(current, target, StringComparison.Ordinal))
            {
                Log("OK   dlc_load.json already matches the no-mod/no-disabled-DLC profile.");
                return;
            }

            BackupFile(path);
            SafeAtomicFile.WriteAllText(path, target, Utf8NoBom);
            Log("No-mod/no-disabled-DLC dlc_load.json written.");
        }

        private void EnsureNoAsync()
        {
            if (String.IsNullOrEmpty(localConfig) || !File.Exists(localConfig))
            {
                Log("Steam localconfig.vdf not found. Set CK3 launch options manually: -noasync");
                return;
            }
            ValveVdfUtilities.VdfObject root;
            ValveVdfUtilities.VdfObject appObject;
            string error;
            if (!TryLoadSteamLocalConfig(out root, out appObject, out error))
            {
                Log(error);
                return;
            }

            string existing = appObject.GetString("LaunchOptions");
            string normalized = NormalizeLaunchOptions(existing, true);
            if (String.Equals(existing, normalized, StringComparison.Ordinal))
            {
                Log("OK   Steam launch options already normalized: " + normalized);
                return;
            }

            BackupFile(localConfig);
            appObject.SetString("LaunchOptions", normalized);
            WriteSteamVdf(localConfig, root);
            Log("Steam launch options normalized: " + normalized);
        }

        private void RemoveDebugModeLaunchOption()
        {
            if (String.IsNullOrEmpty(localConfig) || !File.Exists(localConfig))
            {
                Log("Steam localconfig.vdf not found. Could not check debug_mode.");
                return;
            }

            ValveVdfUtilities.VdfObject root;
            ValveVdfUtilities.VdfObject appObject;
            string error;
            if (!TryLoadSteamLocalConfig(out root, out appObject, out error))
            {
                Log(error.Replace("Set launch options manually: -noasync", "Could not check debug_mode."));
                return;
            }

            string existing = appObject.GetString("LaunchOptions");
            if (String.IsNullOrEmpty(existing))
            {
                Log("No LaunchOptions block found while checking debug_mode.");
                return;
            }

            string cleaned = NormalizeLaunchOptions(existing, true);
            if (String.Equals(existing, cleaned, StringComparison.Ordinal))
            {
                Log("OK   Steam launch options already have debug_mode removed and -noasync kept.");
                return;
            }

            BackupFile(localConfig);
            appObject.SetString("LaunchOptions", cleaned);
            WriteSteamVdf(localConfig, root);
            Log("Steam launch options checked: debug_mode removed, -noasync kept.");
        }

        private void DisableSteamCloudFlag()
        {
            if (String.IsNullOrEmpty(sharedConfig) || !File.Exists(sharedConfig))
            {
                Log("Steam sharedconfig.vdf not found. If Steam Cloud is enabled, disable it in CK3 Properties.");
                return;
            }

            ValveVdfUtilities.VdfObject root;
            ValveVdfUtilities.VdfObject appObject;
            string error;
            if (!TryLoadSteamSharedConfig(out root, out appObject, out error))
            {
                Log(error);
                return;
            }

            if (String.Equals(appObject.GetString("cloudenabled"), "0", StringComparison.Ordinal))
            {
                Log("OK   Steam Cloud flag already set to off for CK3.");
                return;
            }

            BackupFile(sharedConfig);
            appObject.SetString("cloudenabled", "0");
            WriteSteamVdf(sharedConfig, root);
            Log("Steam Cloud flag set to off for CK3 config.");
        }

        private string ExtractSteamLaunchOptions()
        {
            ValveVdfUtilities.VdfObject root;
            ValveVdfUtilities.VdfObject appObject;
            string error;
            return TryLoadSteamLocalConfig(out root, out appObject, out error) ? appObject.GetString("LaunchOptions") : "";
        }

        private string NormalizeLaunchOptions(string options, bool requireNoAsync)
        {
            List<string> parts = new List<string>();
            bool hasNoAsync = false;
            foreach (string raw in WindowsCommandLineUtilities.Tokenize(options ?? ""))
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

            List<string> encoded = new List<string>();
            foreach (string part in parts)
                encoded.Add(WindowsCommandLineUtilities.QuoteArgument(part));
            return String.Join(" ", encoded.ToArray()).Trim();
        }

        private bool IsRiskyLaunchOptionToken(string token)
        {
            string lower = (token ?? "").Trim().TrimStart('-').ToLowerInvariant();
            int equals = lower.IndexOf('=');
            if (equals >= 0)
                lower = lower.Substring(0, equals);
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
            foreach (string raw in WindowsCommandLineUtilities.Tokenize(ExtractSteamLaunchOptions()))
                if (IsRiskyLaunchOptionToken(raw))
                    return true;
            return false;
        }

        private void LogSteamOverlayHints()
        {
            if (String.IsNullOrEmpty(localConfig) || !File.Exists(localConfig))
            {
                Log("Steam overlay settings not checked: localconfig.vdf not found.");
                return;
            }

            ValveVdfUtilities.VdfObject root;
            ValveVdfUtilities.VdfObject appObject;
            string error;
            if (!TryLoadSteamLocalConfig(out root, out appObject, out error))
            {
                Log("Steam overlay setting not found in config. If OOS persists, disable Steam Overlay for CK3 manually.");
                Log("Steam Desktop Theatre/VR settings are global UI options; not changed automatically.");
                return;
            }

            if (appObject.ContainsKeyFragment("Overlay"))
                Log("Steam overlay-related setting found in CK3 block. If OOS persists, disable Steam Overlay for CK3 in Steam UI.");
            else
                Log("Steam overlay setting not found in config. If OOS persists, disable Steam Overlay for CK3 manually.");

            Log("Steam Desktop Theatre/VR settings are global UI options; not changed automatically.");
        }

        private bool RemoveSteamLaunchOptionsOverride()
        {
            if (!EnsureSteamClosedForConfigMutation("Restore default", true))
                return false;

            if (String.IsNullOrEmpty(localConfig) || !File.Exists(localConfig))
                return false;

            ValveVdfUtilities.VdfObject root;
            ValveVdfUtilities.VdfObject appObject;
            string error;
            if (!TryLoadSteamLocalConfig(out root, out appObject, out error))
                return false;
            if (!appObject.RemoveAll("LaunchOptions"))
                return false;

            BackupForRestore(localConfig, "Pre-default-restore backup of Steam localconfig launch options override: " + localConfig);
            WriteSteamVdf(localConfig, root);
            return true;
        }

        private bool RemoveSteamCloudOverride()
        {
            if (!EnsureSteamClosedForConfigMutation("Restore default", true))
                return false;

            if (String.IsNullOrEmpty(sharedConfig) || !File.Exists(sharedConfig))
                return false;

            ValveVdfUtilities.VdfObject root;
            ValveVdfUtilities.VdfObject appObject;
            string error;
            if (!TryLoadSteamSharedConfig(out root, out appObject, out error))
                return false;
            if (!appObject.RemoveAll("cloudenabled"))
                return false;

            BackupForRestore(sharedConfig, "Pre-default-restore backup of Steam sharedconfig cloud override: " + sharedConfig);
            WriteSteamVdf(sharedConfig, root);
            return true;
        }

        private bool TryLoadSteamLocalConfig(out ValveVdfUtilities.VdfObject root, out ValveVdfUtilities.VdfObject appObject, out string error)
        {
            return TryLoadSteamAppConfig(localConfig, "Steam localconfig.vdf not found. Set launch options manually: -noasync", "CK3 app block not found in Steam localconfig. Set launch options manually: -noasync", new[] { "UserLocalConfigStore", "Software", "Valve", "Steam", "apps", "1158310" }, out root, out appObject, out error);
        }

        private bool TryLoadSteamSharedConfig(out ValveVdfUtilities.VdfObject root, out ValveVdfUtilities.VdfObject appObject, out string error)
        {
            return TryLoadSteamAppConfig(sharedConfig, "Steam sharedconfig.vdf not found. If Steam Cloud is enabled, disable it in CK3 Properties.", "CK3 cloud block not found. If Steam Cloud is enabled, disable it in CK3 Properties.", new[] { "UserRoamingConfigStore", "Software", "Valve", "Steam", "apps", "1158310" }, out root, out appObject, out error);
        }

        private bool TryLoadSteamAppConfig(string path, string missingMessage, string appMissingMessage, string[] appPath, out ValveVdfUtilities.VdfObject root, out ValveVdfUtilities.VdfObject appObject, out string error)
        {
            root = null;
            appObject = null;
            error = "";
            if (String.IsNullOrEmpty(path) || !File.Exists(path))
            {
                error = missingMessage;
                return false;
            }

            string text = File.ReadAllText(path, Encoding.UTF8);
            if (!ValveVdfUtilities.TryParse(text, out root, out error))
            {
                error = "Could not parse " + Path.GetFileName(path) + ". " + error;
                return false;
            }

            appObject = ValveVdfUtilities.FindPath(root, appPath);
            if (appObject == null)
            {
                error = appMissingMessage;
                return false;
            }

            return true;
        }

        private void WriteSteamVdf(string path, ValveVdfUtilities.VdfObject root)
        {
            string serialized = ValveVdfUtilities.Serialize(root);
            AtomicWriteResult result = SafeAtomicFile.TryWriteAllText(path, serialized, Encoding.UTF8, delegate (string tempPath)
            {
                ValveVdfUtilities.VdfObject tempRoot;
                string parseError;
                return ValveVdfUtilities.TryParse(File.ReadAllText(tempPath, Encoding.UTF8), out tempRoot, out parseError);
            });
            if (!result.Succeeded)
                throw new IOException(result.Message);
        }

        private bool EnsureSteamClosedForConfigMutation(string operation, bool throwWhenBlocked = false)
        {
            if (!ProcessRunningContains("steam"))
                return true;

            string message = "Steam is running. Close Steam completely before " + (operation ?? "changing Steam config") + " changes `localconfig.vdf` or `sharedconfig.vdf`.";
            Log("WARN  " + message);
            if (throwWhenBlocked)
                throw new InvalidOperationException(message);
            return false;
        }
    }
}

