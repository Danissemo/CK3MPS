using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private bool runtimeStartupFixesApplied;

        private void ApplyRuntimeStartupFixesOnce()
        {
            if (runtimeStartupFixesApplied)
                return;

            runtimeStartupFixesApplied = true;
            ConfigureScanExportRuntimeFix();
            RecoverSteamSharedConfigPath();
        }

        private void RecoverSteamSharedConfigPath()
        {
            try
            {
                if (!String.IsNullOrEmpty(sharedConfig) && File.Exists(sharedConfig))
                    return;

                string detected = FindSteamSharedConfigWithFallback();
                if (String.IsNullOrEmpty(detected))
                {
                    Log("WARN Steam sharedconfig.vdf was not found in Steam userdata. Checked userdata/<id>/7/remote and userdata/<id>/config.");
                    return;
                }

                sharedConfig = detected;
                Log("OK   Steam sharedconfig.vdf detected: " + sharedConfig);
            }
            catch (Exception ex)
            {
                Log("WARN Steam sharedconfig.vdf recovery skipped: " + ex.Message);
            }
        }

        private string FindSteamSharedConfigWithFallback()
        {
            string steam = DetectSteamRoot();
            if (String.IsNullOrEmpty(steam))
                return "";

            string userData = Path.Combine(steam, "userdata");
            if (!Directory.Exists(userData))
                return "";

            List<string> candidates = new List<string>();
            foreach (string userDir in BoundedTraversalUtilities.EnumerateSteamUserDirectories(userData, MaxSteamUserDataUsers, MaxBoundedTraversalElapsedMs))
            {
                AddSharedConfigCandidate(candidates, Path.Combine(userDir, "7", "remote", "sharedconfig.vdf"));
                AddSharedConfigCandidate(candidates, Path.Combine(userDir, "config", "sharedconfig.vdf"));
            }

            string fallback = "";
            foreach (string candidate in candidates)
            {
                if (String.IsNullOrEmpty(fallback))
                    fallback = candidate;
                try
                {
                    string text = ReadTextShared(candidate);
                    if (text.IndexOf("\"1158310\"", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("Crusader Kings III", StringComparison.OrdinalIgnoreCase) >= 0)
                        return candidate;
                }
                catch
                {
                }
            }

            return fallback;
        }

        private void AddSharedConfigCandidate(List<string> candidates, string path)
        {
            if (candidates == null || String.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            string full = Path.GetFullPath(path);
            foreach (string existing in candidates)
                if (String.Equals(existing, full, StringComparison.OrdinalIgnoreCase))
                    return;
            candidates.Add(full);
        }
    }
}
