$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$TestScript = Join-Path $ScriptDir 'test-required.ps1'

$captured = @()
$oldPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    $captured = @(& pwsh -NoProfile -NonInteractive -File $TestScript 2>&1)
    $exitCode = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $oldPreference
}

$tailCount = if ($exitCode -eq 0) { 30 } else { 160 }
$captured | Select-Object -Last $tailCount | ForEach-Object { Write-Host ([string]$_) }

if ($exitCode -ne 0) {
    throw "Required integration test suite failed with exit code $exitCode"
}

Write-Host 'Required integration test suite passed.' -ForegroundColor Green
