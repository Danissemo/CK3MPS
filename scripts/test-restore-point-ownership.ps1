$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$OutDir = Join-Path $Root 'bin\tests'
$BuiltExe = Join-Path $Root 'bin\CK3MPS.exe'
$HarnessSource = Join-Path $Root 'tests\RestorePointOwnershipHarness.cs'
$TestExe = Join-Path $OutDir 'CK3MPS.RestorePointOwnershipHarness.exe'
$Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

& (Join-Path $ScriptDir 'build.ps1')

if (-not (Test-Path -LiteralPath $BuiltExe -PathType Leaf)) {
    throw "Built CK3MPS executable is missing: $BuiltExe"
}
if (-not (Test-Path -LiteralPath $Csc -PathType Leaf)) {
    throw "C# compiler was not found: $Csc"
}
if (-not (Test-Path -LiteralPath $HarnessSource -PathType Leaf)) {
    throw "Restore point ownership harness source is missing: $HarnessSource"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

& $Csc /nologo `
    /target:exe `
    /out:"$TestExe" `
    /r:System.dll `
    /r:System.Core.dll `
    /r:System.Windows.Forms.dll `
    $HarnessSource

if ($LASTEXITCODE -ne 0) {
    throw "Restore point ownership harness compilation failed with exit code $LASTEXITCODE"
}

& $TestExe $BuiltExe
if ($LASTEXITCODE -ne 0) {
    throw "Restore point ownership tests failed with exit code $LASTEXITCODE"
}

Write-Host 'Restore point ownership tests passed.' -ForegroundColor Green
