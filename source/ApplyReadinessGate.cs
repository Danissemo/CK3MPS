using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            InstallApplySettingsReadinessGate();
        }

        private void InstallApplySettingsReadinessGate()
        {
            try
            {
                ReplaceButtonClickHandlers(stabilizeButton, RunStabilizeWithReadinessGate);
                Log("INFO Apply Settings readiness gate installed.");
            }
            catch (Exception ex)
            {
                Log("WARN Apply Settings readiness gate could not replace the default handler: " + ex.Message);
            }
        }

        private void ReplaceButtonClickHandlers(Button button, EventHandler replacement)
        {
            PropertyInfo eventsProperty = typeof(Control).GetProperty("Events", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo clickField = typeof(Control).GetField("EventClick", BindingFlags.Static | BindingFlags.NonPublic);
            if (eventsProperty == null || clickField == null)
                throw new MissingMemberException("WinForms click event internals are unavailable.");

            EventHandlerList events = eventsProperty.GetValue(button, null) as EventHandlerList;
            object clickKey = clickField.GetValue(null);
            if (events == null || clickKey == null)
                throw new InvalidOperationException("WinForms click event list is unavailable.");

            Delegate existing = events[clickKey];
            if (existing != null)
                events.RemoveHandler(clickKey, existing);
            button.Click += replacement;
        }

        private async void RunStabilizeWithReadinessGate(object sender, EventArgs e)
        {
            int finalizeGeneration = ++deferredFinalizeGeneration;
            SetBusy(true);
            ClearLogViews();
            SetProgressValueSafe(0);
            SetProgressMaximumSafe(1);

            try
            {
                LogSection("Run started");
                Log("Preset: " + NullText(Convert.ToString(presetBox.SelectedItem)));
                Log("Selected steps: " + CountSelectedSteps());

                if (!ValidateBeforeRun())
                {
                    SetStatusText("Stopped: fix folder paths before running Stabilize.");
                    AppendRunHistory("stabilize", "stopped_path_validation");
                    return;
                }

                if (CountSelectedSteps() == 0)
                {
                    SetStatusText("No steps selected.");
                    Log("No steps selected. Choose a preset or tick steps manually.");
                    AppendRunHistory("stabilize", "stopped_no_steps");
                    return;
                }

                if (!HasReusableFreshCheckOnlyScan())
                {
                    SetStatusText("Run Scan first to activate Apply Settings.");
                    Log("INFO Apply Settings is locked until a fresh Scan is completed in this session.");
                    return;
                }

                Log("INFO Reusing the fresh Scan from this session.");
                LogSection("Stabilize plan");
                await EnsurePlanningSnapshotPreparedAsync("Preparing apply plan...");

                int plannedSteps = CountPlannedStabilizeSteps();
                SetProgressMaximumSafe(plannedSteps);
                Log("Planned steps after current-state filtering: " + plannedSteps);

                if (plannedSteps == 0)
                {
                    SetStatusText("All selected items are already applied.");
                    Log("INFO All selected items are already in the target state. No changes were needed.");
                    AppendRunHistory("stabilize", "stopped_no_changes_needed");
                    return;
                }

                if (IsGameRunning())
                {
                    MessageBox.Show("Close CK3 and Paradox Launcher first. Steam may stay open.", "CK3 is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Log("Stopped: CK3 or Paradox Launcher is running.");
                    AppendRunHistory("stabilize", "stopped_game_running");
                    return;
                }

                bool shouldStartGuard = IsStepChecked(StepCatalog.ForceNoMods) || IsStepChecked(StepCatalog.StabilizePdxSettings) || IsStepChecked(StepCatalog.ConfirmLaunchedProfile);
                CaptureExecutionSnapshot();
                await Task.Run(delegate
                {
                    RunCoreStabilizeStep(StepCatalog.CheckPathsAndProcesses, "Safety: checking paths", CheckBasePaths, ShouldRunPathValidationCoreStep());
                    RunCoreStabilizeStep(StepCatalog.CreateQuarantine, "Safety: creating quarantine", CreateQuarantine, ShouldRunQuarantineCoreStep());
                    RunPlannedStabilizeStep(StepCatalog.CreateRestorePoint, "Safety: creating Windows restore point", CreateWindowsRestorePoint);
                    RunPlannedStabilizeStep(StepCatalog.FlushDns, "Windows network: flushing DNS cache", FlushDnsCache);
                    RunPlannedStabilizeStep(StepCatalog.DiagnoseNetwork, "Windows network: diagnosing adapters and routes", RunNetworkDiagnostics);
                    RunPlannedStabilizeStep(StepCatalog.AddFirewallRules, "Windows firewall: adding CK3 rules", EnsureFirewallRules);
                    RunPlannedStabilizeStep(StepCatalog.ApplyWindowsProfile, "Windows registry: applying game/network profile", ApplyWindowsGameNetworkProfile);
                    RunPlannedStabilizeStep(StepCatalog.TunePowerAdapters, "Windows adapters: tuning power profile", ApplyPowerAdapterProfile);
                    RunPlannedStabilizeStep(StepCatalog.CheckOverlaysVpn, "Windows apps: checking overlays and VPNs", CheckOverlaysAndVpn);
                    RunPlannedStabilizeStep(StepCatalog.CheckOnlineServices, "Windows network: checking online services", CheckOnlineServices);
                    RunPlannedStabilizeStep(StepCatalog.BackupLauncherSettings, "Launchers: backing up settings", BackupSteamAndLauncherSettings);
                    RunPlannedStabilizeStep(StepCatalog.StabilizeSteamSettings, "Steam: stabilizing CK3 settings", StabilizeSteamSettings);
                    RunPlannedStabilizeStep(StepCatalog.RebuildLauncherDatabase, "Paradox Launcher: rebuilding database", RebuildParadoxLauncherDatabase);
                    RunPlannedStabilizeStep(StepCatalog.CheckRuntimeHygiene, "Launchers: checking runtime hygiene", CheckLauncherRuntimeHygiene);
                    RunPlannedStabilizeStep(StepCatalog.ForceNoMods, "CK3 external profile: writing no-mod profile", ForceNoMods);
                    RunPlannedStabilizeStep(StepCatalog.StabilizePdxSettings, "CK3 external settings: stabilizing settings", StabilizePdxSettings);
                    RunPlannedStabilizeStep(StepCatalog.ConfirmLaunchedProfile, "CK3 runtime verification: writing launch report", WriteRuntimeVerificationReport);
                    RunPlannedStabilizeStep(StepCatalog.WriteCampaignProfile, "CK3 in-game rules: writing game-rule profile", WriteStableGameRuleProfile);
                    RunPlannedStabilizeStep(StepCatalog.ClearPlayerState, "CK3 user state: clearing player UI state", ClearPlayerState);
                    RunPlannedStabilizeStep(StepCatalog.ArchiveReports, "CK3 reports: archiving OOS and crashes", ArchiveReports);
                    RunPlannedStabilizeStep(StepCatalog.ClearCaches, "CK3 cache: clearing CK3 and launcher caches", ClearCaches);
                    RunPlannedStabilizeStep(StepCatalog.QuarantineModDescriptors, "CK3 mods: quarantining .mod descriptors", QuarantineModDescriptors);
                    RunPlannedStabilizeStep(StepCatalog.InspectLoaderFiles, "CK3 binaries: inspecting non-vanilla files", QuarantineLoaderFiles);
                    RunPlannedStabilizeStep(StepCatalog.CheckSaveHygiene, "CK3 saves: stabilizing save launch hygiene", StabilizeSaveHygiene);
                    RunPlannedStabilizeStep(StepCatalog.CleanDocumentsFolder, "CK3 folder cleanup: removing nonessential files", CleanCk3DocumentsFolder);
                    RunPlannedStabilizeStep(StepCatalog.AnalyzeOos, "OOS reports: analyzing latest metadata", AnalyzeLatestOosReport);
                    RunPlannedStabilizeStep(StepCatalog.WriteSupportPackage, "OOS evidence: writing support package index", WriteOosEvidencePack);
                    RunPlannedStabilizeStep(StepCatalog.WritePreventionRules, "OOS protocol: writing prevention rules", WriteOosPreventionProtocol);
                    RunPlannedStabilizeStep(StepCatalog.WriteParityManifest, "MP parity: writing comparison manifest", WriteMultiplayerParityManifest);
                    LogSection("Final readiness summary");
                    RunFixReadinessChecks(true);
                });

                string[] runLogLines = SnapshotRunLogLines();
                int readinessFailures = lastReadinessFailures;
                lastCheckOnlyReportText = BuildCheckOnlyReportText(readinessFailures, runLogLines);
                exportScanReportButton.Enabled = true;

                if (readinessFailures != 0)
                {
                    SetStatusText("Failed: Apply Settings left " + readinessFailures + " readiness blocker(s).");
                    Log("RESULT FAILED. Apply Settings did not pass the readiness gate: " + readinessFailures + " blocker(s) remain.");
                    AppendRunHistory("stabilize", "failed_readiness_gate");
                    InvalidateFreshCheckOnlyScan();
                    return;
                }

                SetStatusText("Done. CK3 profile is prepared for stable vanilla multiplayer.");
                Log("Done. Use host local save, no hotjoin, speed 1-2 after load.");
                string historyLine = BuildRunHistoryLine(
                    "stabilize",
                    "ready",
                    Convert.ToString(presetBox.SelectedItem),
                    ck3Install,
                    ck3Docs,
                    readinessFailures);
                BeginDeferredStabilizeFinalize(finalizeGeneration, readinessFailures, runLogLines, historyLine, shouldStartGuard);
                InvalidateFreshCheckOnlyScan();
            }
            catch (Exception ex)
            {
                SetStatusText("Failed: " + ex.Message);
                Log("ERROR: " + ex);
                AppendRunHistory("stabilize", "failed");
                MessageBox.Show(ex.Message, "CK3MPS", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ClearExecutionSnapshot();
                SetBusy(false);
            }
        }

        private void RunFixReadinessChecks(bool includeRestorePointCheck)
        {
            int failed = 0;
            lastReadinessFailures = 0;
            Log("Readiness check: ordered by the checklist.");

            if (includeRestorePointCheck)
                failed += CheckStepResult(0, WindowsRestorePointInfrastructureOk());
            else
                Log("INFO Readiness skipped Windows restore point infrastructure in Scan mode.");
            failed += CheckStepResult(1, Directory.Exists(ck3Docs) && !IsGameRunning() && VersionParityBaselineOk() && SteamUpdateComplete());
            failed += CheckStepResult(2, !String.IsNullOrEmpty(GetKnownQuarantine()) && Directory.Exists(GetKnownQuarantine()));
            failed += CheckStepResult(3, NetworkBaselineOk());
            failed += CheckStepResult(4, HasAnyActiveNetworkRoute() && NetworkBaselineOk());
            failed += CheckStepResult(5, FirewallRulesPresent());
            failed += CheckStepResult(6, WindowsGameNetworkProfileOk());
            failed += CheckStepResult(7, PowerAdapterProfileOk());
            failed += CheckStepResult(8, WindowsAppsAndServicesOk());
            failed += CheckStepResult(9, OnlineServicesOk());
            failed += CheckStepResult(10, FixBackupSourcesOk());
            failed += CheckStepResult(11, HasNoAsync() && !HasRiskyLaunchOptions() && SteamCloudDisabledOrUnknownQuiet());
            failed += CheckStepResult(12, !File.Exists(Path.Combine(ck3Docs, "launcher-v2.sqlite")) || DlcLoadProfileClean());
            failed += CheckStepResult(13, !ProcessRunningContains("dowser") && !ProcessRunningContains("paradox launcher") && !ProcessRunningExact("ck3"));
            failed += CheckStepResult(14, DlcLoadProfileClean() && !HasUtf8Bom(Path.Combine(ck3Docs, "dlc_load.json")));
            failed += CheckStepResult(15, StableCriticalSettingsOk() && !HasUtf8Bom(Path.Combine(ck3Docs, "pdx_settings.txt")));
            failed += CheckStepResult(16, File.Exists(StabilizerFile("ck3_stabilizer_runtime_verification.txt")) && !RuntimeProfileLooksBadAfterSettings());
            failed += CheckStepResult(17, File.Exists(StabilizerFile("ck3_stabilizer_in_game_mp_settings.txt")));
            failed += CheckStepResult(18, PlayerStateNonCritical());
            failed += CheckStepResult(19, ReportsClean());
            failed += CheckStepResult(20, CacheFoldersClean());
            failed += CheckStepResult(21, CountFiles(Path.Combine(ck3Docs, "mod"), "*.mod") == 0);
            failed += CheckStepResult(22, !String.IsNullOrEmpty(ck3Bin) && Directory.Exists(ck3Bin) && CountSuspectBinaries() == 0);
            failed += CheckStepResult(23, ActiveSaveVersionOk() && SaveLaunchHygieneOk() && BestCleanSaveReadable() && BestCleanSaveVersionOk());
            failed += CheckStepResult(24, Ck3DocumentsCleanupOk());
            failed += CheckStepResult(25, File.Exists(StabilizerFile("ck3_stabilizer_latest_oos_summary.txt")) || String.IsNullOrEmpty(FindLatestOosMetadataFile()));
            failed += CheckStepResult(26, File.Exists(StabilizerFile("ck3_stabilizer_evidence_pack_index.txt")));
            failed += CheckStepResult(27, File.Exists(StabilizerFile("ck3_stabilizer_oos_protocol.txt")));
            failed += CheckStepResult(28, ParityManifestComplete() && File.Exists(StabilizerFile("ck3_stabilizer_oos_risk_score.txt")));

            if (failed == 0)
                Log("OK   Final readiness summary | all checklist checks passed");
            else
                Log("ERROR Final readiness summary | checklist failed checks: " + failed);

            SetProgressValueSafe(Int32.MaxValue);
            SetStatusText(failed == 0 ? "READY for stable CK3 MP profile." : "Failed. Apply Settings left readiness blockers: " + failed);
            lastReadinessFailures = failed;
            Log(failed == 0 ? "RESULT READY." : "RESULT FAILED. Failed checks before final summary: " + failed);
        }

        private bool FixBackupSourcesOk()
        {
            return !String.IsNullOrEmpty(localConfig) && File.Exists(localConfig)
                && !String.IsNullOrEmpty(appManifest) && File.Exists(appManifest)
                && File.Exists(Path.Combine(ck3Docs, "dlc_load.json"))
                && File.Exists(Path.Combine(ck3Docs, "pdx_settings.txt"));
        }
    }
}
