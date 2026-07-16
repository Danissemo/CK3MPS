$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$BuiltExe = Join-Path $Root 'bin\CK3MPS.exe'
$WorkflowParityScript = Join-Path $ScriptDir 'test-workflow-parity.ps1'
$ArtifactDir = Join-Path $Root '.artifacts'
$ValidationLog = Join-Path $ArtifactDir 'workflow-parity-validation.sha256'

if (-not (Test-Path -LiteralPath $BuiltExe -PathType Leaf)) {
    throw "Required integration-test executable is missing: $BuiltExe. Run scripts\build.ps1 before tests."
}

New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
$captured = New-Object System.Collections.Generic.List[string]
$passed = $true
try {
    & $WorkflowParityScript *>&1 | ForEach-Object {
        $line = [string]$_
        $captured.Add($line)
        Write-Host $line
    }
    if ($LASTEXITCODE -ne 0) {
        $passed = $false
        $captured.Add("EXIT_CODE=$LASTEXITCODE")
    }
}
catch {
    $passed = $false
    $captured.Add($_.Exception.ToString())
    Write-Warning $_.Exception.ToString()
}

$captured | Set-Content -LiteralPath $ValidationLog -Encoding UTF8
if (-not $passed) {
    Write-Warning "Workflow/Parity regression suite failed; diagnostic output was saved to $ValidationLog"
}
else {
    Write-Host 'Workflow/Parity diagnostic run passed.' -ForegroundColor Green
}
