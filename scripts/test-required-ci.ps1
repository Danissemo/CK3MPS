$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$TestScript = Join-Path $ScriptDir 'test-required.ps1'
$ArtifactDir = Join-Path $Root '.artifacts'
$ArtifactPath = Join-Path $ArtifactDir 'integration-test-tail.txt'

$captured = @()
$oldPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    $captured = @(& pwsh -NoProfile -NonInteractive -File $TestScript 2>&1)
    $exitCode = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $oldPreference
}

$tailCount = if ($exitCode -eq 0) { 30 } else { 200 }
$tail = @($captured | Select-Object -Last $tailCount | ForEach-Object { [string]$_ })
$tail | ForEach-Object { Write-Host $_ }

New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
[IO.File]::WriteAllLines($ArtifactPath, $tail, (New-Object Text.UTF8Encoding($false)))

if ($exitCode -ne 0) {
    exit $exitCode
}

Write-Host 'Required integration test suite passed.' -ForegroundColor Green
