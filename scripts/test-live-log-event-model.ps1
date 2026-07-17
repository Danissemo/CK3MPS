$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$OutDir = Join-Path $Root "bin\tests"
$TestExe = Join-Path $OutDir "CK3MPS.LiveLogEventModelTests.exe"
$Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $Csc)) {
    throw "C# compiler was not found: $Csc"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

& $Csc /nologo `
    /target:exe `
    /out:"$TestExe" `
    /r:System.dll `
    (Join-Path $Root "source\LiveLogEventModel.cs") `
    (Join-Path $Root "tests\LiveLogEventModelTests.cs")

if ($LASTEXITCODE -ne 0) {
    throw "LiveLogEventModel test compilation failed with exit code $LASTEXITCODE"
}

& $TestExe
if ($LASTEXITCODE -ne 0) {
    throw "LiveLogEventModel tests failed with exit code $LASTEXITCODE"
}
