$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Get-Content (Join-Path $root 'CK3MPS.csproj') -Raw
$runtime = Get-Content (Join-Path $root 'source\WorkflowRuntimeFixes.cs') -Raw
$large = Get-Content (Join-Path $root 'source\WorkflowLargeSaveScoreFix.cs') -Raw
$binding = Get-Content (Join-Path $root 'source\WorkflowLargeSaveHandlerBindingFix.cs') -Raw

if ($project -notmatch 'WorkflowLargeSaveHandlerBindingFix\.cs') {
    throw 'Large save handler binding fix is not compiled.'
}

if ($runtime -notmatch 'RunWorkflowSaveAndHostFix\(\)') {
    throw 'Base workflow fix entry point is missing.'
}

if ($large -notmatch 'RunWorkflowSaveAndHostFixWithLargeSaveScoreRepair\(\)') {
    throw 'Large save repair coordinator is missing.'
}

if ($large -notmatch 'TryForceWorkflowHostSaveIntoLargeSafeBaseline') {
    throw 'Large save repair implementation is missing.'
}

if ($binding -notmatch 'Shown \+= delegate \{ QueueLargeSaveBindingRefresh\(\); \}') {
    throw 'Binding watcher does not rebind after the form Shown lifecycle.'
}

if ($binding -notmatch 'BeginInvoke\(\(MethodInvoker\)delegate[\s\S]*BeginInvoke\(\(MethodInvoker\)delegate') {
    throw 'Binding watcher must defer twice so later legacy reconfiguration cannot overwrite the large-save handler.'
}

if ($binding -notmatch 'ConfigureLargeSaveScoreWorkflowFixHandler\(\)') {
    throw 'Binding watcher does not activate the large-save handler.'
}

Write-Host 'Workflow large-save handler binding checks passed.'
