using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CK3MPS
{
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
            return new[]
            {
                0, 3, 4,
                8, 9,
                10, 11, 12, 13,
                14, 15, 16, 17, 18, 19, 20,
                22, 25, 26, 27, 28
            };
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
            if (String.IsNullOrEmpty(path))
                return false;
            if (IsUserGameDataPath(path, ck3Docs))
                return false;
            if (!String.IsNullOrEmpty(ck3Docs) && path.StartsWith(ck3Docs, StringComparison.OrdinalIgnoreCase))
                return true;
            return (!String.IsNullOrEmpty(localLauncher) && path.StartsWith(localLauncher, StringComparison.OrdinalIgnoreCase))
                || (!String.IsNullOrEmpty(roamingLauncher) && path.StartsWith(roamingLauncher, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsUserGameDataPath(string path, string ck3Docs)
        {
            if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(ck3Docs))
                return false;

            string normalizedPath = Ck3PathUtilities.NormalizeDirectoryPath(path);
            string normalizedDocs = Ck3PathUtilities.NormalizeDirectoryPath(ck3Docs);
            if (String.IsNullOrEmpty(normalizedPath) || String.IsNullOrEmpty(normalizedDocs))
                return false;
            if (!normalizedPath.StartsWith(normalizedDocs, StringComparison.OrdinalIgnoreCase))
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
}
