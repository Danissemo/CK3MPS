param(
    [switch]$RequireReleaseTag
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir

function Read-FirstMatch([string]$Path, [string]$Pattern) {
    $line = Select-String -Path $Path -Pattern $Pattern | Select-Object -First 1
    if (-not $line) { throw "Pattern '$Pattern' was not found in $Path" }
    return $line
}

function Normalize-PackageVersion([string]$VersionText) {
    $clean = $VersionText.TrimStart('v')
    if ($clean -match '^\d+\.\d+$') { return $clean + '.0' }
    return $clean
}

$AppVersionLine = Read-FirstMatch (Join-Path $Root 'source\AppState.cs') 'AppVersion = "([^"]+)"'
$AppVersion = [regex]::Match($AppVersionLine.Line, 'AppVersion = "([^"]+)"').Groups[1].Value
if ([string]::IsNullOrWhiteSpace($AppVersion)) { throw 'AppVersion could not be parsed from source\AppState.cs' }

$ProjectVersionLine = Read-FirstMatch (Join-Path $Root 'CK3MPS.csproj') '<Version>([^<]+)</Version>'
$ProjectVersion = [regex]::Match($ProjectVersionLine.Line, '<Version>([^<]+)</Version>').Groups[1].Value
$ExpectedProjectVersion = Normalize-PackageVersion $AppVersion
if ($ProjectVersion -ne $ExpectedProjectVersion) {
    throw "Project version mismatch: expected $ExpectedProjectVersion from $AppVersion, found $ProjectVersion"
}

$ReleaseNotesPath = Join-Path $Root ("docs\release-notes-{0}.md" -f $AppVersion)
if (-not (Test-Path -LiteralPath $ReleaseNotesPath -PathType Leaf)) {
    throw "Missing release notes file for ${AppVersion}: $ReleaseNotesPath"
}

if ($RequireReleaseTag) {
    if ($env:GITHUB_REF_TYPE -ne 'tag') {
        throw "Release publishing requires a tag ref, but GITHUB_REF_TYPE is '$($env:GITHUB_REF_TYPE)'."
    }
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_REF_NAME)) {
        throw 'Release publishing requires GITHUB_REF_NAME to contain the pushed tag.'
    }
    if ($env:GITHUB_REF_NAME -cne $AppVersion) {
        throw "Release tag mismatch: pushed '$($env:GITHUB_REF_NAME)' but AppVersion is '$AppVersion'."
    }
    Write-Host "Release tag matches AppVersion exactly: $AppVersion"
}

Write-Host 'Version consistency passed.'
Write-Host "AppVersion: $AppVersion"
Write-Host "Project version: $ProjectVersion"
Write-Host "Expected release title: CK3MPS $AppVersion"
