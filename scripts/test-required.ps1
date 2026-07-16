$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$BuiltExe = Join-Path $Root 'bin\CK3MPS.exe'
$WorkflowParityScript = Join-Path $ScriptDir 'test-workflow-parity.ps1'
$ArtifactDir = Join-Path $Root '.artifacts'
$ValidationLog = Join-Path $ArtifactDir 'workflow-parity-validation.log'

if (-not (Test-Path -LiteralPath $BuiltExe -PathType Leaf)) {
    throw "Required integration-test executable is missing: $BuiltExe. Run scripts\build.ps1 before tests."
}

New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
try {
    & $WorkflowParityScript *>&1 | Tee-Object -FilePath $ValidationLog
    if ($LASTEXITCODE -ne 0) {
        throw "Workflow/Parity regression suite failed with exit code $LASTEXITCODE"
    }
}
catch {
    $_.Exception.ToString() | Out-File -LiteralPath $ValidationLog -Append -Encoding UTF8
    throw
}

Write-Host 'Workflow/Parity validation suite completed.' -ForegroundColor Green
