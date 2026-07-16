$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Read-Normalized([string]$RelativePath) {
    $path = Join-Path $Root ($RelativePath -replace '/', '\')
    return ([IO.File]::ReadAllText($path)).Replace("`r`n", "`n")
}

function Write-Normalized([string]$RelativePath, [string]$Text) {
    $path = Join-Path $Root ($RelativePath -replace '/', '\')
    [IO.File]::WriteAllText($path, $Text.Replace("`r`n", "`n"), $Utf8NoBom)
}

function Replace-Exact([string]$RelativePath, [string]$OldText, [string]$NewText, [string]$UniqueNewMarker) {
    $text = Read-Normalized $RelativePath
    $old = $OldText.Replace("`r`n", "`n")
    $new = $NewText.Replace("`r`n", "`n")
    if ($text.Contains($UniqueNewMarker.Replace("`r`n", "`n"))) {
        return
    }
    $first = $text.IndexOf($old, [StringComparison]::Ordinal)
    if ($first -lt 0) {
        throw "Expected block was not found in $RelativePath"
    }
    if ($text.IndexOf($old, $first + $old.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "Expected block was not unique in $RelativePath"
    }
    Write-Normalized $RelativePath ($text.Substring(0, $first) + $new + $text.Substring($first + $old.Length))
}

Replace-Exact 'source/Workflow.cs' @'
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
'@ @'
        private string BuildWorkflowScenarioSummaryText(string scenario, List<WorkflowStepState> states)
        {
            WorkflowAnalysisSnapshot analysis = CurrentWorkflowAnalysis();
'@

Replace-Exact 'source/Workflow.cs' @'
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
'@ @'
        private string BuildWorkflowRecommendation(string scenario, List<WorkflowStepState> states, HostSuitabilityResult host, HostSaveCandidateResult save)
        {
            WorkflowAnalysisSnapshot analysis = CurrentWorkflowAnalysis();
'@

Replace-Exact 'scripts/check-static-danger.ps1' @'
$ExcludedRelativePaths = @(
    "scripts/check-static-danger.ps1"
)
'@ @'
$ExcludedRelativePaths = @(
    "scripts/check-static-danger.ps1",
    "scripts/apply-runtime-hardening.ps1",
    "scripts/apply-runtime-hardening-fixes.ps1"
)
'@ '"scripts/apply-runtime-hardening-fixes.ps1"'

Replace-Exact 'scripts/check-static-danger.ps1' @'
    @{ Path = "source/Updates.cs"; Rule = "Process.Start"; LinePattern = '^Process\.Start\(info\);$'; Reason = "Opens only the vetted official GitHub release page; automatic installation is disabled." },
    @{ Path = "source/Utilities.cs"; Rule = "File.Move"; LinePattern = '^File\.Move\((tempPath, targetPath|source, destination)\);$'; Reason = "SafeAtomicFile commit and bounded history rotation use same-directory rename." },
'@ @'
    @{ Path = "source/Updates.cs"; Rule = "Process.Start"; LinePattern = '^Process\.Start\(info\);$'; Reason = "Opens only the vetted official GitHub release page; automatic installation is disabled." },
    @{ Path = "source/TransactionalOperations.cs"; Rule = "File.Copy"; LinePattern = '^File\.Copy\(source, staged, false\);$'; Reason = "Copies validated app-owned state into a transaction staging tree." },
    @{ Path = "source/TransactionalOperations.cs"; Rule = "File.Move"; LinePattern = '^File\.Move\(staged, target\);$'; Reason = "Commits a verified staged state file without overwrite." },
    @{ Path = "source/TransactionalOperations.cs"; Rule = "File.Delete"; LinePattern = '^File\.Delete\((target|source|path)\);$'; Reason = "Rolls back created targets, cleans committed source files, or removes app-owned journals." },
    @{ Path = "source/TransactionalOperations.cs"; Rule = "Directory.Delete"; LinePattern = '^Directory\.Delete\((path, (true|false)|current, false)\);$'; Reason = "Cleans only validated staging or empty app-state directories." },
    @{ Path = "source/RestoreTransactions.cs"; Rule = "File.Copy"; LinePattern = '^File\.Copy\((manifest, manifestBackup|normalized, record\.BackupPath|backupPath, temp), false\);$'; Reason = "Captures and verifies rollback snapshots inside the app-owned transaction root." },
    @{ Path = "source/RestoreTransactions.cs"; Rule = "File.Delete"; LinePattern = '^File\.Delete\((target|manifest)\);$'; Reason = "Removes only revalidated restore targets or a newly-created manifest during rollback." },
    @{ Path = "source/RestoreTransactions.cs"; Rule = "Directory.Move"; LinePattern = '^Directory\.Move\((target, rollback|stage, target|rollback, target)\);$'; Reason = "Uses same-parent renames for atomic directory commit and rollback." },
    @{ Path = "source/RestoreTransactions.cs"; Rule = "Directory.Delete"; LinePattern = '^Directory\.Delete\((target|rollback|stage|path), true\);$|^Directory\.Delete\(parent, false\);$'; Reason = "Cleans validated restore staging, rollback, or empty transaction directories." },
    @{ Path = "source/Utilities.cs"; Rule = "File.Move"; LinePattern = '^File\.Move\((tempPath, targetPath|source, destination)\);$'; Reason = "SafeAtomicFile commit and bounded history rotation use same-directory rename." },
'@ 'source/TransactionalOperations.cs'

$workflow = Read-Normalized 'source/Workflow.cs'
$requiredMarkers = @(
    'BuildWorkflowScenarioSnapshotCore(scenario, cancellationToken)',
    'WorkflowRefreshStillCurrent(generation, scenario, cancellationToken)',
    'HostSuitabilityResult host = analysis.Host;',
    'OosDeepInsight oos = CurrentWorkflowAnalysis().Oos;',
    'return BuildWorkflowScenarioSnapshotCore(currentWorkflowScenario, CancellationToken.None).Summary;',
    'string advertisedAddress = DetectPrimaryIpv4Address();'
)
foreach ($marker in $requiredMarkers) {
    if (-not $workflow.Contains($marker)) {
        throw "Runtime hardening marker is missing from Workflow.cs: $marker"
    }
}

Write-Host 'Workflow and static-safety remediation completed.' -ForegroundColor Green
