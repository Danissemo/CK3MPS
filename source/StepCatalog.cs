using System;
using System.Collections;

namespace CK3MPS
{
    internal static class StepCatalog
    {
        public const int CreateRestorePoint = 0;
        public const int CheckPathsAndProcesses = 1;
        public const int CreateQuarantine = 2;
        public const int FlushDns = 3;
        public const int DiagnoseNetwork = 4;
        public const int AddFirewallRules = 5;
        public const int ApplyWindowsProfile = 6;
        public const int TunePowerAdapters = 7;
        public const int CheckOverlaysVpn = 8;
        public const int CheckOnlineServices = 9;
        public const int BackupLauncherSettings = 10;
        public const int StabilizeSteamSettings = 11;
        public const int RebuildLauncherDatabase = 12;
        public const int CheckRuntimeHygiene = 13;
        public const int ForceNoMods = 14;
        public const int StabilizePdxSettings = 15;
        public const int ConfirmLaunchedProfile = 16;
        public const int WriteCampaignProfile = 17;
        public const int ClearPlayerState = 18;
        public const int ArchiveReports = 19;
        public const int ClearCaches = 20;
        public const int QuarantineModDescriptors = 21;
        public const int InspectLoaderFiles = 22;
        public const int CheckSaveHygiene = 23;
        public const int CleanDocumentsFolder = 24;
        public const int AnalyzeOos = 25;
        public const int WriteSupportPackage = 26;
        public const int WritePreventionRules = 27;
        public const int WriteParityManifest = 28;

        private static readonly string[] ExpectedLabels =
        {
            "Create Windows restore point",
            "Check CK3 folders and running processes",
            "Create timestamped quarantine backup",
            "Flush DNS cache",
            "Diagnose adapters, routes, DNS, MTU and TCP/IP",
            "Add CK3 allow rules when elevated",
            "Apply game/network stability profile",
            "Tune power and adapter stability profile",
            "Check overlays, VPNs and competing background apps",
            "Check Paradox and Steam online reachability",
            "Back up Steam and Paradox Launcher settings",
            "Stabilize CK3 launch/cloud/overlay settings",
            "Rebuild CK3 launcher database",
            "Check runtime hygiene",
            "Force no-mod dlc_load.json",
            "Stabilize pdx_settings.txt",
            "Confirm launched profile",
            "Write stable new-campaign profile",
            "Clear player UI state",
            "Archive OOS and crash reports",
            "Clear CK3 and launcher caches",
            "Quarantine local .mod descriptors",
            "Inspect non-vanilla loader files",
            "Check active save and save-folder hygiene",
            "Remove nonessential files, keep saves",
            "Analyze latest OOS metadata",
            "Write support package index",
            "Write prevention rules",
            "Write player comparison manifest"
        };

        private static readonly int[] Recommended =
        {
            CreateRestorePoint,
            FlushDns,
            DiagnoseNetwork,
            CheckOverlaysVpn,
            CheckOnlineServices,
            BackupLauncherSettings,
            StabilizeSteamSettings,
            RebuildLauncherDatabase,
            CheckRuntimeHygiene,
            ForceNoMods,
            StabilizePdxSettings,
            ConfirmLaunchedProfile,
            WriteCampaignProfile,
            ClearPlayerState,
            ArchiveReports,
            ClearCaches,
            QuarantineModDescriptors,
            InspectLoaderFiles,
            AnalyzeOos,
            WriteSupportPackage,
            WritePreventionRules,
            WriteParityManifest
        };

        public static bool Validate(IList actualLabels, int[] recommendedIndices, out string error)
        {
            error = "";
            if (actualLabels == null || actualLabels.Count != ExpectedLabels.Length)
            {
                error = "Expected " + ExpectedLabels.Length + " checklist steps, found " + (actualLabels == null ? 0 : actualLabels.Count) + ".";
                return false;
            }

            for (int i = 0; i < ExpectedLabels.Length; i++)
            {
                string actual = Convert.ToString(actualLabels[i]) ?? "";
                if (!String.Equals(actual, ExpectedLabels[i], StringComparison.Ordinal))
                {
                    error = "Step " + i + " changed meaning. Expected '" + ExpectedLabels[i] + "', found '" + actual + "'.";
                    return false;
                }
            }

            if (recommendedIndices == null || recommendedIndices.Length != Recommended.Length)
            {
                error = "Recommended preset step count changed unexpectedly.";
                return false;
            }
            for (int i = 0; i < Recommended.Length; i++)
            {
                if (recommendedIndices[i] != Recommended[i])
                {
                    error = "Recommended preset mapping changed at position " + i + ". Expected step " + Recommended[i] + ", found " + recommendedIndices[i] + ".";
                    return false;
                }
            }
            return true;
        }
    }
}
