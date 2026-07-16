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
    if ($text.Contains($UniqueNewMarker)) {
        return
    }
    $first = $text.IndexOf($old, [StringComparison]::Ordinal)
    if ($first -lt 0) {
        throw "Expected workflow block was not found in $RelativePath"
    }
    if ($text.IndexOf($old, $first + $old.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "Expected workflow block was not unique in $RelativePath"
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
'@ 'private string BuildWorkflowScenarioSummaryText(string scenario, List<WorkflowStepState> states)
        {
            WorkflowAnalysisSnapshot analysis = CurrentWorkflowAnalysis();'

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
'@ 'private string BuildWorkflowRecommendation(string scenario, List<WorkflowStepState> states, HostSuitabilityResult host, HostSaveCandidateResult save)
        {
            WorkflowAnalysisSnapshot analysis = CurrentWorkflowAnalysis();'

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

Write-Host 'Workflow snapshot remediation completed.' -ForegroundColor Green
