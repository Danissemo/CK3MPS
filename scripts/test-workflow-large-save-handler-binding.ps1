$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Get-Content (Join-Path $root 'CK3MPS.csproj') -Raw
$runtime = Get-Content (Join-Path $root 'source\WorkflowRuntimeFixes.cs') -Raw
$large = Get-Content (Join-Path $root 'source\WorkflowLargeSaveScoreFix.cs') -Raw
$binding = Get-Content (Join-Path $root 'source\WorkflowLargeSaveHandlerBindingFix.cs') -Raw
$nonBlocking = Get-Content (Join-Path $root 'source\WorkflowSaveHostNonBlockingFix.cs') -Raw
$scoreOverlay = Get-Content (Join-Path $root 'source\WorkflowAnalyzerOverlaySaveFix.cs') -Raw

if ($project -notmatch 'WorkflowLargeSaveHandlerBindingFix\.cs') { throw 'Large save handler binding fix is not compiled.' }
if ($project -notmatch 'WorkflowSaveHostNonBlockingFix\.cs') { throw 'Non-blocking save + host fix is not compiled.' }
if ($project -notmatch 'WorkflowAnalyzerOverlaySaveFix\.cs') { throw 'Analyzer overlay save fix is not compiled.' }
if ($runtime -notmatch 'RunWorkflowSaveAndHostFix\(\)') { throw 'Base workflow fix entry point is missing.' }
if ($large -notmatch 'TryForceWorkflowHostSaveIntoLargeSafeBaseline') { throw 'Guarded large-save repair implementation is missing.' }
if ($nonBlocking -notmatch 'private async void RunWorkflowSaveAndHostFixNonBlocking\(\)') { throw 'Workflow button does not have an async non-blocking coordinator.' }
if ($nonBlocking -notmatch 'await TryForceWorkflowHostSaveIntoLargeSafeBaselineNonBlockingAsync') { throw 'Non-blocking coordinator does not await the save repair phase.' }
if ($scoreOverlay -notmatch 'ConfigureAnalyzerOverlaySaveHostFixHandler') { throw 'Analyzer overlay handler is missing.' }
if ($scoreOverlay -notmatch 'RewriteAnalyzerVisibleGameRulesBlock') { throw 'Analyzer-visible game_rules rewrite is missing.' }
if ($scoreOverlay -notmatch 'BuildAnalyzerVisibleSafeGameRulesBlock') { throw 'Analyzer-visible safe game_rules block is missing.' }
if ($scoreOverlay -notmatch 'FindMatchingBrace') { throw 'Analyzer overlay rewrite does not replace the exact first game_rules brace block.' }
if ($scoreOverlay -notmatch 'RunWorkflowSaveAndHostFixNonBlocking\(\)') { throw 'Analyzer overlay pre-repair does not hand off to the non-blocking host workflow.' }
if ($scoreOverlay -notmatch 'Task\.Run') { throw 'Analyzer overlay rewrite is not moved away from the WinForms UI thread.' }
if ($nonBlocking -notmatch 'CompressionLevel\.Fastest') { throw 'Large save repair is not using the bounded fast recompression path.' }
if ($nonBlocking -match 'PrepareWorkflowSaveSurgeryBaseline\(\)') { throw 'Fix save + host must not perform the redundant full-save surgery copy/hash pass.' }
if ($nonBlocking -match 'MarkSaveRelatedBlockersManual\(') { throw 'Save repair failures must remain real blockers instead of being relabeled Manual.' }
if ($binding -notmatch 'Shown \+= delegate \{ QueueLargeSaveBindingRefresh\(\); \}') { throw 'Binding watcher does not rebind after the form Shown lifecycle.' }
if ($binding -notmatch 'Activated \+= delegate \{ QueueLargeSaveBindingRefresh\(\); \}') { throw 'Binding watcher does not recover after legacy OnActivated rebinding.' }
if ($binding -notmatch 'MouseDown \+= delegate \{ ConfigureAnalyzerOverlaySaveHostFixHandler\(\); \}') { throw 'Mouse activation cannot synchronously replace a stale legacy click handler.' }
if ($binding -notmatch 'KeyDown \+= delegate') { throw 'Keyboard activation cannot synchronously replace a stale legacy click handler.' }
if ($binding -notmatch 'BeginInvoke\(\(MethodInvoker\)delegate[\s\S]*BeginInvoke\(\(MethodInvoker\)delegate') { throw 'Binding watcher must defer twice so later legacy reconfiguration cannot overwrite the active handler.' }
if ($binding -notmatch 'ConfigureAnalyzerOverlaySaveHostFixHandler\(\)') { throw 'Binding watcher does not activate the analyzer-overlay handler.' }

Write-Host 'Workflow analyzer-overlay non-blocking large-save handler checks passed.'
