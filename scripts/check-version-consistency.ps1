$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir

function Read-FirstMatch([string]$Path, [string]$Pattern) {
    $line = Select-String -Path $Path -Pattern $Pattern | Select-Object -First 1
    if (-not $line) {
        throw "Pattern '$Pattern' was not found in $Path"
    }

    return $line
}

function Normalize-PackageVersion([string]$VersionText) {
    $clean = $VersionText.TrimStart('v')
    if ($clean -match '^\d+\.\d+$') {
        return $clean + ".0"
    }

    return $clean
}

$AppVersionLine = Read-FirstMatch (Join-Path $Root "source\AppState.cs") 'AppVersion = "([^"]+)"'
$AppVersion = [regex]::Match($AppVersionLine.Line, 'AppVersion = "([^"]+)"').Groups[1].Value

$ProjectVersionLine = Read-FirstMatch (Join-Path $Root "CK3MPS.csproj") '<Version>([^<]+)</Version>'
$ProjectVersion = [regex]::Match($ProjectVersionLine.Line, '<Version>([^<]+)</Version>').Groups[1].Value

$ExpectedProjectVersion = Normalize-PackageVersion $AppVersion
if ($ProjectVersion -ne $ExpectedProjectVersion) {
    throw "Project version mismatch: expected $ExpectedProjectVersion from $AppVersion, found $ProjectVersion"
}

$ReleaseNotesPath = Join-Path $Root ("docs\release-notes-{0}.md" -f $AppVersion)
if (-not (Test-Path $ReleaseNotesPath)) {
    throw "Missing release notes file for $AppVersion"
}

$ReleaseTitle = "CK3MPS $AppVersion"
Write-Host "Version consistency passed."
Write-Host "AppVersion: $AppVersion"
Write-Host "Project version: $ProjectVersion"
Write-Host "Expected release title: $ReleaseTitle"
