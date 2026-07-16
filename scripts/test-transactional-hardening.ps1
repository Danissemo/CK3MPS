$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$OutDir = Join-Path $Root 'bin\tests'
$TestExe = Join-Path $OutDir 'CK3MPS.TransactionalMigrationTests.exe'
$Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $Csc -PathType Leaf)) {
    throw "C# compiler was not found: $Csc"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

& $Csc /nologo `
    /target:exe `
    /out:"$TestExe" `
    /r:System.dll `
    (Join-Path $Root 'source\Utilities.cs') `
    (Join-Path $Root 'source\RuntimeModeUtilities.cs') `
    (Join-Path $Root 'source\StepCatalog.cs') `
    (Join-Path $Root 'source\TransactionalOperations.cs') `
    (Join-Path $Root 'tests\TransactionalMigrationTests.cs')

if ($LASTEXITCODE -ne 0) {
    throw "Transactional hardening test compilation failed with exit code $LASTEXITCODE"
}

& $TestExe
if ($LASTEXITCODE -ne 0) {
    throw "Transactional hardening tests failed with exit code $LASTEXITCODE"
}

Write-Host 'Transactional hardening tests passed.' -ForegroundColor Green
