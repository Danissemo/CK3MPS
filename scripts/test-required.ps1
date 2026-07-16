$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$BuiltExe = Join-Path $Root 'bin\CK3MPS.exe'
$TestScript = Join-Path $ScriptDir 'test.ps1'
$WorkflowParityScript = Join-Path $ScriptDir 'test-workflow-parity.ps1'

if (-not (Test-Path -LiteralPath $BuiltExe -PathType Leaf)) {
    throw "Required integration-test executable is missing: $BuiltExe. Run scripts\build.ps1 before tests."
}

& $TestScript
if ($LASTEXITCODE -ne 0) {
    throw "Integration test suite failed with exit code $LASTEXITCODE"
}

& $WorkflowParityScript
if ($LASTEXITCODE -ne 0) {
    throw "Workflow/Parity regression suite failed with exit code $LASTEXITCODE"
}

Write-Host 'Required integration test suite completed.' -ForegroundColor Green
