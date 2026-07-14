$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
& (Join-Path $ScriptDir "build.ps1")

$VersionLine = Select-String -Path (Join-Path $Root "source\AppState.cs") -Pattern 'AppVersion = "([^"]+)"' | Select-Object -First 1
if (-not $VersionLine) {
    throw "Could not detect AppVersion."
}

$Version = [regex]::Match($VersionLine.Line, 'AppVersion = "([^"]+)"').Groups[1].Value
$ReleaseRoot = Join-Path (Split-Path -Parent $Root) "CK3MPS_exports"
$PackageDir = Join-Path $ReleaseRoot "CK3MPS-$Version"
$ZipPath = Join-Path $ReleaseRoot "CK3MPS-$Version.zip"

if (Test-Path $PackageDir) {
    Remove-Item -LiteralPath $PackageDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null

Copy-Item -LiteralPath (Join-Path $Root "release\CK3MPS.exe") -Destination $PackageDir
Copy-Item -LiteralPath (Join-Path $Root "release\README.md") -Destination $PackageDir
Copy-Item -LiteralPath (Join-Path $Root "README.md") -Destination (Join-Path $PackageDir "REPO_README.md")
Copy-Item -LiteralPath (Join-Path $Root "LICENSE") -Destination $PackageDir

if (Test-Path $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}
Compress-Archive -Path (Join-Path $PackageDir "*") -DestinationPath $ZipPath

$Hash = Get-FileHash $ZipPath -Algorithm SHA256
Write-Host "Release package: $ZipPath"
Write-Host "SHA256: $($Hash.Hash)"

Remove-Item -LiteralPath $PackageDir -Recurse -Force


