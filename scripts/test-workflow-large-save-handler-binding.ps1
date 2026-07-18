$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Get-Content (Join-Path $root 'CK3MPS.csproj') -Raw
$runtime = Get-Content (Join-Path $root 'source\WorkflowRuntimeFixes.cs') -Raw
$large = Get-Content (Join-Path $root 'source\WorkflowLargeSaveScoreFix.cs') -Raw
$binding = Get-Content (Join-Path $root 'source\WorkflowLargeSaveHandlerBindingFix.cs') -Raw
$nonBlocking = Get-Content (Join-Path $root 'source\WorkflowSaveHostNonBlockingFix.cs') -Raw
$scoreVerified = Get-Content (Join-Path $root 'source\WorkflowScoreVerifiedSaveHostFix.cs') -Raw

if ($project -notmatch 'WorkflowLargeSaveHandlerBindingFix\.cs') { throw 'Large save handler binding fix is not compiled.' }
if ($project -notmatch 'WorkflowSaveHostNonBlockingFix\.cs') { throw 'Non-blocking save + host fix is not compiled.' }
if ($project -notmatch 'WorkflowScoreVerifiedSaveHostFix\.cs') { throw 'Score-verified save + host fix is not compiled.' }
if ($runtime -notmatch 'RunWorkflowSaveAndHostFix\(\)') { throw 'Base workflow fix entry point is missing.' }
if ($large -notmatch 'TryForceWorkflowHostSaveIntoLargeSafeBaseline') { throw 'Guarded large-save repair implementation is missing.' }
if ($nonBlocking -notmatch 'private async void RunWorkflowSaveAndHostFixNonBlocking\(\)') { throw 'Workflow button does not have an async non-blocking coordinator.' }
if ($nonBlocking -match 'PrepareWorkflowSaveSurgeryBaseline\(\)') { throw 'Fix save + host must not perform the redundant full-save surgery copy/hash pass.' }
if ($nonBlocking -match 'MarkSaveRelatedBlockersManual\(') { throw 'Save repair failures must remain real blockers instead of being relabeled Manual.' }
if ($scoreVerified -notmatch 'ConfigureScoreVerifiedSaveHostFixHandler') { throw 'Score-verified handler is missing.' }
if ($scoreVerified -notmatch 'RunWorkflowScoreVerifiedSaveHostFix') { throw 'Score-verified wrapper is missing.' }
if ($scoreVerified -notmatch 'TryPublishScoreVerifiedSelectedSaveBaseline') { throw 'Score-verified selected save baseline publisher is missing.' }
if ($scoreVerified -notmatch 'BuildScoreSafeGameRulesOverlay') { throw 'Score-verified repair does not create a safe game_rules overlay fallback.' }
if ($scoreVerified -notmatch 'ForceCriticalRuleAssignmentsForScore') { throw 'Score-verified repair does not force direct rule assignments used by score checks.' }
if ($scoreVerified -notmatch 'WorkflowSaveScoreRuleDiagnostics') { throw 'Score-verified repair does not preserve rule diagnostics after post-check.' }
if ($scoreVerified -notmatch 'RunWorkflowSaveAndHostFixNonBlocking\(\)') { throw 'Score-verified wrapper does not continue into the non-blocking host workflow.' }
if ($binding -notmatch 'Shown \+= delegate \{ QueueLargeSaveBindingRefresh\(\); \}') { throw 'Binding watcher does not rebind after the form Shown lifecycle.' }
if ($binding -notmatch 'Activated \+= delegate \{ QueueLargeSaveBindingRefresh\(\); \}') { throw 'Binding watcher does not recover after legacy OnActivated rebinding.' }
if ($binding -notmatch 'MouseDown \+= delegate \{ ConfigureScoreVerifiedSaveHostFixHandler\(\); \}') { throw 'Mouse activation cannot synchronously replace a stale legacy click handler with the score-verified handler.' }
if ($binding -notmatch 'KeyDown \+= delegate') { throw 'Keyboard activation cannot synchronously replace a stale legacy click handler.' }
if ($binding -notmatch 'BeginInvoke\(\(MethodInvoker\)delegate[\s\S]*BeginInvoke\(\(MethodInvoker\)delegate') { throw 'Binding watcher must defer twice so later legacy reconfiguration cannot overwrite the active handler.' }
if ($binding -notmatch 'ConfigureScoreVerifiedSaveHostFixHandler\(\)') { throw 'Binding watcher does not activate the score-verified handler.' }

Write-Host 'Workflow score-verified non-blocking save host binding checks passed.'
