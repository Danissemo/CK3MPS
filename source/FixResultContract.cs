using System;
using System.Collections.Generic;
using System.IO;

namespace CK3MPS
{
    internal enum FixOperationStatus
    {
        Succeeded,
        PartiallySucceeded,
        Failed,
        Unsupported,
        Cancelled
    }

    internal sealed class FixReadinessSnapshot
    {
        public int FailedChecks;
        public string Verdict = "UNKNOWN";
        public DateTime CapturedUtc = DateTime.UtcNow;

        public bool IsReady
        {
            get { return FailedChecks == 0 && String.Equals(Verdict, "READY", StringComparison.OrdinalIgnoreCase); }
        }
    }

    internal sealed class FixOperationResult
    {
        public string OperationId = "";
        public FixOperationStatus Status = FixOperationStatus.Failed;
        public readonly List<string> ChangedElements = new List<string>();
        public readonly List<string> Preconditions = new List<string>();
        public readonly List<string> FailedPreconditions = new List<string>();
        public readonly List<string> Postconditions = new List<string>();
        public readonly List<string> FailedPostconditions = new List<string>();
        public readonly List<int> TargetCheckIds = new List<int>();
        public readonly List<int> RemainingTargetFailedCheckIds = new List<int>();
        public FixReadinessSnapshot ReadinessBefore = new FixReadinessSnapshot();
        public FixReadinessSnapshot ReadinessAfter = new FixReadinessSnapshot();
        public string RollbackStatus = "not_required";
        public string UserMessage = "";
        public string DiagnosticDetails = "";
        public bool MutationAttempted;
        public bool MutationSucceeded;
        public bool VerificationAttempted;
        public bool VerificationSucceeded;

        public bool IsFinalSuccess
        {
            get { return Status == FixOperationStatus.Succeeded && VerificationSucceeded && FailedPostconditions.Count == 0 && RemainingTargetFailedCheckIds.Count == 0; }
        }

        public string ResultLogLine()
        {
            if (IsFinalSuccess)
                return "RESULT| SUCCESS. Target postconditions passed for " + OperationId + ".";

            if (Status == FixOperationStatus.Cancelled)
                return "RESULT| CANCELLED. Fix operation was cancelled before verified success: " + OperationId + ".";

            if (Status == FixOperationStatus.Unsupported)
                return "RESULT| UNSUPPORTED. Fix operation cannot run safely here: " + OperationId + ".";

            if (Status == FixOperationStatus.PartiallySucceeded)
                return "RESULT| PARTIAL. Mutation completed but verified success is incomplete for " + OperationId + ".";

            return "RESULT| FAILED. Target postconditions still failed for " + OperationId + ".";
        }
    }

    internal static class FixOperationResultEvaluator
    {
        public static FixOperationResult Evaluate(
            string operationId,
            IEnumerable<int> targetCheckIds,
            IEnumerable<int> failedCheckIdsAfterRescan,
            FixReadinessSnapshot readinessBefore,
            FixReadinessSnapshot readinessAfter,
            bool mutationAttempted,
            bool mutationSucceeded,
            bool rescanSucceeded,
            IEnumerable<string> failedPreconditions,
            IEnumerable<string> failedPostconditions,
            string rollbackStatus,
            bool cancelled,
            bool unsupported,
            IEnumerable<string> changedElements,
            string diagnosticDetails)
        {
            FixOperationResult result = new FixOperationResult();
            result.OperationId = operationId ?? "";
            result.ReadinessBefore = readinessBefore ?? new FixReadinessSnapshot();
            result.ReadinessAfter = readinessAfter ?? new FixReadinessSnapshot();
            result.MutationAttempted = mutationAttempted;
            result.MutationSucceeded = mutationSucceeded;
            result.VerificationAttempted = rescanSucceeded;
            result.VerificationSucceeded = rescanSucceeded;
            result.RollbackStatus = String.IsNullOrWhiteSpace(rollbackStatus) ? "not_required" : rollbackStatus;
            result.DiagnosticDetails = diagnosticDetails ?? "";

            AddRange(result.TargetCheckIds, targetCheckIds);
            AddRange(result.ChangedElements, changedElements);
            AddRange(result.FailedPreconditions, failedPreconditions);
            AddRange(result.FailedPostconditions, failedPostconditions);

            HashSet<int> targets = new HashSet<int>(result.TargetCheckIds);
            foreach (int failedId in failedCheckIdsAfterRescan ?? new int[0])
            {
                if (targets.Contains(failedId))
                    result.RemainingTargetFailedCheckIds.Add(failedId);
            }

            if (cancelled)
            {
                result.Status = FixOperationStatus.Cancelled;
                result.UserMessage = "Fix was cancelled. No success state was recorded.";
                result.VerificationSucceeded = false;
                return result;
            }

            if (unsupported)
            {
                result.Status = FixOperationStatus.Unsupported;
                result.UserMessage = "Fix is unsupported in the current environment.";
                result.VerificationSucceeded = false;
                return result;
            }

            if (result.FailedPreconditions.Count > 0)
            {
                result.Status = FixOperationStatus.Failed;
                result.UserMessage = "Fix did not start because required preconditions failed.";
                result.VerificationSucceeded = false;
                return result;
            }

            if (!mutationAttempted || !mutationSucceeded)
            {
                result.Status = FixOperationStatus.Failed;
                result.UserMessage = "Fix mutation did not complete successfully.";
                result.VerificationSucceeded = false;
                return result;
            }

            if (!rescanSucceeded)
            {
                result.Status = FixOperationStatus.Failed;
                result.UserMessage = "Fix mutation completed, but verification rescan failed. Treating operation as failed.";
                result.VerificationSucceeded = false;
                return result;
            }

            if (result.FailedPostconditions.Count > 0 || result.RemainingTargetFailedCheckIds.Count > 0)
            {
                result.Status = FixOperationStatus.Failed;
                result.UserMessage = "Fix mutation completed, but target postconditions are still failing.";
                result.VerificationSucceeded = false;
                return result;
            }

            if (!result.ReadinessAfter.IsReady)
            {
                result.Status = FixOperationStatus.PartiallySucceeded;
                result.UserMessage = "Target postconditions passed, but unrelated readiness checks still block full READY.";
                return result;
            }

            result.Status = FixOperationStatus.Succeeded;
            result.UserMessage = "Fix succeeded and final readiness is READY.";
            return result;
        }

        private static void AddRange<T>(ICollection<T> target, IEnumerable<T> values)
        {
            if (target == null || values == null)
                return;
            foreach (T value in values)
                target.Add(value);
        }
    }

    internal sealed partial class MainForm
    {
        private List<int> BuildSelectedFixTargetCheckIds()
        {
            List<int> targetCheckIds = new List<int>();
            for (int i = 0; i < StepCatalog.Count; i++)
            {
                if (IsStepChecked(i))
                    targetCheckIds.Add(i);
            }
            return targetCheckIds;
        }

        private List<int> CollectFailedFixTargetCheckIds(IEnumerable<int> targetCheckIds, bool includeRestorePointCheck)
        {
            List<int> failed = new List<int>();
            foreach (int targetCheckId in targetCheckIds ?? new int[0])
            {
                if (!EvaluateFixTargetCheck(targetCheckId, includeRestorePointCheck))
                    failed.Add(targetCheckId);
            }
            return failed;
        }

        private List<string> BuildFailedPostconditionMessages(IEnumerable<int> failedTargetCheckIds)
        {
            List<string> messages = new List<string>();
            foreach (int failedTargetCheckId in failedTargetCheckIds ?? new int[0])
                messages.Add(StepTitle(failedTargetCheckId));
            return messages;
        }

        private void LogFixResultDetails(FixOperationResult result)
        {
            if (result == null)
                return;

            Log(result.ResultLogLine());
            Log("INFO Fix contract | operation: " + result.OperationId + " | status: " + result.Status + " | before failed: " + result.ReadinessBefore.FailedChecks + " | after failed: " + result.ReadinessAfter.FailedChecks);
            if (result.RemainingTargetFailedCheckIds.Count > 0)
            {
                foreach (int failedTargetCheckId in result.RemainingTargetFailedCheckIds)
                    Log("FAIL Target postcondition still failed: " + StepTitle(failedTargetCheckId));
                Log("INFO Safe next action: run Scan, review the failed target checks, then apply only the affected fix steps again.");
            }
        }

        private bool EvaluateFixTargetCheck(int index, bool includeRestorePointCheck)
        {
            switch (index)
            {
                case StepCatalog.CreateRestorePoint:
                    return !includeRestorePointCheck || WindowsRestorePointInfrastructureOk();
                case StepCatalog.CheckPathsAndProcesses:
                    return Directory.Exists(ck3Docs) && !IsGameRunning() && VersionParityBaselineOk() && SteamUpdateComplete();
                case StepCatalog.CreateQuarantine:
                    return !String.IsNullOrEmpty(GetKnownQuarantine()) && Directory.Exists(GetKnownQuarantine());
                case StepCatalog.FlushDns:
                    return NetworkBaselineOk();
                case StepCatalog.DiagnoseNetwork:
                    return HasAnyActiveNetworkRoute() && NetworkBaselineOk();
                case StepCatalog.AddFirewallRules:
                    return FirewallRulesPresent();
                case StepCatalog.ApplyWindowsProfile:
                    return WindowsGameNetworkProfileOk();
                case StepCatalog.TunePowerAdapters:
                    return PowerAdapterProfileOk();
                case StepCatalog.CheckOverlaysVpn:
                    return WindowsAppsAndServicesOk();
                case StepCatalog.CheckOnlineServices:
                    return OnlineServicesOk();
                case StepCatalog.BackupLauncherSettings:
                    return SteamAndLauncherBackupSourcesOk();
                case StepCatalog.StabilizeSteamSettings:
                    return HasNoAsync() && !HasRiskyLaunchOptions() && SteamCloudDisabledOrUnknownQuiet();
                case StepCatalog.RebuildLauncherDatabase:
                    return !File.Exists(Path.Combine(ck3Docs, "launcher-v2.sqlite")) || DlcLoadProfileClean();
                case StepCatalog.CheckRuntimeHygiene:
                    return !ProcessRunningContains("dowser") && !ProcessRunningContains("paradox launcher") && !ProcessRunningExact("ck3");
                case StepCatalog.ForceNoMods:
                    return DlcLoadProfileClean() && !HasUtf8Bom(Path.Combine(ck3Docs, "dlc_load.json"));
                case StepCatalog.StabilizePdxSettings:
                    return StableCriticalSettingsOk() && !HasUtf8Bom(Path.Combine(ck3Docs, "pdx_settings.txt"));
                case StepCatalog.ConfirmLaunchedProfile:
                    return File.Exists(StabilizerFile("ck3_stabilizer_runtime_verification.txt")) && File.Exists(StabilizerFile("ck3_stabilizer_settings_guard.txt")) && !RuntimeProfileLooksBadAfterSettings();
                case StepCatalog.WriteCampaignProfile:
                    return File.Exists(StabilizerFile("ck3_stabilizer_in_game_mp_settings.txt"));
                case StepCatalog.ClearPlayerState:
                    return PlayerStateNonCritical();
                case StepCatalog.ArchiveReports:
                    return ReportsClean();
                case StepCatalog.ClearCaches:
                    return CacheFoldersClean();
                case StepCatalog.QuarantineModDescriptors:
                    return CountFiles(Path.Combine(ck3Docs, "mod"), "*.mod") == 0;
                case StepCatalog.InspectLoaderFiles:
                    return !String.IsNullOrEmpty(ck3Bin) && Directory.Exists(ck3Bin) && CountSuspectBinaries() == 0;
                case StepCatalog.CheckSaveHygiene:
                    return ActiveSaveVersionOk() && SaveLaunchHygieneOk() && BestCleanSaveReadable() && BestCleanSaveVersionOk();
                case StepCatalog.CleanDocumentsFolder:
                    return Ck3DocumentsCleanupOk();
                case StepCatalog.AnalyzeOos:
                    return File.Exists(StabilizerFile("ck3_stabilizer_latest_oos_summary.txt")) || String.IsNullOrEmpty(FindLatestOosMetadataFile());
                case StepCatalog.WriteSupportPackage:
                    return File.Exists(StabilizerFile("ck3_stabilizer_evidence_pack_index.txt"));
                case StepCatalog.WritePreventionRules:
                    return File.Exists(StabilizerFile("ck3_stabilizer_oos_protocol.txt"));
                case StepCatalog.WriteParityManifest:
                    return ParityManifestComplete() && File.Exists(StabilizerFile("ck3_stabilizer_oos_risk_score.txt"));
                default:
                    return false;
            }
        }
    }
}
