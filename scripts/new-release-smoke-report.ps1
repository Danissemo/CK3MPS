[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$CommitSha,
    [string]$ReleaseCommitSha,
    [string]$EnvironmentName = 'Windows release smoke environment',
    [string]$Tester = $env:GITHUB_ACTOR,
    [string]$EvidenceRoot = 'TBD',
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$TemplatePath = Join-Path $Root 'templates\release-smoke-report.md'

if (-not (Test-Path -LiteralPath $TemplatePath -PathType Leaf)) {
    throw "Release smoke report template is missing: $TemplatePath"
}

if ([string]::IsNullOrWhiteSpace($ReleaseCommitSha)) {
    $ReleaseCommitSha = $CommitSha
}
if ([string]::IsNullOrWhiteSpace($Tester)) {
    $Tester = 'TBD'
}

function Assert-CommitSha {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Value)
    if ($Value -notmatch '^[0-9a-fA-F]{7,40}$') {
        throw "$Name must be a Git commit SHA, got '$Value'."
    }
}

Assert-CommitSha -Name 'CommitSha' -Value $CommitSha
Assert-CommitSha -Name 'ReleaseCommitSha' -Value $ReleaseCommitSha

$AutomatedChecks = @(
    @{ Id = 'AUTO-BUILD-01'; Category = 'release-integrity'; Status = 'TBD'; Evidence = 'GitHub Actions build artifact'; Notes = 'Build produces bin\\CK3MPS.exe and release assets.' },
    @{ Id = 'AUTO-TEST-01'; Category = 'release-integrity'; Status = 'TBD'; Evidence = 'test-script-diagnostics artifact'; Notes = 'test-all orchestrator passes.' },
    @{ Id = 'AUTO-STATIC-01'; Category = 'security'; Status = 'TBD'; Evidence = 'Strict mutation review log'; Notes = 'Static dangerous mutation guard passes.' },
    @{ Id = 'AUTO-META-01'; Category = 'release-integrity'; Status = 'TBD'; Evidence = 'validate-release.ps1 log'; Notes = 'Version/docs/package metadata align.' },
    @{ Id = 'AUTO-MATRIX-01'; Category = 'release-matrix'; Status = 'Pass'; Evidence = 'This generated report'; Notes = 'Generated for exact version and commit.' },
    @{ Id = 'AUTO-MATRIX-02'; Category = 'release-matrix'; Status = 'TBD'; Evidence = 'test-release-smoke-report.ps1 log'; Notes = 'Rejects missing mandatory fields.' },
    @{ Id = 'AUTO-MATRIX-03'; Category = 'release-matrix'; Status = 'TBD'; Evidence = 'test-release-smoke-report.ps1 log'; Notes = 'Rejects tested/release commit mismatch.' },
    @{ Id = 'AUTO-MATRIX-04'; Category = 'release-matrix'; Status = 'TBD'; Evidence = 'test-release-smoke-report.ps1 log'; Notes = 'Rejects failed mandatory security/restore/updater checks.' },
    @{ Id = 'AUTO-MATRIX-05'; Category = 'release-matrix'; Status = 'TBD'; Evidence = 'test-release-smoke-report.ps1 log'; Notes = 'Report evidence isolation is verified.' },
    @{ Id = 'AUTO-PORTABLE-01'; Category = 'install-run'; Status = 'TBD'; Evidence = 'test-all portable logs'; Notes = 'Portable migration fixtures pass.' },
    @{ Id = 'AUTO-RESTORE-01'; Category = 'restore'; Status = 'TBD'; Evidence = 'test-all restore logs'; Notes = 'Restore transaction fixtures pass.' },
    @{ Id = 'AUTO-WORKFLOW-01'; Category = 'workflow-parity'; Status = 'TBD'; Evidence = 'test-all workflow/parity logs'; Notes = 'Deterministic workflow/parity fixtures pass.' }
)

$ManualChecks = @(
    @{ Id = 'MAN-OS-01'; Category = 'os-profile'; Notes = 'Windows 10 x64 clean user profile.' },
    @{ Id = 'MAN-OS-02'; Category = 'os-profile'; Notes = 'Windows 11 x64 clean user profile.' },
    @{ Id = 'MAN-OS-03'; Category = 'os-profile'; Notes = 'Existing user profile with real CK3 data.' },
    @{ Id = 'MAN-INSTALL-01'; Category = 'install-run'; Notes = 'Clean install.' },
    @{ Id = 'MAN-INSTALL-02'; Category = 'install-run'; Notes = 'Upgrade from previous stable release.' },
    @{ Id = 'MAN-INSTALL-03'; Category = 'install-run'; Notes = 'Portable mode.' },
    @{ Id = 'MAN-INSTALL-04'; Category = 'install-run'; Notes = 'Non-portable mode.' },
    @{ Id = 'MAN-INSTALL-05'; Category = 'install-run'; Notes = 'Launch without admin.' },
    @{ Id = 'MAN-INSTALL-06'; Category = 'install-run'; Notes = 'UAC elevation accepted.' },
    @{ Id = 'MAN-INSTALL-07'; Category = 'install-run'; Notes = 'UAC elevation denied.' },
    @{ Id = 'MAN-INSTALL-08'; Category = 'install-run'; Notes = 'Read-only/restricted folder.' },
    @{ Id = 'MAN-INSTALL-09'; Category = 'install-run'; Notes = 'Long path and non-ASCII username/path.' },
    @{ Id = 'MAN-GAME-01'; Category = 'game-env'; Notes = 'Steam not installed.' },
    @{ Id = 'MAN-GAME-02'; Category = 'game-env'; Notes = 'Steam installed and closed.' },
    @{ Id = 'MAN-GAME-03'; Category = 'game-env'; Notes = 'Steam running.' },
    @{ Id = 'MAN-GAME-04'; Category = 'game-env'; Notes = 'CK3 not running.' },
    @{ Id = 'MAN-GAME-05'; Category = 'game-env'; Notes = 'CK3 running.' },
    @{ Id = 'MAN-GAME-06'; Category = 'game-env'; Notes = 'Paradox Launcher absent/damaged/running.' },
    @{ Id = 'MAN-GAME-07'; Category = 'game-env'; Notes = 'Missing user folders.' },
    @{ Id = 'MAN-GAME-08'; Category = 'game-env'; Notes = 'Existing real user data.' },
    @{ Id = 'MAN-NET-01'; Category = 'network'; Notes = 'One adapter.' },
    @{ Id = 'MAN-NET-02'; Category = 'network'; Notes = 'Multiple adapters.' },
    @{ Id = 'MAN-NET-03'; Category = 'network'; Notes = 'VPN enabled.' },
    @{ Id = 'MAN-NET-04'; Category = 'network'; Notes = 'Firewall allow.' },
    @{ Id = 'MAN-NET-05'; Category = 'network'; Notes = 'Firewall deny.' },
    @{ Id = 'MAN-NET-06'; Category = 'network'; Notes = 'DNS/network mutation failure.' },
    @{ Id = 'MAN-NET-07'; Category = 'network'; Notes = 'Offline mode.' },
    @{ Id = 'MAN-NET-08'; Category = 'network'; Notes = 'Slow/unstable connection.' },
    @{ Id = 'MAN-NET-09'; Category = 'network'; Notes = 'Online Parity Room across two networks.' },
    @{ Id = 'MAN-RESTORE-01'; Category = 'restore'; Notes = 'Successful restore.' },
    @{ Id = 'MAN-RESTORE-02'; Category = 'restore'; Notes = 'Failure before commit.' },
    @{ Id = 'MAN-RESTORE-03'; Category = 'restore'; Notes = 'Failure after partial commit.' },
    @{ Id = 'MAN-RESTORE-04'; Category = 'restore'; Notes = 'Rollback success.' },
    @{ Id = 'MAN-RESTORE-05'; Category = 'restore'; Notes = 'Rollback failure.' },
    @{ Id = 'MAN-RESTORE-06'; Category = 'restore'; Notes = 'App-owned restore point.' },
    @{ Id = 'MAN-RESTORE-07'; Category = 'restore'; Notes = 'Foreign restore point cannot be deleted/restored destructively.' },
    @{ Id = 'MAN-RESTORE-08'; Category = 'restore'; Notes = 'Damaged manifest.' },
    @{ Id = 'MAN-RESTORE-09'; Category = 'restore'; Notes = 'Interrupted portable migration.' },
    @{ Id = 'MAN-UPDATER-01'; Category = 'updater'; Notes = 'No updates.' },
    @{ Id = 'MAN-UPDATER-02'; Category = 'updater'; Notes = 'Successful update path.' },
    @{ Id = 'MAN-UPDATER-03'; Category = 'updater'; Notes = 'Checksum mismatch.' },
    @{ Id = 'MAN-UPDATER-04'; Category = 'updater'; Notes = 'Invalid signature.' },
    @{ Id = 'MAN-UPDATER-05'; Category = 'updater'; Notes = 'Blocked executable.' },
    @{ Id = 'MAN-UPDATER-06'; Category = 'updater'; Notes = 'Failed health check.' },
    @{ Id = 'MAN-UPDATER-07'; Category = 'updater'; Notes = 'Rollback updater.' },
    @{ Id = 'MAN-UPDATER-08'; Category = 'updater'; Notes = 'Portable and non-portable upgrade.' },
    @{ Id = 'MAN-UX-01'; Category = 'ux-stability'; Notes = 'Long scan.' },
    @{ Id = 'MAN-UX-02'; Category = 'ux-stability'; Notes = 'Cancel.' },
    @{ Id = 'MAN-UX-03'; Category = 'ux-stability'; Notes = 'Re-run.' },
    @{ Id = 'MAN-UX-04'; Category = 'ux-stability'; Notes = 'Close window during operation.' },
    @{ Id = 'MAN-UX-05'; Category = 'ux-stability'; Notes = 'No Live Log spam.' },
    @{ Id = 'MAN-UX-06'; Category = 'ux-stability'; Notes = 'Correct final verdict.' },
    @{ Id = 'MAN-UX-07'; Category = 'ux-stability'; Notes = 'High DPI.' },
    @{ Id = 'MAN-UX-08'; Category = 'ux-stability'; Notes = 'Display scaling.' },
    @{ Id = 'MAN-UX-09'; Category = 'ux-stability'; Notes = 'Minimum window size.' },
    @{ Id = 'MAN-UX-10'; Category = 'ux-stability'; Notes = 'Slow machine.' }
)

function ConvertTo-ReportRows {
    param([Parameter(Mandatory = $true)][array]$Rows)
    return (($Rows | ForEach-Object {
        $evidence = if ($_.ContainsKey('Evidence')) { $_.Evidence } else { 'TBD' }
        $status = if ($_.ContainsKey('Status')) { $_.Status } else { 'TBD' }
        '| {0} | Yes | {1} | {2} | {3} | {4} |' -f $_.Id, $_.Category, $status, $evidence, $_.Notes
    }) -join [Environment]::NewLine)
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $safeVersion = $Version.TrimStart('v') -replace '[^0-9A-Za-z_.-]', '-'
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputPath = Join-Path $Root (".artifacts\release-smoke-report-{0}-{1}.md" -f $safeVersion, $stamp)
}

$outputFullPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $Root $OutputPath }
$outputDir = Split-Path -Parent $outputFullPath
if (-not [string]::IsNullOrWhiteSpace($outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}

$template = Get-Content -LiteralPath $TemplatePath -Raw
$report = $template.
    Replace('{{VERSION}}', $Version).
    Replace('{{TESTED_COMMIT_SHA}}', $CommitSha).
    Replace('{{RELEASE_COMMIT_SHA}}', $ReleaseCommitSha).
    Replace('{{REPORT_DATE}}', (Get-Date).ToString('yyyy-MM-dd HH:mm:ss K')).
    Replace('{{ENVIRONMENT}}', $EnvironmentName).
    Replace('{{TESTER}}', $Tester).
    Replace('{{EVIDENCE_ROOT}}', $EvidenceRoot).
    Replace('{{AUTOMATED_ROWS}}', (ConvertTo-ReportRows -Rows $AutomatedChecks)).
    Replace('{{MANUAL_ROWS}}', (ConvertTo-ReportRows -Rows $ManualChecks)).
    Replace('{{KNOWN_LIMITATIONS}}', 'TBD').
    Replace('{{FINAL_DECISION}}', 'TBD').
    Replace('{{DECISION_OWNER}}', 'TBD').
    Replace('{{DECISION_NOTES}}', 'TBD')

Set-Content -LiteralPath $outputFullPath -Value $report -Encoding UTF8
Write-Host "Release smoke report created: $outputFullPath"
