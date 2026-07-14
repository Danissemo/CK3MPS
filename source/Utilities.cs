using System;
using System.IO;
using System.Text.RegularExpressions;

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
        public static bool IsNewerRelease(string latestTag, string currentTag)
        {
            int[] latest = VersionParts(latestTag);
            int[] current = VersionParts(currentTag);
            for (int i = 0; i < latest.Length; i++)
            {
                if (latest[i] > current[i])
                    return true;
                if (latest[i] < current[i])
                    return false;
            }

            return false;
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
}
