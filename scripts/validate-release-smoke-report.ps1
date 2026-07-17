[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ReportPath,
    [string]$ExpectedVersion,
    [string]$ExpectedCommitSha,
    [switch]$AllowDraft
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$fullPath = Resolve-Path -LiteralPath $ReportPath -ErrorAction Stop
$text = Get-Content -LiteralPath $fullPath -Raw
$errors = New-Object System.Collections.Generic.List[string]

function Add-ValidationError([string]$Message) {
    $script:errors.Add($Message) | Out-Null
}

function Get-MetadataValue([string]$Name) {
    $pattern = '(?m)^\|\s*' + [regex]::Escape($Name) + '\s*\|\s*(.*?)\s*\|\s*$'
    $match = [regex]::Match($script:text, $pattern)
    if (-not $match.Success) { return $null }
    return $match.Groups[1].Value.Trim()
}

function Test-Placeholder([string]$Value) {
    return [string]::IsNullOrWhiteSpace($Value) -or $Value.Trim() -in @('TBD', '{{VERSION}}', '{{TESTED_COMMIT_SHA}}', '{{RELEASE_COMMIT_SHA}}', '{{REPORT_DATE}}', '{{ENVIRONMENT}}', '{{TESTER}}', '{{EVIDENCE_ROOT}}')
}

$version = Get-MetadataValue 'Version'
$testedCommit = Get-MetadataValue 'Tested commit SHA'
$releaseCommit = Get-MetadataValue 'Release commit SHA'
$reportDate = Get-MetadataValue 'Report date'
$environment = Get-MetadataValue 'Environment'
$tester = Get-MetadataValue 'Tester'
$evidenceRoot = Get-MetadataValue 'Evidence root'
$decision = Get-MetadataValue 'Decision'
$decisionOwner = Get-MetadataValue 'Decision owner'
$decisionNotes = Get-MetadataValue 'Decision notes'

foreach ($pair in @(
    @{ Name = 'Version'; Value = $version },
    @{ Name = 'Tested commit SHA'; Value = $testedCommit },
    @{ Name = 'Release commit SHA'; Value = $releaseCommit },
    @{ Name = 'Report date'; Value = $reportDate },
    @{ Name = 'Environment'; Value = $environment },
    @{ Name = 'Tester'; Value = $tester },
    @{ Name = 'Evidence root'; Value = $evidenceRoot },
    @{ Name = 'Decision'; Value = $decision },
    @{ Name = 'Decision owner'; Value = $decisionOwner },
    @{ Name = 'Decision notes'; Value = $decisionNotes }
)) {
    if (Test-Placeholder $pair.Value) {
        Add-ValidationError "$($pair.Name) is missing or still TBD."
    }
}

if ($ExpectedVersion -and $version -ne $ExpectedVersion) {
    Add-ValidationError "Version mismatch: expected $ExpectedVersion but report has $version."
}

foreach ($pair in @(
    @{ Name = 'Tested commit SHA'; Value = $testedCommit },
    @{ Name = 'Release commit SHA'; Value = $releaseCommit }
)) {
    if ($pair.Value -and $pair.Value -notmatch '^[0-9a-fA-F]{7,40}$') {
        Add-ValidationError "$($pair.Name) is not a valid Git SHA: $($pair.Value)."
    }
}

if ($ExpectedCommitSha -and $releaseCommit -ne $ExpectedCommitSha) {
    Add-ValidationError "Release commit mismatch: expected $ExpectedCommitSha but report has $releaseCommit."
}

if ($testedCommit -and $releaseCommit -and $testedCommit -ne $releaseCommit) {
    Add-ValidationError "Tested commit SHA must match release commit SHA. Tested=$testedCommit Release=$releaseCommit."
}

if ($decision -and $decision -ne 'Ready') {
    Add-ValidationError "Final release decision must be Ready, got '$decision'."
}

$mandatoryRows = New-Object System.Collections.Generic.List[object]
foreach ($line in ($text -split "`r?`n")) {
    if ($line -notmatch '^\|\s*(AUTO|MAN)-[A-Z]+-\d+\s*\|') { continue }
    $cells = $line.Trim('|').Split('|') | ForEach-Object { $_.Trim() }
    if ($cells.Count -lt 6) {
        Add-ValidationError "Malformed matrix row: $line"
        continue
    }

    $mandatoryRows.Add([pscustomobject]@{
        Id = $cells[0]
        Mandatory = $cells[1]
        Category = $cells[2]
        Status = $cells[3]
        Evidence = $cells[4]
        Notes = $cells[5]
    }) | Out-Null
}

if ($mandatoryRows.Count -eq 0) {
    Add-ValidationError 'No mandatory matrix rows were found.'
}

$requiredIds = @(
    'AUTO-BUILD-01','AUTO-TEST-01','AUTO-STATIC-01','AUTO-META-01','AUTO-MATRIX-01','AUTO-MATRIX-02','AUTO-MATRIX-03','AUTO-MATRIX-04','AUTO-MATRIX-05',
    'MAN-OS-01','MAN-OS-02','MAN-INSTALL-02','MAN-INSTALL-06','MAN-INSTALL-07','MAN-NET-04','MAN-NET-05','MAN-NET-09',
    'MAN-RESTORE-01','MAN-RESTORE-02','MAN-RESTORE-03','MAN-RESTORE-04','MAN-RESTORE-05','MAN-RESTORE-06','MAN-RESTORE-07','MAN-RESTORE-08','MAN-RESTORE-09',
    'MAN-UPDATER-03','MAN-UPDATER-04','MAN-UPDATER-06','MAN-UPDATER-07','MAN-UX-05','MAN-UX-06'
)
foreach ($requiredId in $requiredIds) {
    if (-not ($mandatoryRows | Where-Object { $_.Id -eq $requiredId })) {
        Add-ValidationError "Mandatory scenario is missing from report: $requiredId"
    }
}

$validStatuses = @('Pass', 'Fail', 'Blocked', 'N/A')
$blockingCategories = @('security', 'restore', 'updater', 'release-integrity', 'release-matrix')
foreach ($row in $mandatoryRows) {
    if ($row.Mandatory -ne 'Yes') {
        Add-ValidationError "Scenario $($row.Id) must be mandatory."
    }
    if ($row.Status -notin $validStatuses) {
        Add-ValidationError "Scenario $($row.Id) has invalid or empty status '$($row.Status)'."
    }
    if (Test-Placeholder $row.Evidence) {
        Add-ValidationError "Scenario $($row.Id) is missing evidence."
    }
    if (($row.Status -in @('Blocked', 'N/A')) -and (Test-Placeholder $row.Notes)) {
        Add-ValidationError "Scenario $($row.Id) is $($row.Status) but has no concrete notes/reason."
    }
    if (($row.Status -eq 'Blocked') -and (Test-Placeholder $row.Evidence)) {
        Add-ValidationError "Scenario $($row.Id) is Blocked but has no evidence or tracking link."
    }
    if (($row.Status -eq 'Fail') -and ($row.Category -in $blockingCategories)) {
        Add-ValidationError "Blocking scenario $($row.Id) in category $($row.Category) failed."
    }
}

if ($errors.Count -gt 0) {
    if ($AllowDraft) {
        Write-Warning "Release smoke report is not complete yet:"
        $errors | ForEach-Object { Write-Warning $_ }
        exit 0
    }

    $message = "Release smoke report validation failed:`n - " + ($errors -join "`n - ")
    throw $message
}

Write-Host "Release smoke report validation passed: $fullPath"
