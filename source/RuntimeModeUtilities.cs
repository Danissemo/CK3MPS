using System;

namespace CK3MPS
{
    internal static class RuntimeModeUtilities
    {
        public static string ResolveStabilizerRoot(string nonPortableRoot, string portableRoot, bool portableMode)
        {
            return portableMode ? portableRoot : nonPortableRoot;
        }

        public static bool ShouldSuppressLogLine(string verbosity, string formatted)
        {
            string text = (formatted ?? "").TrimStart();
            if (text.Length == 0)
                return String.Equals(verbosity, "Quiet", StringComparison.OrdinalIgnoreCase);

            if (String.Equals(verbosity, "Quiet", StringComparison.OrdinalIgnoreCase))
                return !IsQuietVisible(text);

            if (String.Equals(verbosity, "Normal", StringComparison.OrdinalIgnoreCase))
                return StartsWithAny(text, "VERBOSE", "DEBUG", "TRACE") || IsNormalModeInfoSpam(text);

            return false;
        }

        private static bool IsQuietVisible(string text)
        {
            return StartsWithAny(text, "OK", "FAIL", "WARN", "ERROR", "RESULT", "RISK", "GUARD");
        }

        private static bool IsNormalModeInfoSpam(string text)
        {
            if (!text.StartsWith("INFO", StringComparison.OrdinalIgnoreCase))
                return false;

            return StartsWithAny(
                text,
                "INFO Snapshot updated",
                "INFO Snapshot summary",
                "INFO UI log compacted",
                "INFO Workflow status report already up to date",
                "INFO Scan mode:",
                "INFO Migrated legacy",
                "INFO Live log restarted");
        }

        private static bool StartsWithAny(string text, params string[] prefixes)
        {
            foreach (string prefix in prefixes)
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
