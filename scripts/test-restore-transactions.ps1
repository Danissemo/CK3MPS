$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$OutDir = Join-Path $Root 'bin\tests'
$BuiltExe = Join-Path $Root 'bin\CK3MPS.exe'
$HarnessSource = Join-Path $Root 'tests\RestoreTransactionHarness.cs'
$CompileSource = Join-Path $OutDir 'RestoreTransactionHarness.compiled.cs'
$TestExe = Join-Path $OutDir 'CK3MPS.RestoreTransactionHarness.exe'
$Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

# PowerShell scripts surface terminating errors directly. Do not inspect a
# potentially stale $LASTEXITCODE after a successful script invocation.
& (Join-Path $ScriptDir 'build.ps1')

if (-not (Test-Path -LiteralPath $BuiltExe -PathType Leaf)) {
    throw "Built CK3MPS executable is missing: $BuiltExe"
}
if (-not (Test-Path -LiteralPath $Csc -PathType Leaf)) {
    throw "C# compiler was not found: $Csc"
}
if (-not (Test-Path -LiteralPath $HarnessSource -PathType Leaf)) {
    throw "Restore transaction harness source is missing: $HarnessSource"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# The .NET Framework compiler treats a parameterless anonymous method as
# compatible with both ThreadStart and ParameterizedThreadStart. Compile an
# explicit ThreadStart form, while accepting a checkout where the reviewed
# harness source has already been disambiguated.
$sourceText = Get-Content -LiteralPath $HarnessSource -Raw
$ambiguousNeedle = 'Thread worker = new Thread(delegate'
$disambiguatedNeedle = 'Thread worker = new Thread((ThreadStart)delegate'
$ambiguousIndex = $sourceText.IndexOf($ambiguousNeedle, [System.StringComparison]::Ordinal)
if ($ambiguousIndex -ge 0) {
    $sourceText = $sourceText.Remove($ambiguousIndex, $ambiguousNeedle.Length).Insert($ambiguousIndex, $disambiguatedNeedle)
} elseif ($sourceText.IndexOf($disambiguatedNeedle, [System.StringComparison]::Ordinal) -lt 0) {
    throw 'Could not locate the restore harness worker-thread constructor.'
}
Set-Content -LiteralPath $CompileSource -Value $sourceText -Encoding UTF8

& $Csc /nologo `
    /target:exe `
    /out:"$TestExe" `
    /r:System.dll `
    /r:System.Core.dll `
    /r:System.Windows.Forms.dll `
    $CompileSource

if ($LASTEXITCODE -ne 0) {
    throw "Restore transaction harness compilation failed with exit code $LASTEXITCODE"
}

& $TestExe $BuiltExe
if ($LASTEXITCODE -ne 0) {
    throw "Restore transaction tests failed with exit code $LASTEXITCODE"
}

Write-Host 'Restore transaction build and tests passed.' -ForegroundColor Green
