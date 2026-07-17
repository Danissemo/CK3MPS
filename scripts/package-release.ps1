param(
    [switch]$SkipBuild,
    [string]$ExportRoot
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$BuiltExe = Join-Path $Root 'bin\CK3MPS.exe'
$ReleaseDir = Join-Path $Root 'release'
$ReleaseExe = Join-Path $ReleaseDir 'CK3MPS.exe'
$ReleaseChecksum = Join-Path $ReleaseDir 'CK3MPS.exe.sha256'

if ($SkipBuild) {
    if (-not (Test-Path -LiteralPath $BuiltExe -PathType Leaf)) {
        throw "Cannot package release without the tested build output: $BuiltExe"
    }
    New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null
    Copy-Item -LiteralPath $BuiltExe -Destination $ReleaseExe -Force
    $exeHash = Get-FileHash -LiteralPath $ReleaseExe -Algorithm SHA256
    Set-Content -LiteralPath $ReleaseChecksum -Value ($exeHash.Hash.ToLowerInvariant() + '  CK3MPS.exe') -Encoding ascii
} else {
    & (Join-Path $ScriptDir 'build.ps1') -UpdateReleaseArtifacts
}

& (Join-Path $ScriptDir 'validate-release.ps1')

$VersionLine = Select-String -Path (Join-Path $Root 'source\AppState.cs') -Pattern 'AppVersion = "([^"]+)"' | Select-Object -First 1
if (-not $VersionLine) { throw 'Could not detect AppVersion.' }
$Version = [regex]::Match($VersionLine.Line, 'AppVersion = "([^"]+)"').Groups[1].Value
if ([string]::IsNullOrWhiteSpace($Version)) { throw 'AppVersion is empty.' }

if ([string]::IsNullOrWhiteSpace($ExportRoot)) {
    $ReleaseRoot = Join-Path (Split-Path -Parent $Root) 'CK3MPS_exports'
} else {
    $ReleaseRoot = if ([System.IO.Path]::IsPathRooted($ExportRoot)) { $ExportRoot } else { Join-Path $Root $ExportRoot }
}

New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null
$PackageDir = Join-Path $ReleaseRoot "CK3MPS-$Version"
$ZipPath = Join-Path $ReleaseRoot "CK3MPS-$Version.zip"
$ZipChecksumPath = Join-Path $ReleaseRoot "CK3MPS-$Version.zip.sha256"

if (Test-Path -LiteralPath $PackageDir) { Remove-Item -LiteralPath $PackageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null
foreach ($requiredPath in @($ReleaseExe, $ReleaseChecksum)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required release artifact is missing: $requiredPath"
    }
}

Copy-Item -LiteralPath $ReleaseExe -Destination $PackageDir
Copy-Item -LiteralPath (Join-Path $Root 'release\README.md') -Destination $PackageDir
Copy-Item -LiteralPath (Join-Path $Root 'README.md') -Destination (Join-Path $PackageDir 'REPO_README.md')
Copy-Item -LiteralPath (Join-Path $Root 'LICENSE') -Destination $PackageDir

if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
Compress-Archive -Path (Join-Path $PackageDir '*') -DestinationPath $ZipPath
$Hash = Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256
Set-Content -LiteralPath $ZipChecksumPath -Value ($Hash.Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($ZipPath)) -Encoding ascii
Write-Host "Release package: $ZipPath"
Write-Host "Release checksum: $ZipChecksumPath"
Write-Host "SHA256: $($Hash.Hash)"
Remove-Item -LiteralPath $PackageDir -Recurse -Force