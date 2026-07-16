$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$BuiltExe = Join-Path $Root 'bin\CK3MPS.exe'
$TestScript = Join-Path $ScriptDir 'test.ps1'

if (-not (Test-Path -LiteralPath $BuiltExe -PathType Leaf)) {
    throw "Required integration-test executable is missing: $BuiltExe. Run scripts\build.ps1 before tests."
}
if (-not (Test-Path -LiteralPath $TestScript -PathType Leaf)) {
    throw "Required integration-test script is missing: $TestScript"
}

# PowerShell scripts propagate terminating errors themselves. Do not inspect
# $LASTEXITCODE here: a successful script can leave a stale native exit code.
& $TestScript

Write-Host 'Required integration test suite completed.' -ForegroundColor Green
