Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$MainWindowPath = Join-Path $Root 'source\MainWindow.cs'

if (-not (Test-Path -LiteralPath $MainWindowPath -PathType Leaf)) {
    throw "Missing MainWindow source: $MainWindowPath"
}

$text = Get-Content -LiteralPath $MainWindowPath -Raw
$changed = $false

$marker = 'FixOperationResult fixResult = FixOperationResultEvaluator.Evaluate('
if ($text.IndexOf($marker, [System.StringComparison]::Ordinal) -ge 0) {
    Write-Host 'Strict fix result contract patch already applied.'
    return
}

$oldBefore = @'
                bool shouldStartGuard = IsStepChecked(StepCatalog.ForceNoMods) || IsStepChecked(StepCatalog.StabilizePdxSettings) || IsStepChecked(StepCatalog.ConfirmLaunchedProfile);
                CaptureExecutionSnapshot();
'@
$newBefore = @'
                bool shouldStartGuard = IsStepChecked(StepCatalog.ForceNoMods) || IsStepChecked(StepCatalog.StabilizePdxSettings) || IsStepChecked(StepCatalog.ConfirmLaunchedProfile);
                int readinessFailuresBeforeApply = lastReadinessFailures;
                CaptureExecutionSnapshot();
'@

if ($text.IndexOf($oldBefore, [System.StringComparison]::Ordinal) -lt 0) {
    throw 'Could not locate readiness-before capture insertion point in MainWindow.cs.'
}
$text = $text.Replace($oldBefore, $newBefore)
$changed = $true

$oldOutcome = @'
                if (readinessFailures == 0)
                {
                    SetStatusText("Done. CK3 profile is prepared for stable vanilla multiplayer.");
                    Log("Done. Use host local save, no hotjoin, speed 1-2 after load.");
                    historyResult = "ready";
                }
                else
                {
                    SetStatusText("Completed with blockers. Fix failed readiness checks before serious MP.");
                    Log("RESULT Completed with blockers. Fix failed readiness checks before serious MP.");
                    historyResult = "completed_with_blockers";
                }
'@
$newOutcome = @'
                List<int> targetCheckIds = BuildSelectedFixTargetCheckIds();
                List<int> remainingTargetFailures = CollectFailedFixTargetCheckIds(targetCheckIds, true);
                FixReadinessSnapshot readinessBefore = new FixReadinessSnapshot();
                readinessBefore.FailedChecks = readinessFailuresBeforeApply;
                readinessBefore.Verdict = readinessFailuresBeforeApply == 0 ? "READY" : "NOT READY";
                FixReadinessSnapshot readinessAfter = new FixReadinessSnapshot();
                readinessAfter.FailedChecks = readinessFailures;
                readinessAfter.Verdict = readinessFailures == 0 ? "READY" : "NOT READY";
                FixOperationResult fixResult = FixOperationResultEvaluator.Evaluate(
                    "stabilize",
                    targetCheckIds,
                    remainingTargetFailures,
                    readinessBefore,
                    readinessAfter,
                    true,
                    true,
                    true,
                    new string[0],
                    BuildFailedPostconditionMessages(remainingTargetFailures),
                    "not_required",
                    false,
                    false,
                    new string[0],
                    "Apply Settings final verification after current-state rescan.");
                LogFixResultDetails(fixResult);

                if (fixResult.IsFinalSuccess)
                {
                    SetStatusText("Done. CK3 profile is prepared for stable vanilla multiplayer.");
                    Log("Done. Use host local save, no hotjoin, speed 1-2 after load.");
                    historyResult = "ready";
                }
                else if (fixResult.Status == FixOperationStatus.PartiallySucceeded)
                {
                    SetStatusText("Target fix complete; unrelated readiness blockers remain.");
                    historyResult = "target_ready_with_unrelated_blockers";
                }
                else
                {
                    SetStatusText("Fix failed verification. Target postconditions still need attention.");
                    historyResult = "failed_target_postconditions";
                }
'@

if ($text.IndexOf($oldOutcome, [System.StringComparison]::Ordinal) -lt 0) {
    throw 'Could not locate old stabilize outcome block in MainWindow.cs.'
}
$text = $text.Replace($oldOutcome, $newOutcome)
$changed = $true

if ($changed) {
    Set-Content -LiteralPath $MainWindowPath -Value $text -Encoding UTF8
    Write-Host 'Applied strict fix result contract patch to MainWindow.cs.'
}
