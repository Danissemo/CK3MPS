$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'bin\tests'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw 'The .NET Framework C# compiler was not found.'
}

& $csc /nologo /target:exe /out:(Join-Path $outDir 'LoggingEventServiceTests.exe') `
    (Join-Path $root 'source\LiveLogEventModel.cs') `
    (Join-Path $root 'source\LoggingEventService.cs') `
    (Join-Path $root 'tests\LoggingEventServiceTests.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $outDir 'LoggingEventServiceTests.exe')
exit $LASTEXITCODE
