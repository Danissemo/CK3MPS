$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Get-NormalizedText {
    param([string]$RelativePath)
    $path = Join-Path $Root ($RelativePath -replace '/', '\')
    return ([IO.File]::ReadAllText($path)).Replace("`r`n", "`n")
}

function Set-NormalizedText {
    param([string]$RelativePath, [string]$Text)
    $path = Join-Path $Root ($RelativePath -replace '/', '\')
    [IO.File]::WriteAllText($path, $Text.Replace("`r`n", "`n"), $Utf8NoBom)
}

function Replace-LiteralOnce {
    param(
        [string]$RelativePath,
        [string]$OldText,
        [string]$NewText,
        [string]$AppliedMarker
    )
    $text = Get-NormalizedText $RelativePath
    $old = $OldText.Replace("`r`n", "`n")
    $new = $NewText.Replace("`r`n", "`n")
    if (-not [string]::IsNullOrEmpty($AppliedMarker) -and $text.Contains($AppliedMarker)) {
        return
    }
    $index = $text.IndexOf($old, [StringComparison]::Ordinal)
    if ($index -lt 0) {
        throw "Expected block was not found in $RelativePath"
    }
    if ($text.IndexOf($old, $index + $old.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "Expected block was not unique in $RelativePath"
    }
    $updated = $text.Substring(0, $index) + $new + $text.Substring($index + $old.Length)
    Set-NormalizedText $RelativePath $updated
}

function Replace-RegexOnce {
    param(
        [string]$RelativePath,
        [string]$Pattern,
        [string]$Replacement,
        [string]$AppliedMarker
    )
    $text = Get-NormalizedText $RelativePath
    if (-not [string]::IsNullOrEmpty($AppliedMarker) -and $text.Contains($AppliedMarker)) {
        return
    }
    $options = [Text.RegularExpressions.RegexOptions]::Multiline -bor [Text.RegularExpressions.RegexOptions]::Singleline
    $regex = New-Object Text.RegularExpressions.Regex($Pattern, $options)
    $matches = $regex.Matches($text)
    if ($matches.Count -ne 1) {
        throw "Expected one regex match in $RelativePath, found $($matches.Count): $Pattern"
    }
    Set-NormalizedText $RelativePath ($regex.Replace($text, $Replacement.Replace("`r`n", "`n"), 1))
}

# Compile the new hardening components.
Replace-LiteralOnce 'CK3MPS.csproj' @'
    <Compile Include="source\StepCatalog.cs" />
'@ @'
    <Compile Include="source\StepCatalog.cs" />
    <Compile Include="source\TransactionalOperations.cs" />
    <Compile Include="source\RestoreTransactions.cs" />
    <Compile Include="source\WorkflowAnalysisCoordinator.cs" />
'@ 'source\TransactionalOperations.cs'

# Portable mode: recover journals during startup and do not expose the new root until commit.
Replace-LiteralOnce 'source/AppConfig.cs' @'
            try
            {
                LoadLegacyPathOverrides();
'@ @'
            try
            {
                bool? recoveredPortableMode = TransactionalStateMigration.Recover(nonPortableStabilizerRoot, portableStabilizerRoot);
                if (recoveredPortableMode.HasValue)
                    portableMode = recoveredPortableMode.Value;
                LoadLegacyPathOverrides();
'@ 'recoveredPortableMode = TransactionalStateMigration.Recover'

Replace-RegexOnce 'source/AppConfig.cs' '(?m)^        private async Task SetPortableModeAsync\(bool enabled\)\n        \{.*?^        private void RelinkLiveLogPath' @'
        private async Task SetPortableModeAsync(bool enabled)
        {
            string resolvedRoot = RuntimeModeUtilities.ResolveStabilizerRoot(nonPortableStabilizerRoot, portableStabilizerRoot, enabled);
            if (portableMode == enabled && String.Equals(stabilizerRoot, resolvedRoot, StringComparison.OrdinalIgnoreCase))
                return;

            bool oldPortableMode = portableMode;
            string oldRoot = stabilizerRoot;
            string oldLiveLogPath = liveLogFilePath;
            bool migrationCommitted = false;

            portableModeChangeInProgress = true;
            portableModeBox.Enabled = false;
            try
            {
                InvalidateFreshCheckOnlyScan();
                statusLabel.Text = "Moving CK3MPS state for portable mode...";
                await Task.Run(delegate { TransactionalStateMigration.Migrate(oldRoot, resolvedRoot, enabled); });
                migrationCommitted = true;

                portableMode = enabled;
                RefreshStabilizerRoot();
                RelinkLiveLogPath(oldRoot, stabilizerRoot, oldLiveLogPath);
                SaveAppConfig();

                statusLabel.Text = enabled
                    ? "Portable mode enabled. CK3MPS state was moved next to the exe."
                    : "Portable mode disabled. CK3MPS state was moved back to Documents.";
                Log("INFO Portable mode " + (enabled ? "enabled" : "disabled") + ". State root: " + stabilizerRoot);
                LogVerbose("Portable mode migration: " + oldRoot + " -> " + stabilizerRoot);
                LogVerbose("Settings file: " + AppConfigFile());
            }
            catch
            {
                portableMode = migrationCommitted ? enabled : oldPortableMode;
                RefreshStabilizerRoot();
                UpdateSettingsUi();
                statusLabel.Text = migrationCommitted
                    ? "Portable mode moved successfully, but settings refresh failed. Restart CK3MPS to finish."
                    : "Portable mode change failed and was rolled back.";
                throw;
            }
            finally
            {
                portableModeChangeInProgress = false;
                portableModeBox.Enabled = true;
            }
        }

        private void RelinkLiveLogPath
'@ 'migrationCommitted = false'

# Restore selected/default now run as one rollback-capable batch.
Replace-LiteralOnce 'source/Restore.cs' @'
                activeRestoreOperationSnapshots = confirmationSnapshots;
                foreach (RestoreEntry entry in entries)
                {
                    if (entry.Kind == "file" || entry.Kind == "moved_file")
                        RestoreFileEntry(entry);
                    else if (entry.Kind == "created_file")
                        RestoreCreatedFileEntry(entry);
                    else if (entry.Kind == "directory" || entry.Kind == "moved_directory")
                        RestoreDirectoryEntry(entry);
                    else if (entry.Kind == "registry")
                        RestoreRegistryEntry(entry);
                    else
                        throw new InvalidOperationException("This restore entry is informational. Use the details text or Windows restore point for this item.");

                    Log("OK   Restored: " + entry.Description);
                }
                RefreshRestoreList();
'@ @'
                activeRestoreOperationSnapshots = confirmationSnapshots;
                ExecuteRestoreBatch(entries, false);
                foreach (RestoreEntry entry in entries)
                    Log("OK   Restored: " + entry.Description);
                RefreshRestoreList();
'@ 'ExecuteRestoreBatch(entries, false)'

Replace-LiteralOnce 'source/Restore.cs' @'
                activeRestoreOperationSnapshots = confirmationSnapshots;
                foreach (RestoreEntry entry in entries)
                {
                    RestoreDefaultEntry(entry);
                    Log("OK   Restored default behavior: " + entry.Description);
                }
                RefreshRestoreList();
'@ @'
                activeRestoreOperationSnapshots = confirmationSnapshots;
                ExecuteRestoreBatch(entries, true);
                foreach (RestoreEntry entry in entries)
                    Log("OK   Restored default behavior: " + entry.Description);
                RefreshRestoreList();
'@ 'ExecuteRestoreBatch(entries, true)'

Replace-RegexOnce 'source/Restore.cs' '(?m)^        private void RestoreDirectoryEntry\(RestoreEntry entry\)\n        \{.*?^        private void RestoreRegistryEntry' @'
        private void RestoreDirectoryEntry(RestoreEntry entry)
        {
            EnsureRestoreOperationStillAllowed(entry, "directory restore");
            if (!Directory.Exists(entry.BackupPath))
                throw new DirectoryNotFoundException("Backup directory is missing: " + entry.BackupPath);

            if (Directory.Exists(entry.SourcePath) && DirectoryContentsEqual(entry.SourcePath, entry.BackupPath))
                return;
            if (Directory.Exists(entry.SourcePath))
                BackupForRestore(entry.SourcePath, "Pre-restore backup of current directory: " + entry.SourcePath);

            AtomicReplaceDirectoryFromBackup(entry.BackupPath, entry.SourcePath);
            RecordRestoreEntry("restore_action", entry.SourcePath, entry.BackupPath, "Restored directory: " + entry.SourcePath, entry.Before, DescribePath(entry.SourcePath), "restored");
        }

        private void RestoreRegistryEntry
'@ 'AtomicReplaceDirectoryFromBackup(entry.BackupPath, entry.SourcePath)'

# Cancel stale workflow work immediately when the scenario changes.
Replace-LiteralOnce 'source/Workflow.cs' @'
                if (workflowModeBox.SelectedItem != null)
                {
                    currentWorkflowScenario = Convert.ToString(workflowModeBox.SelectedItem);
'@ @'
                if (workflowModeBox.SelectedItem != null)
                {
                    CancelWorkflowScenarioRefresh();
                    currentWorkflowScenario = Convert.ToString(workflowModeBox.SelectedItem);
'@ 'CancelWorkflowScenarioRefresh();'

Replace-LiteralOnce 'source/Workflow.cs' @'
            WorkflowScenarioSnapshot snapshot;
            if (!TryGetWorkflowScenarioSnapshot(scenario, out snapshot) && !workflowRefreshPending)
                BeginInvoke((MethodInvoker)delegate { BeginWorkflowScenarioRefresh(); });
'@ @'
            WorkflowScenarioSnapshot snapshot;
            if (!TryGetWorkflowScenarioSnapshot(scenario, out snapshot))
                BeginInvoke((MethodInvoker)delegate { BeginWorkflowScenarioRefresh(); });
'@ 'if (!TryGetWorkflowScenarioSnapshot(scenario, out snapshot))'

Replace-RegexOnce 'source/Workflow.cs' '(?m)^        private void BeginWorkflowScenarioRefresh\(\)\n        \{.*?^        private void RebuildWorkflowScenarioUi' @'
        private void BeginWorkflowScenarioRefresh()
        {
            if (!workflowUiInitialized)
                return;

            workflowRefreshPending = true;
            workflowLoadGeneration++;
            int generation = workflowLoadGeneration;
            string scenario = currentWorkflowScenario = NullText(Convert.ToString(workflowModeBox.SelectedItem)) == "(none)"
                ? currentWorkflowScenario
                : Convert.ToString(workflowModeBox.SelectedItem);
            CancellationToken cancellationToken = BeginWorkflowRefreshCancellation();

            workflowRenderTimer.Stop();
            updatingWorkflowUi = true;
            try
            {
                workflowStepsListBox.Items.Clear();
            }
            finally
            {
                updatingWorkflowUi = false;
            }

            workflowVerdictLabel.Text = "Status: checking your setup...";
            ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
            workflowSummaryBox.Text = "Loading workflow checks for " + scenario + "...";
            ApplyWorkflowSummaryStyling();
            workflowProgressBar.Value = 0;
            workflowProgressBar.Maximum = 1;
            workflowProgressBar.Visible = true;

            Task.Run(delegate
            {
                try
                {
                    WorkflowScenarioSnapshot snapshot = BuildWorkflowScenarioSnapshotCore(scenario, cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!WorkflowRefreshStillCurrent(generation, scenario, cancellationToken))
                            return;

                        workflowRenderStates = snapshot.States ?? new List<WorkflowStepState>();
                        workflowRenderVerdict = snapshot.Verdict;
                        workflowRenderSummary = snapshot.Summary;
                        workflowRenderIndex = 0;
                        workflowStepStates.Clear();
                        workflowStepStates.AddRange(workflowRenderStates);
                        StoreWorkflowScenarioSnapshot(snapshot);
                        workflowProgressBar.Maximum = Math.Max(1, workflowRenderStates.Count);
                        workflowProgressBar.Value = 0;
                        workflowProgressBar.Visible = true;
                        workflowRenderTimer.Start();
                    });
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!WorkflowRefreshStillCurrent(generation, scenario, cancellationToken))
                            return;

                        workflowRenderTimer.Stop();
                        workflowProgressBar.Visible = false;
                        workflowVerdictLabel.Text = "Status: workflow refresh failed.";
                        ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
                        workflowSummaryBox.Text = "Workflow checks could not be loaded for the selected scenario.";
                        ApplyWorkflowSummaryStyling();
                        workflowRefreshPending = false;
                    });
                }
            }, cancellationToken);
        }

        private void RebuildWorkflowScenarioUi
'@ 'BuildWorkflowScenarioSnapshotCore(scenario, cancellationToken)'

Replace-LiteralOnce 'source/Workflow.cs' @'
        private WorkflowScenarioSnapshot BuildWorkflowScenarioSnapshot(string scenario)
        {
            WorkflowScenarioSnapshot snapshot = new WorkflowScenarioSnapshot();
            snapshot.Scenario = scenario;
            BuildWorkflowScenarioSteps(scenario, snapshot.States);
            snapshot.Verdict = BuildWorkflowVerdictLine(scenario, snapshot.States);
            snapshot.Summary = BuildWorkflowScenarioSummaryText(scenario, snapshot.States);
            return snapshot;
        }
'@ @'
        private WorkflowScenarioSnapshot BuildWorkflowScenarioSnapshot(string scenario)
        {
            return BuildWorkflowScenarioSnapshotCore(scenario, CancellationToken.None);
        }
'@ 'return BuildWorkflowScenarioSnapshotCore(scenario, CancellationToken.None);'

Replace-LiteralOnce 'source/Workflow.cs' @'
        private void BuildWorkflowScenarioSteps(string scenario, List<WorkflowStepState> states)
        {
            HostSuitabilityResult host = AnalyzeHostSuitability();
            HostSaveCandidateResult save = AnalyzeWorkflowHostSaveCandidate();
            OosDeepInsight oos = AnalyzeLatestOosDeepInsight();
            OosIncidentState incident = AnalyzeOosIncidentState();
'@ @'
        private void BuildWorkflowScenarioSteps(string scenario, List<WorkflowStepState> states)
        {
            WorkflowAnalysisSnapshot analysis = CurrentWorkflowAnalysis();
            HostSuitabilityResult host = analysis.Host;
            HostSaveCandidateResult save = analysis.Save;
            OosDeepInsight oos = analysis.Oos;
            OosIncidentState incident = analysis.Incident;
'@ 'WorkflowAnalysisSnapshot analysis = CurrentWorkflowAnalysis();'

Replace-LiteralOnce 'source/Workflow.cs' @'
        private string BuildWorkflowVerdictLine(string scenario, List<WorkflowStepState> states)
        {
            OosDeepInsight oos = AnalyzeLatestOosDeepInsight();
'@ @'
        private string BuildWorkflowVerdictLine(string scenario, List<WorkflowStepState> states)
        {
            OosDeepInsight oos = CurrentWorkflowAnalysis().Oos;
'@ 'OosDeepInsight oos = CurrentWorkflowAnalysis().Oos;'

Replace-LiteralOnce 'source/Workflow.cs' @'
        private string BuildWorkflowScenarioSummaryText(string scenario, List<WorkflowStepState> states)
        {
            HostSuitabilityResult host = AnalyzeHostSuitability();
            HostSaveCandidateResult save = AnalyzeWorkflowHostSaveCandidate();
            OosDeepInsight oos = AnalyzeLatestOosDeepInsight();
            OosIncidentState incident = AnalyzeOosIncidentState();
'@ @'
        private string BuildWorkflowScenarioSummaryText(string scenario, List<WorkflowStepState> states)
        {
            WorkflowAnalysisSnapshot analysis = CurrentWorkflowAnalysis();
            HostSuitabilityResult host = analysis.Host;
            HostSaveCandidateResult save = analysis.Save;
            OosDeepInsight oos = analysis.Oos;
            OosIncidentState incident = analysis.Incident;
'@ 'private string BuildWorkflowScenarioSummaryText'

# The previous marker is method-wide, so explicitly ensure the analysis block was applied.
$text = Get-NormalizedText 'source/Workflow.cs'
if (-not $text.Contains('HostSuitabilityResult host = analysis.Host;')) {
    throw 'Workflow summary was not wired to the immutable analysis snapshot.'
}

Replace-LiteralOnce 'source/Workflow.cs' @'
        private string BuildWorkflowRecommendation(string scenario, List<WorkflowStepState> states, HostSuitabilityResult host, HostSaveCandidateResult save)
        {
            OosDeepInsight oos = AnalyzeLatestOosDeepInsight();
            OosIncidentState incident = AnalyzeOosIncidentState();
'@ @'
        private string BuildWorkflowRecommendation(string scenario, List<WorkflowStepState> states, HostSuitabilityResult host, HostSaveCandidateResult save)
        {
            WorkflowAnalysisSnapshot analysis = CurrentWorkflowAnalysis();
            OosDeepInsight oos = analysis.Oos;
            OosIncidentState incident = analysis.Incident;
'@ 'WorkflowAnalysisSnapshot analysis = CurrentWorkflowAnalysis();'

Replace-LiteralOnce 'source/Workflow.cs' @'
        private string BuildWorkflowStatusReportText()
        {
            List<WorkflowStepState> snapshot = new List<WorkflowStepState>();
            BuildWorkflowScenarioSteps(currentWorkflowScenario, snapshot);
            return BuildWorkflowScenarioSummaryText(currentWorkflowScenario, snapshot);
        }
'@ @'
        private string BuildWorkflowStatusReportText()
        {
            return BuildWorkflowScenarioSnapshotCore(currentWorkflowScenario, CancellationToken.None).Summary;
        }
'@ 'return BuildWorkflowScenarioSnapshotCore(currentWorkflowScenario, CancellationToken.None).Summary;'

# LAN parity room: bind only to the detected primary IPv4 interface, retaining all existing crypto/rate limits.
Replace-LiteralOnce 'source/Workflow.cs' @'
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
'@ @'
            IPAddress bindAddress;
            string advertisedAddress = DetectPrimaryIpv4Address();
            if (!IPAddress.TryParse(advertisedAddress, out bindAddress)
                || bindAddress.AddressFamily != AddressFamily.InterNetwork)
                bindAddress = IPAddress.Loopback;
            TcpListener listener = new TcpListener(bindAddress, 0);
            listener.Start();
'@ 'string advertisedAddress = DetectPrimaryIpv4Address();'

Replace-LiteralOnce 'source/Workflow.cs' @'
                TextBox hostBox = new TextBox { Left = 130, Top = 16, Width = 240, Text = "127.0.0.1" };
'@ @'
                TextBox hostBox = new TextBox { Left = 130, Top = 16, Width = 240, Text = DetectPrimaryIpv4Address() };
'@ 'Text = DetectPrimaryIpv4Address()'

Replace-LiteralOnce 'source/AppState.cs' @'
        private const int ExpectedStepCount = 29;
'@ @'
        private const int ExpectedStepCount = StepCatalog.Count;
'@ 'ExpectedStepCount = StepCatalog.Count'

Replace-LiteralOnce 'source/StepCatalog.cs' @'
    internal static class StepCatalog
    {
'@ @'
    internal static class StepCatalog
    {
        public const int Count = 29;
'@ 'public const int Count = 29;'

Replace-LiteralOnce 'source/StepCatalog.cs' @'
        public static bool Validate(IList actualLabels, int[] recommendedIndices, out string error)
'@ @'
        public static int[] RecommendedIndices()
        {
            return (int[])Recommended.Clone();
        }

        public static bool Validate(IList actualLabels, int[] recommendedIndices, out string error)
'@ 'public static int[] RecommendedIndices()'

Replace-RegexOnce 'source/Utilities.cs' '(?m)^        public static int\[\] RecommendedStepIndices\(\)\n        \{.*?^        \}' @'
        public static int[] RecommendedStepIndices()
        {
            return StepCatalog.RecommendedIndices();
        }
'@ 'return StepCatalog.RecommendedIndices();'

Replace-LiteralOnce 'scripts/test.ps1' @'
    (Join-Path $Root "source\Utilities.cs") `
    (Join-Path $Root "source\RuntimeModeUtilities.cs") `
'@ @'
    (Join-Path $Root "source\Utilities.cs") `
    (Join-Path $Root "source\RuntimeModeUtilities.cs") `
    (Join-Path $Root "source\StepCatalog.cs") `
'@ 'source\StepCatalog.cs'

# Replace checklist group/index literals with named constants.
$stepChecklist = Get-NormalizedText 'source/StepChecklist.cs'
$groupReplacements = [ordered]@{
    'new[] { 0, 1, 2 }' = 'new[] { StepCatalog.CreateRestorePoint, StepCatalog.CheckPathsAndProcesses, StepCatalog.CreateQuarantine }'
    'new[] { 3, 4, 5, 6, 7, 8, 9 }' = 'new[] { StepCatalog.FlushDns, StepCatalog.DiagnoseNetwork, StepCatalog.AddFirewallRules, StepCatalog.ApplyWindowsProfile, StepCatalog.TunePowerAdapters, StepCatalog.CheckOverlaysVpn, StepCatalog.CheckOnlineServices }'
    'new[] { 10, 11, 12, 13 }' = 'new[] { StepCatalog.BackupLauncherSettings, StepCatalog.StabilizeSteamSettings, StepCatalog.RebuildLauncherDatabase, StepCatalog.CheckRuntimeHygiene }'
    'new[] { 14, 15, 16, 17 }' = 'new[] { StepCatalog.ForceNoMods, StepCatalog.StabilizePdxSettings, StepCatalog.ConfirmLaunchedProfile, StepCatalog.WriteCampaignProfile }'
    'new[] { 18, 19, 20, 21, 22, 23, 24 }' = 'new[] { StepCatalog.ClearPlayerState, StepCatalog.ArchiveReports, StepCatalog.ClearCaches, StepCatalog.QuarantineModDescriptors, StepCatalog.InspectLoaderFiles, StepCatalog.CheckSaveHygiene, StepCatalog.CleanDocumentsFolder }'
    'new[] { 25, 26, 27, 28 }' = 'new[] { StepCatalog.AnalyzeOos, StepCatalog.WriteSupportPackage, StepCatalog.WritePreventionRules, StepCatalog.WriteParityManifest }'
}
foreach ($pair in $groupReplacements.GetEnumerator()) {
    $stepChecklist = $stepChecklist.Replace($pair.Key, $pair.Value)
}
$caseNames = @(
    'CreateRestorePoint','CheckPathsAndProcesses','CreateQuarantine','FlushDns','DiagnoseNetwork','AddFirewallRules','ApplyWindowsProfile','TunePowerAdapters','CheckOverlaysVpn','CheckOnlineServices',
    'BackupLauncherSettings','StabilizeSteamSettings','RebuildLauncherDatabase','CheckRuntimeHygiene','ForceNoMods','StabilizePdxSettings','ConfirmLaunchedProfile','WriteCampaignProfile','ClearPlayerState','ArchiveReports','ClearCaches','QuarantineModDescriptors','InspectLoaderFiles','CheckSaveHygiene','CleanDocumentsFolder','AnalyzeOos','WriteSupportPackage','WritePreventionRules','WriteParityManifest'
)
for ($i = 0; $i -lt $caseNames.Count; $i++) {
    $stepChecklist = $stepChecklist.Replace("case $i`:", "case StepCatalog.$($caseNames[$i]):")
}
Set-NormalizedText 'source/StepChecklist.cs' $stepChecklist

$mainWindow = Get-NormalizedText 'source/MainWindow.cs'
$mainWindow = $mainWindow.Replace('bool shouldStartGuard = IsStepChecked(14) || IsStepChecked(15) || IsStepChecked(16);', 'bool shouldStartGuard = IsStepChecked(StepCatalog.ForceNoMods) || IsStepChecked(StepCatalog.StabilizePdxSettings) || IsStepChecked(StepCatalog.ConfirmLaunchedProfile);')
$mainWindow = $mainWindow.Replace('RunCoreStabilizeStep(1,', 'RunCoreStabilizeStep(StepCatalog.CheckPathsAndProcesses,')
$mainWindow = $mainWindow.Replace('RunCoreStabilizeStep(2,', 'RunCoreStabilizeStep(StepCatalog.CreateQuarantine,')
for ($i = 0; $i -lt $caseNames.Count; $i++) {
    $mainWindow = $mainWindow.Replace("RunPlannedStabilizeStep($i,", "RunPlannedStabilizeStep(StepCatalog.$($caseNames[$i]),")
}
Set-NormalizedText 'source/MainWindow.cs' $mainWindow

Replace-LiteralOnce 'source/MainWindow.cs' @'
            if (steps.Items.Count != ExpectedStepCount)
                Log("WARN Step configuration mismatch: expected " + ExpectedStepCount + ", actual " + steps.Items.Count);

            if (steps.Items.Count > 0 && !steps.Items[0].ToString().StartsWith("Create Windows restore point", StringComparison.Ordinal))
                Log("WARN First checklist item is not the expected Safety block.");

            if (steps.Items.Count > 0 && !steps.Items[steps.Items.Count - 1].ToString().StartsWith("Write player comparison manifest", StringComparison.Ordinal))
                Log("WARN Last checklist item is not the expected MP parity block.");
'@ @'
            string catalogError;
            if (!StepCatalog.Validate(steps.Items, PresetUtilities.RecommendedStepIndices(), out catalogError))
                Log("WARN Step configuration mismatch: " + catalogError);
'@ 'StepCatalog.Validate(steps.Items'

# Correct strict SHA checking on Windows: inspect the Git index blob, not CRLF-transformed worktree bytes.
Replace-LiteralOnce 'scripts/check-static-danger-strict.ps1' @'
    $actual = git -C $Root hash-object -- $relativePath
'@ @'
    $actual = git -C $Root rev-parse "HEAD:$relativePath"
'@ 'rev-parse "HEAD:$relativePath"'

# Documentation: current source map, update behavior, transactions, and LAN scope.
Replace-LiteralOnce 'source/README.md' @'
- `Updates.cs`
  GitHub release lookup, checksum-aware update download, and external updater-script generation.
'@ @'
- `Updates.cs`
  GitHub release lookup and safe hand-off to the official releases page. Automatic in-place installation is intentionally disabled.
- `SaveAnalysis.cs`
  Bounded save parsing, rule checks, host-save scoring, and safe-copy preparation.
- `OosDeepAnalysis.cs`
  Deep OOS evidence parsing, contamination scoring, recovery recommendations, and incident history support.
- `Workflow.cs`
  Scenario UI, host/save/OOS workflow, parity comparison, and authenticated parity-room transport.
- `TransactionalOperations.cs`
  Crash-recoverable portable-mode migration with staging and a two-root journal.
- `RestoreTransactions.cs`
  Atomic directory replacement and rollback-capable multi-item restore transactions.
- `WorkflowAnalysisCoordinator.cs`
  Cancelable workflow refreshes and immutable per-refresh analysis snapshots.
- `StepCatalog.cs`
  Named IDs and validation for the 29 checklist actions.
'@ 'TransactionalOperations.cs'

Replace-LiteralOnce 'docs/CODEBASE.md' @'
- GitHub release checking and updater bootstrap
'@ @'
- GitHub release checking and safe navigation to the official release
'@ 'safe navigation to the official release'

Replace-LiteralOnce 'docs/CODEBASE.md' @'
- the app can self-detect newer published releases and download them safely
'@ @'
- the app can detect newer published releases without performing an unsafe in-place self-update
'@ 'without performing an unsafe in-place self-update'

Replace-LiteralOnce 'docs/CODEBASE.md' @'
- queries the GitHub releases API
- selects the matching asset and checksum
- validates SHA256 before starting updater flow
- generates a PowerShell updater script
'@ @'
- queries the GitHub releases API
- compares the latest release tag with the running version
- opens the official release page after explicit user action
- keeps automatic in-place installation disabled
'@ 'keeps automatic in-place installation disabled'

$codebase = Get-NormalizedText 'docs/CODEBASE.md'
if (-not $codebase.Contains('## Transactional Safety')) {
    $codebase += @'

## Transactional Safety

- Portable-mode migration copies into a staging tree, records progress in both state roots, verifies content, commits the destination, and recovers or finishes cleanup on the next startup.
- Directory restore stages and verifies the backup before swapping sibling directories with atomic renames.
- Multi-item restore captures reverse snapshots and the restore manifest, then rolls back already-applied entries if a later entry fails.
- Workflow refreshes use cancellation tokens and generation/scenario checks. One immutable host/save/OOS/incident snapshot feeds steps, verdict, summary, and recommendation for a refresh.
- Parity room listens on the detected primary IPv4 interface for LAN sessions. Payload encryption, authentication, replay checks, size limits, peer limits, and rate limits remain mandatory.
'@
    Set-NormalizedText 'docs/CODEBASE.md' $codebase
}

# Ensure shutdown cancels workflow work as well as the watcher.
Replace-LiteralOnce 'source/AppState.cs' @'
            FormClosed += delegate { StopOosWatcherServices(1500); };
'@ @'
            FormClosed += delegate { CancelWorkflowScenarioRefresh(); StopOosWatcherServices(1500); };
'@ 'CancelWorkflowScenarioRefresh(); StopOosWatcherServices'

# Fault points are test-only and leave the real journal semantics unchanged.
$transactional = Get-NormalizedText 'source/TransactionalOperations.cs'
if (-not $transactional.Contains('ThrowIfMigrationFault("copied")')) {
    $transactional = $transactional.Replace('                journal.Phase = "Copied";`n                PersistJournal(journal);'.Replace('`n', "`n"), '                journal.Phase = "Copied";`n                PersistJournal(journal);`n                ThrowIfMigrationFault("copied");'.Replace('`n', "`n"))
    $transactional = $transactional.Replace('                journal.Phase = "Committed";`n                PersistJournal(journal);'.Replace('`n', "`n"), '                journal.Phase = "Committed";`n                PersistJournal(journal);`n                ThrowIfMigrationFault("committed");'.Replace('`n', "`n"))
    $insert = @'

        private static void ThrowIfMigrationFault(string phase)
        {
            string requested = Environment.GetEnvironmentVariable("CK3MPS_TEST_MIGRATION_FAULT");
            if (String.Equals(requested, phase, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Injected state migration fault at phase: " + phase);
        }
'@
    $transactional = $transactional.Replace("`n        private static void ValidateRoots", $insert + "`n        private static void ValidateRoots")
    Set-NormalizedText 'source/TransactionalOperations.cs' $transactional
}

Write-Host 'Runtime hardening remediation applied.' -ForegroundColor Green
