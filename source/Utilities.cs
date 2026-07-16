using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace CK3MPS
{
    internal static class MutationAudit
    {
        private static readonly object Sync = new object();
        private static int readOnlyScopeDepth;
        private static readonly List<string> Attempts = new List<string>();

        public static void BeginReadOnlyScope()
        {
            lock (Sync)
            {
                if (readOnlyScopeDepth == 0)
                    Attempts.Clear();
                readOnlyScopeDepth++;
            }
        }

        public static string[] EndReadOnlyScope()
        {
            lock (Sync)
            {
                if (readOnlyScopeDepth > 0)
                    readOnlyScopeDepth--;
                string[] result = Attempts.ToArray();
                if (readOnlyScopeDepth == 0)
                    Attempts.Clear();
                return result;
            }
        }

        public static void RecordMutation(string kind, string target)
        {
            lock (Sync)
            {
                if (readOnlyScopeDepth <= 0)
                    return;
                string attempt = (kind ?? "mutation") + ":" + (target ?? "");
                Attempts.Add(attempt);
                throw new InvalidOperationException("Read-only Scan blocked a mutation attempt: " + attempt);
            }
        }
    }

    internal static class Ck3PathUtilities
    {
        public static string DefaultSettingsFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Paradox Interactive", "Crusader Kings III");
        }

        public static string NormalizeDirectoryPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                return "";
            return Path.GetFullPath(path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        public static string NormalizeGameFolderSelection(string selectedPath)
        {
            string path = NormalizeDirectoryPath(selectedPath);
            if (String.IsNullOrEmpty(path))
                return "";

            if (String.Equals(Path.GetFileName(path), "binaries", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(path, "ck3.exe"))
                && Directory.GetParent(path) != null)
                return Directory.GetParent(path).FullName;

            return path;
        }

        public static string NormalizeSettingsFolderSelection(string selectedPath)
        {
            string path = NormalizeDirectoryPath(selectedPath);
            if (String.IsNullOrEmpty(path))
                return "";

            if (String.Equals(Path.GetFileName(path), "save games", StringComparison.OrdinalIgnoreCase)
                && Directory.GetParent(path) != null)
                return Directory.GetParent(path).FullName;

            return path;
        }

        public static bool IsValidGameFolder(string path)
        {
            return !String.IsNullOrEmpty(path)
                && Directory.Exists(path)
                && File.Exists(Path.Combine(path, "binaries", "ck3.exe"));
        }

        public static bool IsValidSettingsFolder(string path)
        {
            if (String.IsNullOrEmpty(path) || !Directory.Exists(path))
                return false;

            return File.Exists(Path.Combine(path, "pdx_settings.txt"))
                || File.Exists(Path.Combine(path, "dlc_load.json"))
                || File.Exists(Path.Combine(path, "continue_game.json"))
                || File.Exists(Path.Combine(path, "launcher-v2.sqlite"))
                || Directory.Exists(Path.Combine(path, "save games"))
                || Directory.Exists(Path.Combine(path, "logs"));
        }

        public static string ValidationText(bool exists, bool valid)
        {
            if (!exists)
                return "not found";
            return valid ? "valid" : "wrong folder";
        }
    }

    internal static class VersionUtilities
    {
        public static int CompareReleaseTags(string leftTag, string rightTag)
        {
            int[] left = VersionParts(leftTag);
            int[] right = VersionParts(rightTag);
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] > right[i])
                    return 1;
                if (left[i] < right[i])
                    return -1;
            }

            return 0;
        }

        public static bool IsNewerRelease(string latestTag, string currentTag)
        {
            return CompareReleaseTags(latestTag, currentTag) > 0;
        }

        public static int[] VersionParts(string tag)
        {
            int[] parts = new[] { 0, 0, 0, 0 };
            MatchCollection matches = Regex.Matches(tag ?? "", "\\d+");
            for (int i = 0; i < parts.Length && i < matches.Count; i++)
            {
                int parsed;
                if (Int32.TryParse(matches[i].Value, out parsed))
                    parts[i] = parsed;
            }
            return parts;
        }
    }

    internal static class PresetUtilities
    {
        public static int[] RecommendedStepIndices()
        {
            return StepCatalog.RecommendedIndices();
        }

        public static bool ContainsStep(int[] steps, int index)
        {
            foreach (int step in steps)
                if (step == index)
                    return true;
            return false;
        }
    }

    internal static class RestoreManifestUtilities
    {
        internal enum RestorePathKind
        {
            Unknown = 0,
            ManagedWorkflowSave,
            Ck3ConfigFile,
            Ck3GeneratedDirectory,
            LauncherCache,
            SteamConfig,
            Ck3MpsCreatedFile,
            RegistryTarget
        }

        private static readonly string[] RestorableCk3Files =
        {
            "continue_game.json",
            "dlc_load.json",
            "pdx_settings.txt",
            "launcher-v2.sqlite"
        };

        private static readonly string[] RestorableCk3Directories =
        {
            "player",
            "shadercache",
            "logs",
            "newsfeed",
            "oos",
            "crashes",
            "dumps",
            "exceptions",
            "playsets_backup"
        };

        private static readonly string[] RestorableLauncherDirectories =
        {
            "Cache",
            "GPUCache",
            "DawnGraphiteCache",
            "DawnWebGPUCache",
            "logs",
            "game-metadata",
            "telemetry-whitelist-cache"
        };

        private static readonly string[] AllowedRegistrySubKeys =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR",
            @"System\GameConfigStore",
            @"Software\Microsoft\DirectX\UserGpuPreferences",
            @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"
        };

        public static string EscapeTsv(string value)
        {
            return (value ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }

        public static string InferRunIdFromCreated(string created)
        {
            DateTime parsed;
            if (DateTime.TryParse(created, out parsed))
                return parsed.ToString("yyyyMMdd_HHmmss");
            return "legacy";
        }

        public static string RunIdFromManifestParts(string[] parts, string created)
        {
            return parts != null && parts.Length > 9 && !String.IsNullOrEmpty(parts[9])
                ? parts[9]
                : InferRunIdFromCreated(created);
        }

        public static bool IsOwnedByCk3OrParadoxLauncher(string path, string ck3Docs, string localLauncher, string roamingLauncher)
        {
            RestorePathKind kind = GetRestorePathKind(path, ck3Docs, localLauncher, roamingLauncher);
            return kind == RestorePathKind.Ck3ConfigFile
                || kind == RestorePathKind.Ck3GeneratedDirectory
                || kind == RestorePathKind.LauncherCache;
        }

        public static RestorePathKind GetRestorePathKind(string path, string ck3Docs, string localLauncher, string roamingLauncher)
        {
            string normalizedPath;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(path, out normalizedPath))
                return RestorePathKind.Unknown;

            string normalizedDocs = Ck3PathUtilities.NormalizeDirectoryPath(ck3Docs);
            if (!String.IsNullOrEmpty(normalizedDocs))
            {
                if (String.Equals(normalizedPath, normalizedDocs, StringComparison.OrdinalIgnoreCase))
                    return RestorePathKind.Unknown;

                if (PathContainmentUtilities.IsWithinRoot(normalizedDocs, normalizedPath))
                {
                    string relative = normalizedPath.Substring(normalizedDocs.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string[] parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && String.Equals(parts[0], "save games", StringComparison.OrdinalIgnoreCase)
                        && normalizedPath.EndsWith(".ck3", StringComparison.OrdinalIgnoreCase))
                        return RestorePathKind.ManagedWorkflowSave;

                    if (parts.Length == 1)
                    {
                        foreach (string fileName in RestorableCk3Files)
                            if (String.Equals(parts[0], fileName, StringComparison.OrdinalIgnoreCase))
                                return RestorePathKind.Ck3ConfigFile;
                        foreach (string directoryName in RestorableCk3Directories)
                            if (String.Equals(parts[0], directoryName, StringComparison.OrdinalIgnoreCase) && Directory.Exists(normalizedPath))
                                return RestorePathKind.Ck3GeneratedDirectory;
                        if (parts[0].StartsWith("oos_archive_", StringComparison.OrdinalIgnoreCase) && Directory.Exists(normalizedPath))
                            return RestorePathKind.Ck3GeneratedDirectory;
                    }

                    return RestorePathKind.Unknown;
                }
            }

            string normalizedLocalLauncher = Ck3PathUtilities.NormalizeDirectoryPath(localLauncher);
            string normalizedRoamingLauncher = Ck3PathUtilities.NormalizeDirectoryPath(roamingLauncher);
            if (IsWithinNamedChild(normalizedLocalLauncher, normalizedPath, RestorableLauncherDirectories)
                || IsWithinNamedChild(normalizedRoamingLauncher, normalizedPath, RestorableLauncherDirectories))
                return RestorePathKind.LauncherCache;

            return RestorePathKind.Unknown;
        }

        public static bool IsDefaultRestorablePath(string path, string ck3Docs, string localLauncher, string roamingLauncher)
        {
            RestorePathKind kind = GetRestorePathKind(path, ck3Docs, localLauncher, roamingLauncher);
            return kind == RestorePathKind.Ck3ConfigFile
                || kind == RestorePathKind.Ck3GeneratedDirectory
                || kind == RestorePathKind.LauncherCache;
        }

        public static bool IsUserGameDataPath(string path, string ck3Docs)
        {
            if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(ck3Docs))
                return false;

            string normalizedPath = Ck3PathUtilities.NormalizeDirectoryPath(path);
            string normalizedDocs = Ck3PathUtilities.NormalizeDirectoryPath(ck3Docs);
            if (String.IsNullOrEmpty(normalizedPath) || String.IsNullOrEmpty(normalizedDocs))
                return false;
            if (!PathContainmentUtilities.IsWithinRoot(normalizedDocs, normalizedPath))
                return false;

            string relative = normalizedPath.Substring(normalizedDocs.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (relative.Length == 0)
                return false;

            string[] parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            if (String.Equals(parts[0], "save games", StringComparison.OrdinalIgnoreCase))
                return true;
            if (String.Equals(parts[0], "mod", StringComparison.OrdinalIgnoreCase) && normalizedPath.EndsWith(".mod", StringComparison.OrdinalIgnoreCase))
                return true;
            if (normalizedPath.EndsWith(".ck3", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public static bool IsAllowedRegistryRestoreTarget(string path)
        {
            string rootName;
            string subKey;
            string valueName;
            if (!TryParseRegistryRestorePath(path, out rootName, out subKey, out valueName))
                return false;

            if (String.Equals(rootName, "HKCU", StringComparison.OrdinalIgnoreCase))
            {
                if (String.Equals(subKey, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", StringComparison.OrdinalIgnoreCase))
                    return StringEqualsAny(valueName, "AppCaptureEnabled");

                if (String.Equals(subKey, @"System\GameConfigStore", StringComparison.OrdinalIgnoreCase))
                {
                    return StringEqualsAny(valueName,
                        "GameDVR_Enabled",
                        "GameDVR_FSEBehaviorMode",
                        "GameDVR_HonorUserFSEBehaviorMode",
                        "GameDVR_DXGIHonorFSEWindowsCompatible",
                        "GameDVR_EFSEFeatureFlags");
                }

                if (String.Equals(subKey, @"Software\Microsoft\DirectX\UserGpuPreferences", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(subKey, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", StringComparison.OrdinalIgnoreCase))
                    return LooksLikeCk3ExeValueName(valueName);
            }

            if (String.Equals(rootName, "HKLM", StringComparison.OrdinalIgnoreCase)
                && String.Equals(subKey, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", StringComparison.OrdinalIgnoreCase))
                return StringEqualsAny(valueName, "NetworkThrottlingIndex", "SystemResponsiveness");

            return false;
        }

        public static bool TryParseRegistryRestorePath(string path, out string rootName, out string subKey, out string valueName)
        {
            rootName = "";
            subKey = "";
            valueName = "";

            if (String.IsNullOrWhiteSpace(path))
                return false;

            int firstSlash = path.IndexOf('\\');
            if (firstSlash <= 0 || firstSlash >= path.Length - 1)
                return false;

            rootName = path.Substring(0, firstSlash);
            string remainder = path.Substring(firstSlash + 1);

            foreach (string allowedSubKey in AllowedRegistrySubKeys)
            {
                if (!remainder.StartsWith(allowedSubKey + "\\", StringComparison.OrdinalIgnoreCase))
                    continue;

                subKey = allowedSubKey;
                valueName = remainder.Substring(allowedSubKey.Length + 1);
                return !String.IsNullOrWhiteSpace(valueName);
            }

            int lastSlash = remainder.LastIndexOf('\\');
            if (lastSlash <= 0 || lastSlash >= remainder.Length - 1)
                return false;

            subKey = remainder.Substring(0, lastSlash);
            valueName = remainder.Substring(lastSlash + 1);
            return !String.IsNullOrWhiteSpace(rootName)
                && !String.IsNullOrWhiteSpace(subKey)
                && !String.IsNullOrWhiteSpace(valueName);
        }

        private static bool IsWithinNamedChild(string root, string path, IEnumerable<string> allowedNames)
        {
            if (String.IsNullOrEmpty(root) || !PathContainmentUtilities.IsWithinRoot(root, path))
                return false;

            string relative = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string[] parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            foreach (string allowedName in allowedNames)
                if (String.Equals(parts[0], allowedName, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }

        private static bool StringEqualsAny(string value, params string[] expected)
        {
            foreach (string item in expected)
                if (String.Equals(value, item, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool LooksLikeCk3ExeValueName(string valueName)
        {
            if (String.IsNullOrWhiteSpace(valueName))
                return false;

            string normalized;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(valueName, out normalized))
                return false;

            return normalized.EndsWith(Path.DirectorySeparatorChar + "ck3.exe", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(Path.AltDirectorySeparatorChar + "ck3.exe", StringComparison.OrdinalIgnoreCase);
        }

        public static string SerializeRegistryValue(object value, RegistryValueKind kind)
        {
            if (value == null)
                return "(missing)";

            if (kind == RegistryValueKind.DWord || kind == RegistryValueKind.QWord)
                return kind + ":" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            if (kind == RegistryValueKind.MultiString)
                return kind + ":" + JoinEscaped(value as string[] ?? new[] { Convert.ToString(value) ?? "" });
            if (kind == RegistryValueKind.Binary || kind == RegistryValueKind.None)
                return kind + ":hex:" + BytesToHex(value as byte[] ?? new byte[0]);

            return kind + ":" + EscapeRegistryText(Convert.ToString(value) ?? "");
        }

        public static object ParseSerializedRegistryValue(string value, RegistryValueKind kind)
        {
            if (kind == RegistryValueKind.DWord)
                return Int32.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            if (kind == RegistryValueKind.QWord)
                return Int64.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            if (kind == RegistryValueKind.MultiString)
                return SplitEscaped(value);
            if (kind == RegistryValueKind.Binary || kind == RegistryValueKind.None)
            {
                string payload = value ?? "";
                if (payload.StartsWith("hex:", StringComparison.OrdinalIgnoreCase))
                    payload = payload.Substring(4);
                return HexToBytes(payload);
            }

            return UnescapeRegistryText(value ?? "");
        }

        private static string EscapeRegistryText(string value)
        {
            return (value ?? "")
                .Replace("\\", "\\\\")
                .Replace(";", "\\;")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static string UnescapeRegistryText(string value)
        {
            if (String.IsNullOrEmpty(value))
                return "";

            StringBuilder sb = new StringBuilder(value.Length);
            bool escape = false;
            foreach (char ch in value)
            {
                if (escape)
                {
                    switch (ch)
                    {
                        case 'r': sb.Append('\r'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case ';': sb.Append(';'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append(ch); break;
                    }
                    escape = false;
                }
                else if (ch == '\\')
                {
                    escape = true;
                }
                else
                {
                    sb.Append(ch);
                }
            }

            if (escape)
                sb.Append('\\');
            return sb.ToString();
        }

        private static string JoinEscaped(string[] values)
        {
            if (values == null || values.Length == 0)
                return "";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    sb.Append(';');
                sb.Append(EscapeRegistryText(values[i] ?? ""));
            }
            return sb.ToString();
        }

        private static string[] SplitEscaped(string value)
        {
            if (String.IsNullOrEmpty(value))
                return new string[0];

            System.Collections.Generic.List<string> items = new System.Collections.Generic.List<string>();
            StringBuilder current = new StringBuilder();
            bool escape = false;
            foreach (char ch in value)
            {
                if (escape)
                {
                    switch (ch)
                    {
                        case 'r': current.Append('\r'); break;
                        case 'n': current.Append('\n'); break;
                        case 't': current.Append('\t'); break;
                        case ';': current.Append(';'); break;
                        case '\\': current.Append('\\'); break;
                        default: current.Append(ch); break;
                    }
                    escape = false;
                }
                else if (ch == '\\')
                {
                    escape = true;
                }
                else if (ch == ';')
                {
                    items.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(ch);
                }
            }

            if (escape)
                current.Append('\\');
            items.Add(current.ToString());
            return items.ToArray();
        }

        private static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "";

            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static byte[] HexToBytes(string value)
        {
            string text = value ?? "";
            if (text.Length == 0)
                return new byte[0];
            if ((text.Length % 2) != 0)
                throw new FormatException("Hex registry value length is invalid.");

            byte[] bytes = new byte[text.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
            return bytes;
        }
    }

    internal static class StabilizerFileNameUtilities
    {
        public static string CompactName(string name)
        {
            string normalized = name ?? "";
            if (String.Equals(normalized, "ck3_stabilizer_check_only_report.txt", StringComparison.OrdinalIgnoreCase))
                return "check.txt";
            if (String.Equals(normalized, "ck3_stabilizer_last_report.txt", StringComparison.OrdinalIgnoreCase))
                return "report.txt";
            if (String.Equals(normalized, "ck3_stabilizer_mp_parity_manifest.txt", StringComparison.OrdinalIgnoreCase))
                return "mp_parity.txt";
            if (String.Equals(normalized, "ck3_stabilizer_oos_protocol.txt", StringComparison.OrdinalIgnoreCase))
                return "protocol.txt";
            if (String.Equals(normalized, "ck3_stabilizer_expected_profile_hashes.txt", StringComparison.OrdinalIgnoreCase))
                return "state.txt";

            const string prefix = "ck3_stabilizer_";
            string compact = Path.GetFileName(normalized);
            if (compact.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                compact = compact.Substring(prefix.Length);
            return String.IsNullOrEmpty(compact) ? "report.txt" : compact;
        }
    }

    internal static class ChecksumUtilities
    {
        public static string ExtractExpectedSha256(string checksumText, string assetName)
        {
            string text = checksumText ?? "";
            string wanted = Path.GetFileName(assetName ?? "");

            if (!String.IsNullOrEmpty(wanted))
            {
                string escaped = Regex.Escape(wanted);
                Match named = Regex.Match(text, "([A-Fa-f0-9]{64})\\s+[* ]?" + escaped, RegexOptions.IgnoreCase);
                if (named.Success)
                    return named.Groups[1].Value.ToLowerInvariant();

                Match reversed = Regex.Match(text, escaped + "\\s+([A-Fa-f0-9]{64})", RegexOptions.IgnoreCase);
                if (reversed.Success)
                    return reversed.Groups[1].Value.ToLowerInvariant();
            }

            Match single = Regex.Match(text.Trim(), "^([A-Fa-f0-9]{64})\\s*$", RegexOptions.IgnoreCase);
            if (single.Success)
                return single.Groups[1].Value.ToLowerInvariant();

            return "";
        }
    }

    internal static class SaveRuleUtilities
    {
        public static string ExtractBraceBlock(string text, string key)
        {
            if (String.IsNullOrEmpty(text) || String.IsNullOrEmpty(key))
                return "";

            Match match = Regex.Match(text, "(?is)\\b" + Regex.Escape(key) + "\\s*=\\s*\\{");
            if (!match.Success)
                return "";

            int openIndex = match.Index + match.Length - 1;
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                if (text[i] == '{')
                    depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return text.Substring(openIndex + 1, i - openIndex - 1);
                }
            }

            return "";
        }

        public static string NormalizeRuleValue(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "";

            string normalized = value.Trim();
            normalized = normalized.Trim('"');
            normalized = Regex.Replace(normalized, "\\s+", " ");
            return normalized.Trim().ToLowerInvariant();
        }

        public static bool ValueLooksDisabled(string value)
        {
            string normalized = NormalizeRuleValue(value);
            return normalized == "off"
                || normalized == "disabled"
                || normalized == "disable"
                || normalized == "no"
                || normalized == "none"
                || normalized == "false"
                || normalized == "0";
        }

        public static bool ValueLooksNoPlayers(string value)
        {
            string normalized = NormalizeRuleValue(value).Replace(" ", "_");
            return normalized == "no_players"
                || normalized == "none"
                || normalized == "disabled"
                || normalized == "off";
        }

        public static int? TryParseIntValue(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return null;

            Match match = Regex.Match(value, "-?\\d+");
            int parsed;
            if (!match.Success || !Int32.TryParse(match.Value, out parsed))
                return null;
            return parsed;
        }
    }

    internal static class PathContainmentUtilities
    {
        public static bool TryNormalizeAbsolutePath(string path, out string normalized)
        {
            normalized = "";
            if (String.IsNullOrWhiteSpace(path))
                return false;

            string candidate = path.Trim();
            if (candidate.IndexOf('\0') >= 0 || candidate.IndexOf('*') >= 0 || candidate.IndexOf('?') >= 0)
                return false;
            if (candidate.StartsWith(@"\\.\", StringComparison.Ordinal) || candidate.StartsWith(@"\\?\", StringComparison.Ordinal))
                return false;
            if (!Path.IsPathRooted(candidate))
                return false;
            if (HasAlternateDataStream(candidate))
                return false;

            try
            {
                normalized = Path.GetFullPath(candidate);
                return !String.IsNullOrWhiteSpace(normalized);
            }
            catch
            {
                normalized = "";
                return false;
            }
        }

        public static bool IsWithinRoot(string rootPath, string targetPath)
        {
            string normalizedRoot;
            string normalizedTarget;
            if (!TryNormalizeAbsolutePath(rootPath, out normalizedRoot) || !TryNormalizeAbsolutePath(targetPath, out normalizedTarget))
                return false;

            normalizedRoot = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (String.Equals(normalizedRoot, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                return true;

            string prefix = normalizedRoot + Path.DirectorySeparatorChar;
            return normalizedTarget.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ContainsReparsePointInExistingSegments(string path)
        {
            string normalized;
            if (!TryNormalizeAbsolutePath(path, out normalized))
                return true;

            string current = Path.GetPathRoot(normalized);
            if (String.IsNullOrEmpty(current))
                return true;

            string remainder = normalized.Substring(current.Length);
            string[] parts = remainder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                current = Path.Combine(current, part);
                if (!Directory.Exists(current) && !File.Exists(current))
                    break;

                try
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        return true;
                }
                catch
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsManagedSaveFilePath(string saveRoot, string path)
        {
            string normalizedSaveRoot;
            string normalizedPath;
            if (!TryNormalizeAbsolutePath(saveRoot, out normalizedSaveRoot) || !TryNormalizeAbsolutePath(path, out normalizedPath))
                return false;
            if (!normalizedPath.EndsWith(".ck3", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!IsWithinRoot(normalizedSaveRoot, normalizedPath))
                return false;
            return !ContainsReparsePointInExistingSegments(normalizedPath);
        }

        private static bool HasAlternateDataStream(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                return false;

            int driveSeparator = path.IndexOf(':');
            if (driveSeparator < 0)
                return false;

            return path.IndexOf(':', driveSeparator + 1) >= 0;
        }
    }

    internal static class BoundedTraversalUtilities
    {
        internal sealed class TraversalSettings
        {
            public int MaxDirectories = 256;
            public int MaxFiles = 256;
            public int MaxDepth = 4;
            public int MaxElapsedMs = 3000;
            public bool SkipReparsePoints = true;
        }

        internal sealed class TraversalResult
        {
            public readonly List<string> Paths = new List<string>();
            public int DirectoriesVisited;
            public int FilesVisited;
            public bool HitDirectoryLimit;
            public bool HitFileLimit;
            public bool TimedOut;
        }

        public static List<string> EnumerateSteamUserDirectories(string userDataRoot, int maxUsers, int maxElapsedMs)
        {
            List<string> users = new List<string>();
            string root;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(userDataRoot, out root) || !Directory.Exists(root))
                return users;

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly))
                {
                    if (sw.ElapsedMilliseconds >= maxElapsedMs || users.Count >= maxUsers)
                        break;
                    if (ShouldSkipDirectory(dir, true))
                        continue;
                    if (IsNumericDirectoryName(Path.GetFileName(dir)))
                        users.Add(dir);
                }
            }
            catch
            {
            }

            return users;
        }

        public static List<string> EnumerateSteamUserAppDirectories(string userDataRoot, string appId, int maxUsers, int maxResults, int maxElapsedMs)
        {
            List<string> results = new List<string>();
            foreach (string userDir in EnumerateSteamUserDirectories(userDataRoot, maxUsers, maxElapsedMs))
            {
                if (results.Count >= maxResults)
                    break;

                string appDir = Path.Combine(userDir, appId ?? "");
                if (Directory.Exists(appDir) && !ShouldSkipDirectory(appDir, true))
                    results.Add(appDir);
            }
            return results;
        }

        public static TraversalResult EnumerateFilesBounded(string root, string searchPattern, TraversalSettings settings)
        {
            TraversalSettings effective = settings ?? new TraversalSettings();
            TraversalResult result = new TraversalResult();
            string normalizedRoot;
            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(root, out normalizedRoot) || !Directory.Exists(normalizedRoot))
                return result;

            Stopwatch sw = Stopwatch.StartNew();
            Stack<TraversalFrame> stack = new Stack<TraversalFrame>();
            stack.Push(new TraversalFrame(normalizedRoot, 0));

            while (stack.Count > 0)
            {
                if (sw.ElapsedMilliseconds >= effective.MaxElapsedMs)
                {
                    result.TimedOut = true;
                    break;
                }

                TraversalFrame frame = stack.Pop();
                if (frame.Depth > effective.MaxDepth)
                    continue;
                if (ShouldSkipDirectory(frame.Path, effective.SkipReparsePoints))
                    continue;
                if (++result.DirectoriesVisited > effective.MaxDirectories)
                {
                    result.HitDirectoryLimit = true;
                    break;
                }

                try
                {
                    foreach (string file in Directory.GetFiles(frame.Path, searchPattern ?? "*", SearchOption.TopDirectoryOnly))
                    {
                        if (sw.ElapsedMilliseconds >= effective.MaxElapsedMs)
                        {
                            result.TimedOut = true;
                            break;
                        }
                        result.Paths.Add(file);
                        result.FilesVisited++;
                        if (result.Paths.Count >= effective.MaxFiles)
                        {
                            result.HitFileLimit = true;
                            break;
                        }
                    }
                }
                catch
                {
                }

                if (result.TimedOut || result.HitFileLimit)
                    break;
                if (frame.Depth >= effective.MaxDepth)
                    continue;

                string[] children = new string[0];
                try
                {
                    children = Directory.GetDirectories(frame.Path, "*", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                }

                for (int i = children.Length - 1; i >= 0; i--)
                {
                    if (ShouldSkipDirectory(children[i], effective.SkipReparsePoints))
                        continue;
                    stack.Push(new TraversalFrame(children[i], frame.Depth + 1));
                }
            }

            return result;
        }

        private static bool IsNumericDirectoryName(string name)
        {
            if (String.IsNullOrWhiteSpace(name))
                return false;

            for (int i = 0; i < name.Length; i++)
                if (!Char.IsDigit(name[i]))
                    return false;
            return true;
        }

        private static bool ShouldSkipDirectory(string path, bool skipReparsePoints)
        {
            if (!skipReparsePoints)
                return false;

            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }

        private sealed class TraversalFrame
        {
            public readonly string Path;
            public readonly int Depth;

            public TraversalFrame(string path, int depth)
            {
                Path = path ?? "";
                Depth = depth;
            }
        }
    }

    internal enum AtomicWriteStatus
    {
        Success,
        Conflict,
        ValidationFailed,
        IoError
    }

    internal sealed class AtomicWriteResult
    {
        public AtomicWriteStatus Status;
        public string Message;

        public bool Succeeded
        {
            get { return Status == AtomicWriteStatus.Success; }
        }
    }

    internal static class SafeAtomicFile
    {
        private sealed class FileSnapshot
        {
            public bool Exists;
            public long Length;
            public DateTime LastWriteUtc;
            public FileAttributes Attributes;
            public string Sha256;
        }

        private const long MaxAppendFileBytes = 2L * 1024L * 1024L;
        private const int MaxAppendRotations = 3;
        private static readonly object PathLocksSync = new object();
        private static readonly Dictionary<string, object> PathLocks = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private static object GetPathLock(string path)
        {
            string key = Path.GetFullPath(path ?? "");
            lock (PathLocksSync)
            {
                object sync;
                if (!PathLocks.TryGetValue(key, out sync))
                {
                    sync = new object();
                    PathLocks[key] = sync;
                }
                return sync;
            }
        }

        private static FileSnapshot CaptureSnapshot(string path)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path))
                return new FileSnapshot { Exists = false, Length = 0, LastWriteUtc = DateTime.MinValue, Sha256 = "" };

            FileInfo info = new FileInfo(path);
            string hash;
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                hash = Convert.ToBase64String(sha.ComputeHash(stream));
            return new FileSnapshot
            {
                Exists = true,
                Length = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc,
                Attributes = info.Attributes,
                Sha256 = hash
            };
        }

        private static bool SnapshotMatches(string path, FileSnapshot expected)
        {
            FileSnapshot current = CaptureSnapshot(path);
            if (expected == null)
                return true;

            return current.Exists == expected.Exists
                && current.Length == expected.Length
                && current.LastWriteUtc == expected.LastWriteUtc
                && current.Attributes == expected.Attributes
                && String.Equals(current.Sha256, expected.Sha256, StringComparison.Ordinal);
        }

        public static void ReplaceFile(string tempPath, string targetPath)
        {
            MutationAudit.RecordMutation("file-replace", targetPath);
            if (File.Exists(targetPath))
                File.Replace(tempPath, targetPath, null, true);
            else
                File.Move(tempPath, targetPath);
        }

        public static void TryDeleteTempFile(string path)
        {
            try
            {
                if (!String.IsNullOrEmpty(path) && File.Exists(path))
                {
                    MutationAudit.RecordMutation("file-delete", path);
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        public static AtomicWriteResult TryWriteAllText(string path, string content, Encoding encoding, Func<string, bool> validateTemp)
        {
            MutationAudit.RecordMutation("file-write", path);
            FileSnapshot expected;
            try
            {
                expected = CaptureSnapshot(path);
            }
            catch (Exception ex)
            {
                return new AtomicWriteResult { Status = AtomicWriteStatus.IoError, Message = ex.Message };
            }
            object sync = GetPathLock(path);
            lock (sync)
            {
                return TryWriteAllTextCore(path, content, encoding, validateTemp, expected);
            }
        }

        public static AtomicWriteResult TryAppendText(string path, string content, Encoding encoding)
        {
            string target = path ?? "";
            MutationAudit.RecordMutation("file-append", target);
            object sync = GetPathLock(target);
            lock (sync)
            {
                try
                {
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        FileSnapshot expected = CaptureSnapshot(target);

                        Encoding effectiveEncoding = encoding ?? new UTF8Encoding(false);
                        string addition = content ?? "";
                        long additionBytes = effectiveEncoding.GetByteCount(addition);
                        if (expected.Exists && expected.Length + additionBytes > MaxAppendFileBytes)
                        {
                            if (!SnapshotMatches(target, expected))
                                continue;
                            RotateAppendFiles(target);
                            expected = CaptureSnapshot(target);
                        }

                        string existing = expected.Exists ? File.ReadAllText(target, effectiveEncoding) : "";
                        AtomicWriteResult result = TryWriteAllTextCore(target, existing + addition, effectiveEncoding, null, expected);
                        if (result.Status != AtomicWriteStatus.Conflict)
                            return result;
                    }
                    return new AtomicWriteResult { Status = AtomicWriteStatus.Conflict, Message = "Target file kept changing during append." };
                }
                catch (Exception ex)
                {
                    return new AtomicWriteResult { Status = AtomicWriteStatus.IoError, Message = ex.Message };
                }
            }
        }

        private static void RotateAppendFiles(string path)
        {
            MutationAudit.RecordMutation("file-rotate", path);
            for (int index = MaxAppendRotations; index >= 1; index--)
            {
                string source = index == 1 ? path : path + "." + (index - 1).ToString();
                string destination = path + "." + index.ToString();
                if (!File.Exists(source))
                    continue;
                if (File.Exists(destination))
                    File.Delete(destination);
                File.Move(source, destination);
            }
        }

        private static AtomicWriteResult TryWriteAllTextCore(string path, string content, Encoding encoding, Func<string, bool> validateTemp, FileSnapshot expectedSnapshot)
        {
            string target = path ?? "";
            string dir = Path.GetDirectoryName(target);
            if (String.IsNullOrEmpty(dir))
                return new AtomicWriteResult { Status = AtomicWriteStatus.IoError, Message = "Target directory is missing." };

            Directory.CreateDirectory(dir);
            string tempPath = Path.Combine(dir, Path.GetFileName(target) + ".tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (!SnapshotMatches(target, expectedSnapshot))
                    return new AtomicWriteResult { Status = AtomicWriteStatus.Conflict, Message = "Target file changed before the atomic write began." };

                using (FileStream stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, encoding ?? new UTF8Encoding(false)))
                {
                    writer.Write(content ?? "");
                    writer.Flush();
                    stream.Flush(true);
                }

                if (validateTemp != null && !validateTemp(tempPath))
                    return new AtomicWriteResult { Status = AtomicWriteStatus.ValidationFailed, Message = "Temporary file validation failed." };
                if (!SnapshotMatches(target, expectedSnapshot))
                    return new AtomicWriteResult { Status = AtomicWriteStatus.Conflict, Message = "Target file changed before replace." };

                ReplaceFile(tempPath, target);
                return new AtomicWriteResult { Status = AtomicWriteStatus.Success, Message = "" };
            }
            catch (Exception ex)
            {
                return new AtomicWriteResult { Status = AtomicWriteStatus.IoError, Message = ex.Message };
            }
            finally
            {
                TryDeleteTempFile(tempPath);
            }
        }

        public static void WriteAllText(string path, string content, Encoding encoding)
        {
            AtomicWriteResult result = TryWriteAllText(path, content, encoding, null);
            if (!result.Succeeded)
                throw new IOException(result.Message);
        }

        public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding encoding)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string line in lines ?? new string[0])
                sb.AppendLine(line ?? "");
            WriteAllText(path, sb.ToString(), encoding);
        }
    }

    internal static class IncidentHistoryJsonUtilities
    {
        public static string BuildJsonLine(string timestampUtc, string trigger, string incidentId, string stage, string recommendedPath, int continuationRiskScore, string confidence, bool hotjoinAllowed, string note)
        {
            return "{"
                + "\"schemaVersion\":1,"
                + "\"timestampUtc\":\"" + EscapeJson(timestampUtc) + "\","
                + "\"trigger\":\"" + EscapeJson(trigger) + "\","
                + "\"incidentId\":\"" + EscapeJson(incidentId) + "\","
                + "\"stage\":\"" + EscapeJson(stage) + "\","
                + "\"recommendedPath\":\"" + EscapeJson(recommendedPath) + "\","
                + "\"continuationRiskScore\":" + continuationRiskScore.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"confidence\":\"" + EscapeJson(confidence) + "\","
                + "\"hotjoin\":\"" + (hotjoinAllowed ? "allowed" : "blocked") + "\","
                + "\"note\":\"" + EscapeJson(note) + "\""
                + "}";
        }

        public static string[] ParseLine(string raw)
        {
            string line = raw ?? "";
            if (String.IsNullOrWhiteSpace(line))
                return null;

            if (line.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                return new[]
                {
                    ExtractJsonString(line, "timestampUtc"),
                    ExtractJsonString(line, "trigger"),
                    ExtractJsonString(line, "incidentId"),
                    ExtractJsonString(line, "stage"),
                    ExtractJsonString(line, "recommendedPath"),
                    ExtractJsonNumber(line, "continuationRiskScore"),
                    ExtractJsonString(line, "confidence"),
                    ExtractJsonString(line, "hotjoin"),
                    ExtractJsonString(line, "note")
                };
            }

            return line.Split('\t');
        }

        private static string EscapeJson(string value)
        {
            string text = value ?? "";
            StringBuilder sb = new StringBuilder(text.Length + 8);
            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        private static string ExtractJsonString(string json, string field)
        {
            Match match = Regex.Match(json ?? "", "\"" + Regex.Escape(field) + "\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"", RegexOptions.CultureInvariant);
            return match.Success ? UnescapeJson(match.Groups[1].Value) : "";
        }

        private static string ExtractJsonNumber(string json, string field)
        {
            Match match = Regex.Match(json ?? "", "\"" + Regex.Escape(field) + "\"\\s*:\\s*(-?\\d+)", RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : "0";
        }

        private static string UnescapeJson(string value)
        {
            string text = value ?? "";
            StringBuilder sb = new StringBuilder(text.Length);
            bool escape = false;
            foreach (char ch in text)
            {
                if (escape)
                {
                    switch (ch)
                    {
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(ch); break;
                    }
                    escape = false;
                }
                else if (ch == '\\')
                {
                    escape = true;
                }
                else
                {
                    sb.Append(ch);
                }
            }
            if (escape)
                sb.Append('\\');
            return sb.ToString();
        }
    }

    internal static class WindowsCommandLineUtilities
    {
        public static List<string> Tokenize(string commandLine)
        {
            List<string> args = new List<string>();
            string text = commandLine ?? "";
            int i = 0;

            while (i < text.Length)
            {
                while (i < text.Length && Char.IsWhiteSpace(text[i]))
                    i++;
                if (i >= text.Length)
                    break;

                StringBuilder current = new StringBuilder();
                bool inQuotes = false;
                while (i < text.Length)
                {
                    char ch = text[i];
                    if (ch == '\\')
                    {
                        int slashStart = i;
                        while (i < text.Length && text[i] == '\\')
                            i++;
                        int slashCount = i - slashStart;

                        if (i < text.Length && text[i] == '"')
                        {
                            current.Append('\\', slashCount / 2);
                            if ((slashCount % 2) == 0)
                            {
                                inQuotes = !inQuotes;
                                i++;
                            }
                            else
                            {
                                current.Append('"');
                                i++;
                            }
                            continue;
                        }

                        current.Append('\\', slashCount);
                        continue;
                    }

                    if (ch == '"')
                    {
                        inQuotes = !inQuotes;
                        i++;
                        continue;
                    }

                    if (!inQuotes && Char.IsWhiteSpace(ch))
                        break;

                    current.Append(ch);
                    i++;
                }

                args.Add(current.ToString());
            }

            return args;
        }

        public static string QuoteArgument(string arg)
        {
            string value = arg ?? "";
            if (value.Length == 0)
                return "\"\"";
            if (!NeedsQuoting(value))
                return value;

            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            int backslashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (ch == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                    backslashes = 0;
                    continue;
                }

                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }
                sb.Append(ch);
            }

            if (backslashes > 0)
                sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        private static bool NeedsQuoting(string value)
        {
            foreach (char ch in value ?? "")
                if (Char.IsWhiteSpace(ch) || ch == '"')
                    return true;
            return false;
        }
    }

    internal static class ValveVdfUtilities
    {
        internal sealed class VdfEntry
        {
            public string Key;
            public string StringValue;
            public VdfObject ObjectValue;

            public bool IsObject
            {
                get { return ObjectValue != null; }
            }
        }

        internal sealed class VdfObject
        {
            public readonly List<VdfEntry> Entries = new List<VdfEntry>();

            public VdfObject FindObject(string key)
            {
                foreach (VdfEntry entry in Entries)
                {
                    if (entry.IsObject && String.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                        return entry.ObjectValue;
                }
                return null;
            }

            public string GetString(string key)
            {
                foreach (VdfEntry entry in Entries)
                {
                    if (!entry.IsObject && String.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                        return entry.StringValue ?? "";
                }
                return "";
            }

            public VdfObject EnsureObject(string key)
            {
                VdfObject existing = FindObject(key);
                if (existing != null)
                    return existing;

                VdfObject created = new VdfObject();
                Entries.Add(new VdfEntry
                {
                    Key = key ?? "",
                    ObjectValue = created
                });
                return created;
            }

            public void SetString(string key, string value)
            {
                RemoveAll(key);
                Entries.Add(new VdfEntry
                {
                    Key = key ?? "",
                    StringValue = value ?? ""
                });
            }

            public bool RemoveAll(string key)
            {
                bool removed = false;
                for (int i = Entries.Count - 1; i >= 0; i--)
                {
                    if (String.Equals(Entries[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        Entries.RemoveAt(i);
                        removed = true;
                    }
                }
                return removed;
            }

            public bool ContainsKeyFragment(string fragment)
            {
                foreach (VdfEntry entry in Entries)
                {
                    if ((entry.Key ?? "").IndexOf(fragment ?? "", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                    if (!entry.IsObject)
                    {
                        if ((entry.StringValue ?? "").IndexOf(fragment ?? "", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                    else if (entry.ObjectValue != null && entry.ObjectValue.ContainsKeyFragment(fragment))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public static bool TryParse(string text, out VdfObject root, out string error)
        {
            VdfParser parser = new VdfParser(text ?? "");
            return parser.TryParse(out root, out error);
        }

        public static string Serialize(VdfObject root)
        {
            StringBuilder sb = new StringBuilder();
            SerializeObject(sb, root ?? new VdfObject(), 0, true);
            return sb.ToString();
        }

        public static VdfObject FindPath(VdfObject root, params string[] path)
        {
            VdfObject current = root;
            foreach (string part in path ?? new string[0])
            {
                if (current == null)
                    return null;
                current = current.FindObject(part);
            }
            return current;
        }

        public static VdfObject EnsurePath(VdfObject root, params string[] path)
        {
            VdfObject current = root ?? new VdfObject();
            foreach (string part in path ?? new string[0])
                current = current.EnsureObject(part);
            return current;
        }

        private static void SerializeObject(StringBuilder sb, VdfObject obj, int depth, bool topLevel)
        {
            foreach (VdfEntry entry in obj.Entries)
            {
                string indent = new string('\t', depth);
                sb.Append(indent);
                sb.Append('"').Append(Escape(entry.Key)).Append('"');
                if (entry.IsObject)
                {
                    sb.Append("\r\n");
                    sb.Append(indent).Append("{\r\n");
                    SerializeObject(sb, entry.ObjectValue ?? new VdfObject(), depth + 1, false);
                    sb.Append(indent).Append("}\r\n");
                }
                else
                {
                    sb.Append("\t\t\"").Append(Escape(entry.StringValue)).Append("\"\r\n");
                }
            }
        }

        private static string Escape(string value)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in value ?? "")
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        private sealed class VdfParser
        {
            private readonly string text;
            private int index;

            public VdfParser(string source)
            {
                text = source ?? "";
            }

            public bool TryParse(out VdfObject root, out string error)
            {
                root = new VdfObject();
                error = "";
                try
                {
                    while (true)
                    {
                        SkipTrivia();
                        if (index >= text.Length)
                            break;

                        string key = ReadToken();
                        if (key == null)
                        {
                            error = "Expected key at position " + index + ".";
                            return false;
                        }

                        SkipTrivia();
                        if (index < text.Length && text[index] == '{')
                        {
                            index++;
                            VdfObject child;
                            if (!TryParseObjectBody(out child, out error))
                                return false;
                            root.Entries.Add(new VdfEntry { Key = key, ObjectValue = child });
                        }
                        else
                        {
                            string value = ReadToken();
                            if (value == null)
                            {
                                error = "Expected value for key '" + key + "'.";
                                return false;
                            }
                            root.Entries.Add(new VdfEntry { Key = key, StringValue = value });
                        }
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            private bool TryParseObjectBody(out VdfObject obj, out string error)
            {
                obj = new VdfObject();
                error = "";
                while (true)
                {
                    SkipTrivia();
                    if (index >= text.Length)
                    {
                        error = "Unexpected end of file while parsing object body.";
                        return false;
                    }

                    if (text[index] == '}')
                    {
                        index++;
                        return true;
                    }

                    string key = ReadToken();
                    if (key == null)
                    {
                        error = "Expected object key at position " + index + ".";
                        return false;
                    }

                    SkipTrivia();
                    if (index < text.Length && text[index] == '{')
                    {
                        index++;
                        VdfObject child;
                        if (!TryParseObjectBody(out child, out error))
                            return false;
                        obj.Entries.Add(new VdfEntry { Key = key, ObjectValue = child });
                    }
                    else
                    {
                        string value = ReadToken();
                        if (value == null)
                        {
                            error = "Expected value for key '" + key + "'.";
                            return false;
                        }
                        obj.Entries.Add(new VdfEntry { Key = key, StringValue = value });
                    }
                }
            }

            private void SkipTrivia()
            {
                while (index < text.Length)
                {
                    char ch = text[index];
                    if (Char.IsWhiteSpace(ch))
                    {
                        index++;
                        continue;
                    }
                    if (ch == '/' && index + 1 < text.Length && text[index + 1] == '/')
                    {
                        index += 2;
                        while (index < text.Length && text[index] != '\r' && text[index] != '\n')
                            index++;
                        continue;
                    }
                    break;
                }
            }

            private string ReadToken()
            {
                SkipTrivia();
                if (index >= text.Length)
                    return null;

                if (text[index] == '"')
                    return ReadQuotedToken();

                int start = index;
                while (index < text.Length)
                {
                    char ch = text[index];
                    if (Char.IsWhiteSpace(ch) || ch == '{' || ch == '}')
                        break;
                    index++;
                }
                return index > start ? text.Substring(start, index - start) : null;
            }

            private string ReadQuotedToken()
            {
                if (text[index] != '"')
                    return null;

                index++;
                StringBuilder sb = new StringBuilder();
                bool escape = false;
                while (index < text.Length)
                {
                    char ch = text[index++];
                    if (escape)
                    {
                        switch (ch)
                        {
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            case 'r': sb.Append('\r'); break;
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            default: sb.Append(ch); break;
                        }
                        escape = false;
                        continue;
                    }

                    if (ch == '\\')
                    {
                        escape = true;
                        continue;
                    }
                    if (ch == '"')
                        return sb.ToString();
                    sb.Append(ch);
                }

                throw new FormatException("Unterminated quoted VDF token.");
            }
        }
    }

    internal static class PdxSettingsUtilities
    {
        internal sealed class PdxEntry
        {
            public string Key;
            public string ScalarValue;
            public PdxObject ObjectValue;

            public bool IsObject
            {
                get { return ObjectValue != null; }
            }
        }

        internal sealed class PdxObject
        {
            public readonly List<PdxEntry> Entries = new List<PdxEntry>();

            public PdxObject FindObject(string key)
            {
                foreach (PdxEntry entry in Entries)
                {
                    if (entry.IsObject && String.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                        return entry.ObjectValue;
                }
                return null;
            }

            public string GetScalar(string key)
            {
                foreach (PdxEntry entry in Entries)
                {
                    if (!entry.IsObject && String.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                        return entry.ScalarValue ?? "";
                }
                return "";
            }

            public PdxObject EnsureObject(string key)
            {
                PdxObject existing = FindObject(key);
                if (existing != null)
                    return existing;

                PdxObject created = new PdxObject();
                Entries.Add(new PdxEntry
                {
                    Key = key ?? "",
                    ObjectValue = created
                });
                return created;
            }

            public void SetScalar(string key, string value)
            {
                RemoveAll(key);
                Entries.Add(new PdxEntry
                {
                    Key = key ?? "",
                    ScalarValue = value ?? ""
                });
            }

            public void SetObject(string key, PdxObject value)
            {
                RemoveAll(key);
                Entries.Add(new PdxEntry
                {
                    Key = key ?? "",
                    ObjectValue = value ?? new PdxObject()
                });
            }

            public bool RemoveAll(string key)
            {
                bool removed = false;
                for (int i = Entries.Count - 1; i >= 0; i--)
                {
                    if (String.Equals(Entries[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        Entries.RemoveAt(i);
                        removed = true;
                    }
                }
                return removed;
            }
        }

        public static bool TryParse(string text, out PdxObject root, out string error)
        {
            PdxParser parser = new PdxParser(text ?? "");
            return parser.TryParse(out root, out error);
        }

        public static string Serialize(PdxObject root)
        {
            StringBuilder sb = new StringBuilder();
            SerializeObject(sb, root ?? new PdxObject(), 0, true);
            return sb.ToString();
        }

        public static PdxObject FindPath(PdxObject root, params string[] path)
        {
            PdxObject current = root;
            foreach (string part in path ?? new string[0])
            {
                if (current == null)
                    return null;
                current = current.FindObject(part);
            }
            return current;
        }

        public static PdxObject EnsurePath(PdxObject root, params string[] path)
        {
            PdxObject current = root ?? new PdxObject();
            foreach (string part in path ?? new string[0])
                current = current.EnsureObject(part);
            return current;
        }

        public static bool SectionSettingMatches(string text, string section, string key, Dictionary<string, string> expectedFields)
        {
            PdxObject root;
            string error;
            if (!TryParse(text, out root, out error))
                return false;

            PdxObject sectionObject = root.FindObject(section);
            if (sectionObject == null)
                return false;

            PdxObject settingObject = sectionObject.FindObject(key);
            if (settingObject == null)
                return false;

            foreach (KeyValuePair<string, string> pair in expectedFields ?? new Dictionary<string, string>())
            {
                string actual = settingObject.GetScalar(pair.Key);
                if (!String.Equals(actual, pair.Value ?? "", StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        public static string SetSectionSettingBlock(string text, string section, string key, Dictionary<string, string> fields)
        {
            PdxObject root;
            string error;
            if (!TryParse(text, out root, out error))
            {
                root = new PdxObject();
                if (!String.IsNullOrWhiteSpace(text))
                {
                    PdxObject fallbackRoot;
                    string fallbackError;
                    if (!TryParse("#\r\n" + text, out fallbackRoot, out fallbackError))
                        return text;
                    root = fallbackRoot;
                }
            }

            PdxObject sectionObject = root.EnsureObject(section);
            PdxObject settingObject = new PdxObject();
            foreach (KeyValuePair<string, string> pair in fields ?? new Dictionary<string, string>())
                settingObject.SetScalar(pair.Key, pair.Value);
            sectionObject.SetObject(key, settingObject);
            return Serialize(root);
        }

        public static string ExtractSectionBody(string text, string section)
        {
            PdxObject root;
            string error;
            if (!TryParse(text, out root, out error))
                return "";

            PdxObject sectionObject = root.FindObject(section);
            if (sectionObject == null)
                return "";

            StringBuilder sb = new StringBuilder();
            foreach (PdxEntry entry in sectionObject.Entries)
            {
                if (entry.IsObject)
                {
                    sb.Append('"').Append(entry.Key).Append("\"={");
                    sb.Append(Serialize(entry.ObjectValue).Trim());
                    sb.Append("}\r\n");
                }
                else
                {
                    sb.Append(entry.Key).Append('=').Append(entry.ScalarValue).Append("\r\n");
                }
            }
            return sb.ToString();
        }

        public static Dictionary<string, string> ParseFieldMap(string body)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            PdxObject tempRoot;
            string error;
            if (!TryParse("{\r\n" + (body ?? "") + "\r\n}", out tempRoot, out error))
                return fields;

            foreach (PdxEntry entry in tempRoot.Entries)
            {
                if (!entry.IsObject)
                    fields[entry.Key ?? ""] = entry.ScalarValue ?? "";
            }

            return fields;
        }

        public static Dictionary<string, string> ParseFieldMapFromPattern(string pattern)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string text = (pattern ?? "").Replace("\\s*", " ").Trim();
            foreach (Match match in Regex.Matches(text, "([A-Za-z0-9_]+)=((?:\"[^\"]*\")|(?:[^\\s]+))"))
            {
                string key = match.Groups[1].Value;
                string value = match.Groups[2].Value.Trim();
                if (value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal) && value.Length >= 2)
                    value = value.Substring(1, value.Length - 2);
                fields[key] = value;
            }
            return fields;
        }

        private static void SerializeObject(StringBuilder sb, PdxObject obj, int depth, bool topLevel)
        {
            foreach (PdxEntry entry in obj.Entries)
            {
                string indent = new string('\t', depth);
                sb.Append(indent).Append('"').Append(Escape(entry.Key)).Append('"').Append('=');
                if (entry.IsObject)
                {
                    sb.Append("{\r\n");
                    SerializeObject(sb, entry.ObjectValue ?? new PdxObject(), depth + 1, false);
                    sb.Append(indent).Append("}\r\n");
                }
                else
                {
                    string value = entry.ScalarValue ?? "";
                    if (NeedsQuotedScalar(value))
                        sb.Append('"').Append(Escape(value)).Append('"');
                    else
                        sb.Append(value);
                    sb.Append("\r\n");
                }
            }
        }

        private static bool NeedsQuotedScalar(string value)
        {
            string text = value ?? "";
            if (text.Length == 0)
                return true;
            foreach (char ch in text)
            {
                if (Char.IsWhiteSpace(ch) || ch == '"' || ch == '{' || ch == '}' || ch == '=' || ch == '#')
                    return true;
            }
            return false;
        }

        private static string Escape(string value)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in value ?? "")
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        private sealed class PdxParser
        {
            private readonly string text;
            private int index;

            public PdxParser(string source)
            {
                text = source ?? "";
            }

            public bool TryParse(out PdxObject root, out string error)
            {
                root = new PdxObject();
                error = "";
                try
                {
                    while (true)
                    {
                        SkipTrivia();
                        if (index >= text.Length)
                            break;

                        if (text[index] == '{')
                        {
                            index++;
                            PdxObject body;
                            if (!TryParseObjectBody(out body, out error, true))
                                return false;
                            root = body;
                            SkipTrivia();
                            if (index < text.Length)
                            {
                                error = "Unexpected content after root object.";
                                return false;
                            }
                            return true;
                        }

                        string key = ReadToken();
                        if (key == null)
                        {
                            error = "Expected key at position " + index + ".";
                            return false;
                        }

                        SkipTrivia();
                        if (index < text.Length && text[index] == '=')
                            index++;
                        SkipTrivia();

                        if (index < text.Length && text[index] == '{')
                        {
                            index++;
                            PdxObject child;
                            if (!TryParseObjectBody(out child, out error, false))
                                return false;
                            root.Entries.Add(new PdxEntry { Key = key, ObjectValue = child });
                        }
                        else
                        {
                            string value = ReadToken();
                            if (value == null)
                            {
                                error = "Expected value for key '" + key + "'.";
                                return false;
                            }
                            root.Entries.Add(new PdxEntry { Key = key, ScalarValue = value });
                        }
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            private bool TryParseObjectBody(out PdxObject obj, out string error, bool syntheticRoot)
            {
                obj = new PdxObject();
                error = "";
                while (true)
                {
                    SkipTrivia();
                    if (index >= text.Length)
                    {
                        if (syntheticRoot)
                            return true;
                        error = "Unexpected end of file while parsing object body.";
                        return false;
                    }

                    if (text[index] == '}')
                    {
                        index++;
                        return true;
                    }

                    string key = ReadToken();
                    if (key == null)
                    {
                        error = "Expected object key at position " + index + ".";
                        return false;
                    }

                    SkipTrivia();
                    if (index < text.Length && text[index] == '=')
                        index++;
                    SkipTrivia();

                    if (index < text.Length && text[index] == '{')
                    {
                        index++;
                        PdxObject child;
                        if (!TryParseObjectBody(out child, out error, false))
                            return false;
                        obj.Entries.Add(new PdxEntry { Key = key, ObjectValue = child });
                    }
                    else
                    {
                        string value = ReadToken();
                        if (value == null)
                        {
                            error = "Expected value for key '" + key + "'.";
                            return false;
                        }
                        obj.Entries.Add(new PdxEntry { Key = key, ScalarValue = value });
                    }
                }
            }

            private void SkipTrivia()
            {
                while (index < text.Length)
                {
                    char ch = text[index];
                    if (Char.IsWhiteSpace(ch))
                    {
                        index++;
                        continue;
                    }
                    if (ch == '#')
                    {
                        while (index < text.Length && text[index] != '\r' && text[index] != '\n')
                            index++;
                        continue;
                    }
                    break;
                }
            }

            private string ReadToken()
            {
                SkipTrivia();
                if (index >= text.Length)
                    return null;

                if (text[index] == '"')
                    return ReadQuotedToken();

                int start = index;
                while (index < text.Length)
                {
                    char ch = text[index];
                    if (Char.IsWhiteSpace(ch) || ch == '{' || ch == '}' || ch == '=' || ch == '#')
                        break;
                    index++;
                }
                return index > start ? text.Substring(start, index - start) : null;
            }

            private string ReadQuotedToken()
            {
                index++;
                StringBuilder sb = new StringBuilder();
                bool escape = false;
                while (index < text.Length)
                {
                    char ch = text[index++];
                    if (escape)
                    {
                        switch (ch)
                        {
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            case 'r': sb.Append('\r'); break;
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            default: sb.Append(ch); break;
                        }
                        escape = false;
                        continue;
                    }

                    if (ch == '\\')
                    {
                        escape = true;
                        continue;
                    }
                    if (ch == '"')
                        return sb.ToString();
                    sb.Append(ch);
                }

                throw new FormatException("Unterminated quoted pdx_settings token.");
            }
        }
    }
}
