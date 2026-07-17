Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$ContractPath = Join-Path $Root 'source\FixResultContract.cs'
$ProjectPath = Join-Path $Root 'CK3MPS.csproj'

if (-not (Test-Path -LiteralPath $ContractPath -PathType Leaf)) {
    throw "Missing fix result contract source file: $ContractPath"
}

$contract = Get-Content -LiteralPath $ContractPath -Raw
$project = Get-Content -LiteralPath $ProjectPath -Raw

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Needle,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if ($Text.IndexOf($Needle, [System.StringComparison]::Ordinal) -lt 0) {
        throw $Message
    }
}

foreach ($status in @('Succeeded', 'PartiallySucceeded', 'Failed', 'Unsupported', 'Cancelled')) {
    Assert-Contains $contract $status "FixOperationStatus is missing required state: $status"
}

foreach ($field in @(
    'OperationId',
    'ChangedElements',
    'Preconditions',
    'Postconditions',
    'FailedPostconditions',
    'ReadinessBefore',
    'ReadinessAfter',
    'RollbackStatus',
    'UserMessage',
    'DiagnosticDetails',
    'MutationSucceeded',
    'VerificationSucceeded',
    'RemainingTargetFailedCheckIds')) {
    Assert-Contains $contract $field "FixOperationResult is missing required contract member: $field"
}

Assert-Contains $project 'source\FixResultContract.cs' 'CK3MPS.csproj does not compile the strict fix result contract.'

# Regression: apply without exception but target check remains failed must never be final success.
Assert-Contains $contract 'RemainingTargetFailedCheckIds.Count > 0' 'Target failed checks must force a failed result.'
Assert-Contains $contract 'Fix mutation completed, but target postconditions are still failing.' 'Missing user-facing message for failed target postconditions.'
Assert-Contains $contract 'RESULT| FAILED' 'Failed target postconditions must produce RESULT| FAILED.'

# Regression: rescan failure must fail the operation even when mutation succeeded.
Assert-Contains $contract '!rescanSucceeded' 'Verification rescan failure is not handled.'
Assert-Contains $contract 'verification rescan failed' 'Missing diagnostic message for rescan failure.'

# Regression: target checks fixed but unrelated checks remain failed must be partial, not full failure or false OK.
Assert-Contains $contract 'FixOperationStatus.PartiallySucceeded' 'Partial success state is missing.'
Assert-Contains $contract 'unrelated readiness checks still block full READY' 'Missing distinction for unrelated readiness failures.'

# Regression: rollback, cancel, unsupported and diagnostic details must remain visible.
Assert-Contains $contract 'RollbackStatus' 'Rollback status must be carried in the result contract.'
Assert-Contains $contract 'FixOperationStatus.Cancelled' 'Cancellation state must be explicit.'
Assert-Contains $contract 'FixOperationStatus.Unsupported' 'Unsupported state must be explicit.'
Assert-Contains $contract 'DiagnosticDetails' 'Diagnostic details must be retained.'

# Regression: final success must require verification and no failed postconditions.
Assert-Contains $contract 'VerificationSucceeded && FailedPostconditions.Count == 0 && RemainingTargetFailedCheckIds.Count == 0' 'Final success must require verification success and clean target postconditions.'

Write-Host 'PASS strict fix result contract regression checks'
