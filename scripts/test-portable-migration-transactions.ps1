$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$OutDir = Join-Path $Root 'bin\tests'
$TestExe = Join-Path $OutDir 'CK3MPS.PortableMigrationTransactions.Tests.exe'
$FrameworkRoots = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$Csc = $FrameworkRoots | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1

if (-not $Csc) {
    throw 'C# compiler was not found in the .NET Framework v4 directories.'
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

& $Csc /nologo `
    /target:exe `
    /out:"$TestExe" `
    /r:System.dll `
    /r:System.Core.dll `
    (Join-Path $Root 'source\Utilities.cs') `
    (Join-Path $Root 'source\RuntimeModeUtilities.cs') `
    (Join-Path $Root 'source\StepCatalog.cs') `
    (Join-Path $Root 'source\TransactionalOperations.cs') `
    (Join-Path $Root 'tests\TransactionalMigrationTests.cs')

if ($LASTEXITCODE -ne 0) {
    throw "Portable migration transaction test compilation failed with exit code $LASTEXITCODE"
}

& $TestExe
if ($LASTEXITCODE -ne 0) {
    throw "Portable migration transaction tests failed with exit code $LASTEXITCODE"
}

Write-Host 'Portable migration transaction tests passed.' -ForegroundColor Green
