Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$BuiltExe = Join-Path $Root 'bin\CK3MPS.exe'
$TestLogDir = Join-Path $Root 'bin\test-logs'

if (-not (Test-Path -LiteralPath $BuiltExe -PathType Leaf)) {
    throw "Test orchestration requires a completed build. Missing executable: $BuiltExe"
}
New-Item -ItemType Directory -Force -Path $TestLogDir | Out-Null

function Get-TestPriority {
    param([Parameter(Mandatory = $true)][string]$Name)
    switch -Regex ($Name.ToLowerInvariant()) {
        '^test-required\.ps1$' { return 0 }
        'portable.*migration|migration.*portable' { return 10 }
        'restore.*transaction|transaction.*restore' { return 20 }
        'workflow.*parity|parity.*workflow' { return 30 }
        'step-?catalog|catalog.*step' { return 40 }
        '^test-transactional-hardening\.ps1$' { return 50 }
        default { return 100 }
    }
}

function Get-PowerShellExecutable {
    $candidateName = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' }
    $candidatePath = Join-Path $PSHOME $candidateName
    if (Test-Path -LiteralPath $candidatePath -PathType Leaf) { return $candidatePath }
    foreach ($commandName in @('pwsh', 'powershell')) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
    }
    throw 'Could not locate a PowerShell executable for isolated test execution.'
}

function Invoke-TestScript {
    param(
        [Parameter(Mandatory = $true)][System.IO.FileInfo]$Script,
        [Parameter(Mandatory = $true)][string]$PowerShellExecutable
    )

    $logPath = Join-Path $TestLogDir ($Script.BaseName + '.log')
    Write-Host ("::group::Test script: {0}" -f $Script.Name)
    try {
        & $PowerShellExecutable -NoLogo -NoProfile -NonInteractive -File $Script.FullName *> $logPath
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            if (Test-Path -LiteralPath $logPath -PathType Leaf) {
                Get-Content -LiteralPath $logPath | ForEach-Object { Write-Host $_ }
            }
            throw "Test script '$($Script.Name)' failed with exit code $exitCode."
        }
        Write-Host ("PASS {0}" -f $Script.Name) -ForegroundColor Green
    }
    finally {
        Write-Host '::endgroup::'
    }
}

$RequiredIntegrationScript = Join-Path $ScriptDir 'test-required.ps1'
if (-not (Test-Path -LiteralPath $RequiredIntegrationScript -PathType Leaf)) {
    throw "Required integration-test wrapper is missing: $RequiredIntegrationScript"
}

$requiredScript = Get-Item -LiteralPath $RequiredIntegrationScript
$additionalScripts = Get-ChildItem -LiteralPath $ScriptDir -Filter 'test-*.ps1' -File |
    Where-Object { $_.Name -notin @('test-all.ps1', 'test-required.ps1') } |
    Sort-Object `
        @{ Expression = { Get-TestPriority -Name $_.Name }; Ascending = $true }, `
        @{ Expression = { $_.Name.ToLowerInvariant() }; Ascending = $true }

$orderedScripts = @($requiredScript) + @($additionalScripts)
$duplicateNames = $orderedScripts | Group-Object { $_.Name.ToLowerInvariant() } | Where-Object { $_.Count -gt 1 }
if ($duplicateNames) {
    throw "Duplicate test-script names were discovered: $($duplicateNames.Name -join ', ')"
}

$PowerShellExecutable = Get-PowerShellExecutable
Write-Host ("Test orchestrator discovered {0} script(s)." -f $orderedScripts.Count)
foreach ($script in $orderedScripts) {
    Invoke-TestScript -Script $script -PowerShellExecutable $PowerShellExecutable
}
Write-Host ("All test scripts passed: {0}" -f (($orderedScripts | ForEach-Object Name) -join ', ')) -ForegroundColor Green
