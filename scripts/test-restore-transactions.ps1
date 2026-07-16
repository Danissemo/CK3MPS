$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$OutDir = Join-Path $Root 'bin\tests'
$BuiltExe = Join-Path $Root 'bin\CK3MPS.exe'
$TestExe = Join-Path $OutDir 'CK3MPS.RestoreTransactionHarness.exe'
$Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

& (Join-Path $ScriptDir 'build.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "CK3MPS build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $BuiltExe -PathType Leaf)) {
    throw "Built CK3MPS executable is missing: $BuiltExe"
}
if (-not (Test-Path -LiteralPath $Csc -PathType Leaf)) {
    throw "C# compiler was not found: $Csc"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

& $Csc /nologo `
    /target:exe `
    /out:"$TestExe" `
    /r:System.dll `
    /r:System.Core.dll `
    /r:System.Windows.Forms.dll `
    (Join-Path $Root 'tests\RestoreTransactionHarness.cs')

if ($LASTEXITCODE -ne 0) {
    throw "Restore transaction harness compilation failed with exit code $LASTEXITCODE"
}

& $TestExe $BuiltExe
if ($LASTEXITCODE -ne 0) {
    throw "Restore transaction tests failed with exit code $LASTEXITCODE"
}

Write-Host 'Restore transaction build and tests passed.' -ForegroundColor Green
