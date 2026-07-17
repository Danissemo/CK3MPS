Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('ck3mps-release-smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $TempRoot | Out-Null

$Generator = Join-Path $ScriptDir 'new-release-smoke-report.ps1'
$Validator = Join-Path $ScriptDir 'validate-release-smoke-report.ps1'
$CommitA = '0123456789abcdef0123456789abcdef01234567'
$CommitB = 'fedcba9876543210fedcba9876543210fedcba98'

function Invoke-ExpectFailure {
    param([Parameter(Mandatory = $true)][scriptblock]$Script, [Parameter(Mandatory = $true)][string]$Name)
    try {
        & $Script
    }
    catch {
        Write-Host "Expected failure observed: $Name"
        return
    }
    throw "Expected failure but command succeeded: $Name"
}

function Complete-DraftReport {
    param([Parameter(Mandatory = $true)][string]$Path)

    $text = Get-Content -LiteralPath $Path -Raw
    $text = $text -replace '\| (AUTO|MAN)-([A-Z]+)-([0-9]+) \| Yes \| ([^|]+) \| TBD \| TBD \| ([^|]+) \|', '| $1-$2-$3 | Yes | $4 | Pass | evidence://$1-$2-$3 | $5 |'
    $text = $text -replace '\| (AUTO|MAN)-([A-Z]+)-([0-9]+) \| Yes \| ([^|]+) \| TBD \| ([^|]+) \| ([^|]+) \|', '| $1-$2-$3 | Yes | $4 | Pass | $5 | $6 |'
    $text = $text.Replace('- TBD', '- None')
    $text = $text.Replace('| Decision | TBD |', '| Decision | Ready |')
    $text = $text.Replace('| Decision owner | TBD |', '| Decision owner | test-runner |')
    $text = $text.Replace('| Decision notes | TBD |', '| Decision notes | All mandatory evidence present. |')
    Set-Content -LiteralPath $Path -Value $text -Encoding UTF8
}

try {
    $reportA = Join-Path $TempRoot 'report-a.md'
    $reportB = Join-Path $TempRoot 'report-b.md'

    & $Generator -Version 'v0.0-test' -CommitSha $CommitA -ReleaseCommitSha $CommitA -EnvironmentName 'CI smoke' -Tester 'test-runner' -EvidenceRoot 'artifact://root' -OutputPath $reportA
    & $Generator -Version 'v0.0-test' -CommitSha $CommitA -ReleaseCommitSha $CommitA -EnvironmentName 'CI smoke' -Tester 'test-runner' -EvidenceRoot 'artifact://root' -OutputPath $reportB

    if (-not (Test-Path -LiteralPath $reportA -PathType Leaf)) { throw 'First generated report is missing.' }
    if (-not (Test-Path -LiteralPath $reportB -PathType Leaf)) { throw 'Second generated report is missing.' }
    if ((Resolve-Path $reportA).Path -eq (Resolve-Path $reportB).Path) { throw 'Report rerun mixed evidence by using the same path.' }

    Invoke-ExpectFailure -Name 'draft report is incomplete' -Script { & $Validator -ReportPath $reportA -ExpectedVersion 'v0.0-test' -ExpectedCommitSha $CommitA }

    Complete-DraftReport -Path $reportA
    & $Validator -ReportPath $reportA -ExpectedVersion 'v0.0-test' -ExpectedCommitSha $CommitA

    $missingVersion = Join-Path $TempRoot 'missing-version.md'
    Copy-Item -LiteralPath $reportA -Destination $missingVersion
    (Get-Content -LiteralPath $missingVersion -Raw).Replace('| Version | v0.0-test |', '| Version | TBD |') | Set-Content -LiteralPath $missingVersion -Encoding UTF8
    Invoke-ExpectFailure -Name 'missing version blocks release' -Script { & $Validator -ReportPath $missingVersion -ExpectedVersion 'v0.0-test' -ExpectedCommitSha $CommitA }

    $mismatch = Join-Path $TempRoot 'mismatch.md'
    Copy-Item -LiteralPath $reportA -Destination $mismatch
    (Get-Content -LiteralPath $mismatch -Raw).Replace("| Release commit SHA | $CommitA |", "| Release commit SHA | $CommitB |") | Set-Content -LiteralPath $mismatch -Encoding UTF8
    Invoke-ExpectFailure -Name 'commit mismatch blocks release' -Script { & $Validator -ReportPath $mismatch -ExpectedVersion 'v0.0-test' -ExpectedCommitSha $CommitA }

    $failedSecurity = Join-Path $TempRoot 'failed-security.md'
    Copy-Item -LiteralPath $reportA -Destination $failedSecurity
    (Get-Content -LiteralPath $failedSecurity -Raw).Replace('| AUTO-STATIC-01 | Yes | security | Pass | Strict mutation review log | Static dangerous mutation guard passes. |', '| AUTO-STATIC-01 | Yes | security | Fail | Strict mutation review log | Static dangerous mutation guard failed. |') | Set-Content -LiteralPath $failedSecurity -Encoding UTF8
    Invoke-ExpectFailure -Name 'failed mandatory security blocks release' -Script { & $Validator -ReportPath $failedSecurity -ExpectedVersion 'v0.0-test' -ExpectedCommitSha $CommitA }

    $blockedNoReason = Join-Path $TempRoot 'blocked-no-reason.md'
    Copy-Item -LiteralPath $reportA -Destination $blockedNoReason
    (Get-Content -LiteralPath $blockedNoReason -Raw).Replace('| MAN-NET-09 | Yes | network | Pass | evidence://MAN-NET-09 | Online Parity Room across two networks. |', '| MAN-NET-09 | Yes | network | Blocked | artifact://ticket-1 | TBD |') | Set-Content -LiteralPath $blockedNoReason -Encoding UTF8
    Invoke-ExpectFailure -Name 'blocked without reason blocks release' -Script { & $Validator -ReportPath $blockedNoReason -ExpectedVersion 'v0.0-test' -ExpectedCommitSha $CommitA }

    Write-Host 'Release smoke report self-tests passed.' -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
