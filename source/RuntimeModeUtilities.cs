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
                return IsDiagnosticLine(text);

            return false;
        }

        public static bool IsDiagnosticLine(string formatted)
        {
            string text = (formatted ?? "").TrimStart();
            return StartsWithAny(text, "VERBOSE", "DEBUG", "TRACE", "DIAGNOSTIC");
        }

        public static bool ShouldShowDiagnosticEvents(string verbosity)
        {
            return String.Equals(verbosity, "Verbose", StringComparison.OrdinalIgnoreCase)
                || String.Equals(verbosity, "Debug", StringComparison.OrdinalIgnoreCase)
                || String.Equals(verbosity, "Diagnostic", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsQuietVisible(string text)
        {
            return StartsWithAny(text, "OK", "FAIL", "WARN", "ERROR", "RESULT", "RISK", "GUARD")
                || ContainsAny(text, "NOT READY", "ROLLBACK FAILURE", "SECURITY REFUSAL", "FAILED POSTCONDITION", "FAILED CHECK");
        }

        private static bool StartsWithAny(string text, params string[] prefixes)
        {
            foreach (string prefix in prefixes)
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            foreach (string needle in needles)
                if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }
    }
}
