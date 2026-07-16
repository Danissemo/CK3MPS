$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$OutDir = Join-Path $Root "bin\tests\workflow-parity"
$BuiltExe = Join-Path $Root "bin\CK3MPS.exe"
$CscCandidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$Csc = $CscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $Csc) {
    throw "C# compiler was not found in the .NET Framework directories."
}
if (-not (Test-Path $BuiltExe)) {
    throw "Built application is required before Workflow/Parity regression tests: $BuiltExe"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Build-Harness {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Sources
    )

    $target = Join-Path $OutDir ("CK3MPS." + $Name + ".exe")
    $arguments = @(
        "/nologo",
        "/target:exe",
        "/out:$target",
        "/r:System.dll",
        "/r:System.Core.dll",
        "/r:System.Windows.Forms.dll"
    ) + $Sources

    & $Csc @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name compilation failed with exit code $LASTEXITCODE"
    }
    return $target
}

function Run-Harness {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Executable,
        [string[]]$Arguments = @()
    )

    Write-Host "[workflow-parity] Running $Name"
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

$WorkflowRefreshExe = Build-Harness -Name "WorkflowRefreshRegressionTests" -Sources @(
    (Join-Path $Root "source\WorkflowAnalysisCoordinator.cs"),
    (Join-Path $Root "tests\WorkflowRefreshRegressionTests.cs")
)
Run-Harness -Name "Workflow refresh race/snapshot tests" -Executable $WorkflowRefreshExe

$ParitySecurityExe = Build-Harness -Name "ParityRoomSecurityHarness" -Sources @(
    (Join-Path $Root "tests\ParityRoomSecurityHarness.cs")
)
Run-Harness -Name "Parity encryption/signature/replay/rate tests" -Executable $ParitySecurityExe -Arguments @($BuiltExe)

$ParitySlowClientExe = Build-Harness -Name "ParityRoomSlowClientHarness" -Sources @(
    (Join-Path $Root "tests\ParityRoomSlowClientHarness.cs")
)
Run-Harness -Name "Parity slow-client/loopback test" -Executable $ParitySlowClientExe -Arguments @($BuiltExe)

$ParityLanExe = Build-Harness -Name "ParityRoomLanRegressionHarness" -Sources @(
    (Join-Path $Root "tests\ParityRoomLanRegressionHarness.cs")
)
Run-Harness -Name "Parity LAN/security/limit/stop tests" -Executable $ParityLanExe -Arguments @($BuiltExe)

Write-Host "[workflow-parity] All dedicated Workflow and Parity regression tests passed."
