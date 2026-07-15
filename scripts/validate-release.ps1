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

$AppStatePath = Join-Path $Root "source\AppState.cs"
$ProjectPath = Join-Path $Root "CK3MPS.csproj"
$ReadmePath = Join-Path $Root "README.md"
$ReleaseReadmePath = Join-Path $Root "release\README.md"
$ReleaseDocsPath = Join-Path $Root "docs\RELEASE.md"
$TestingDocsPath = Join-Path $Root "docs\TESTING.md"

$AppVersionLine = Read-FirstMatch $AppStatePath 'AppVersion = "([^"]+)"'
$AppVersion = [regex]::Match($AppVersionLine.Line, 'AppVersion = "([^"]+)"').Groups[1].Value
if ([string]::IsNullOrWhiteSpace($AppVersion)) {
    throw "AppVersion could not be parsed from source\AppState.cs"
}

$PackageVersionLine = Read-FirstMatch $ProjectPath '<Version>([^<]+)</Version>'
$PackageVersion = [regex]::Match($PackageVersionLine.Line, '<Version>([^<]+)</Version>').Groups[1].Value
$ExpectedPackageVersion = Normalize-PackageVersion $AppVersion
if ($PackageVersion -ne $ExpectedPackageVersion) {
    throw "Version mismatch: AppVersion is $AppVersion but CK3MPS.csproj Version is $PackageVersion"
}

$ReleaseNotesPath = Join-Path $Root ("docs\release-notes-{0}.md" -f $AppVersion)
if (-not (Test-Path $ReleaseNotesPath)) {
    throw "Release notes file is missing: $ReleaseNotesPath"
}

$RequiredPaths = @(
    (Join-Path $Root "assets\screenshots\main-window.png"),
    (Join-Path $Root "assets\screenshots\scan.png"),
    (Join-Path $Root "assets\screenshots\report.png"),
    (Join-Path $Root "assets\screenshots\restore.png"),
    $ReadmePath,
    $ReleaseReadmePath,
    $ReleaseDocsPath,
    $TestingDocsPath
)

foreach ($path in $RequiredPaths) {
    if (-not (Test-Path $path)) {
        throw "Required release file is missing: $path"
    }
}

$ReleaseNotesText = Get-Content $ReleaseNotesPath -Raw
if ($ReleaseNotesText -notmatch [regex]::Escape("CK3MPS-$AppVersion.zip")) {
    throw "Release notes do not reference CK3MPS-$AppVersion.zip"
}

$ReleaseDocsText = Get-Content $ReleaseDocsPath -Raw
if ($ReleaseDocsText -notmatch [regex]::Escape("CK3MPS-$AppVersion.zip")) {
    throw "docs\RELEASE.md is not aligned with $AppVersion"
}

$ReadmeText = Get-Content $ReadmePath -Raw
if ($ReadmeText -notmatch 'Scan' -or $ReadmeText -notmatch 'Review' -or $ReadmeText -notmatch 'Apply Settings') {
    throw "README.md is missing the current Scan / Review / Apply Settings workflow text"
}

$ReleaseReadmeText = Get-Content $ReleaseReadmePath -Raw
if ($ReleaseReadmeText -notmatch 'Scan' -or $ReleaseReadmeText -notmatch 'Review' -or $ReleaseReadmeText -notmatch 'Apply Settings') {
    throw "release\README.md is missing the current Scan / Review / Apply Settings workflow text"
}

Write-Host "Release validation passed for $AppVersion"
