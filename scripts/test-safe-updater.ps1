Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$Exe = Join-Path $Root 'bin\CK3MPS.exe'
if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) { throw "Safe updater tests require a completed build: $Exe" }

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Invoke-UpdaterExpectFailure([string]$RequestPath, [string]$Scenario) {
    $oldUi = $env:CK3MPS_SUPPRESS_UPDATER_UI
    try {
        $env:CK3MPS_SUPPRESS_UPDATER_UI = '1'
        $process = Start-Process -FilePath $Exe -ArgumentList '--apply-update', $RequestPath -PassThru -Wait
        if ($process.ExitCode -eq 0) { throw "Updater unexpectedly accepted failure scenario: $Scenario" }
        Write-Host "PASS expected updater rejection: $Scenario"
    }
    finally {
        $env:CK3MPS_SUPPRESS_UPDATER_UI = $oldUi
    }
}

$Temp = Join-Path ([System.IO.Path]::GetTempPath()) ('ck3mps-updater-tests-' + [guid]::NewGuid().ToString('N'))
$Install = Join-Path $Temp 'install'
$Staging = Join-Path $Temp 'staging'
New-Item -ItemType Directory -Force -Path $Install, $Staging | Out-Null
Copy-Item -LiteralPath $Exe -Destination (Join-Path $Install 'CK3MPS.exe')

try {
    $health = Join-Path $Temp 'health.ok'
    $healthProcess = Start-Process -FilePath $Exe -ArgumentList '--update-health-check', $health -PassThru -Wait
    Assert-True ($healthProcess.ExitCode -eq 0) 'Health-check mode returned a nonzero exit code.'
    Assert-True (Test-Path -LiteralPath $health -PathType Leaf) 'Health-check mode did not create its token.'
    Assert-True ((Get-Content -LiteralPath $health -Raw).Trim() -ceq 'healthy') 'Health-check token was invalid.'
    Write-Host 'PASS updater health-check mode'

    $package = Join-Path $Staging 'CK3MPS-0.4.zip'
    [System.IO.File]::WriteAllBytes($package, [byte[]](1,2,3,4,5))
    $manifest = Join-Path $Staging 'CK3MPS-0.4.zip.manifest.json'
    $baseManifest = [ordered]@{
        schemaVersion = 1
        repository = 'Danissemo/CK3MPS'
        version = 'v0.4'
        packageAsset = 'CK3MPS-0.4.zip'
        packageSha256 = ('0' * 64)
        publisherSubject = 'CN=Danissemo'
        healthTimeoutSeconds = 5
        files = @([ordered]@{ path = 'CK3MPS.exe'; sha256 = ('0' * 64); signed = $true })
    }
    $baseManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding utf8

    $request = Join-Path $Staging 'request-checksum.json'
    [ordered]@{
        packagePath = $package
        manifestPath = $manifest
        installRoot = $Install
        stagingRoot = $Staging
        currentVersion = 'v0.3'
        targetVersion = 'v0.4'
        allowDowngrade = $false
    } | ConvertTo-Json | Set-Content -LiteralPath $request -Encoding utf8
    Invoke-UpdaterExpectFailure $request 'checksum mismatch'

    $downgradeRequest = Join-Path $Staging 'request-downgrade.json'
    [ordered]@{
        packagePath = $package
        manifestPath = $manifest
        installRoot = $Install
        stagingRoot = $Staging
        currentVersion = 'v0.4'
        targetVersion = 'v0.3'
        allowDowngrade = $false
    } | ConvertTo-Json | Set-Content -LiteralPath $downgradeRequest -Encoding utf8
    Invoke-UpdaterExpectFailure $downgradeRequest 'downgrade blocked'

    $badRepositoryManifest = Join-Path $Staging 'manifest-repository.json'
    $bad = $baseManifest.Clone()
    $bad.repository = 'attacker/CK3MPS'
    $bad | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $badRepositoryManifest -Encoding utf8
    $repositoryRequest = Join-Path $Staging 'request-repository.json'
    [ordered]@{
        packagePath = $package
        manifestPath = $badRepositoryManifest
        installRoot = $Install
        stagingRoot = $Staging
        currentVersion = 'v0.3'
        targetVersion = 'v0.4'
        allowDowngrade = $false
    } | ConvertTo-Json | Set-Content -LiteralPath $repositoryRequest -Encoding utf8
    Invoke-UpdaterExpectFailure $repositoryRequest 'repository allowlist'

    $source = Get-Content -LiteralPath (Join-Path $Root 'source\Updates.cs') -Raw
    Assert-True ($source -match 'releaseDto\.Prerelease') 'Stable release selection does not reject prereleases.'
    Assert-True ($source -match 'ValidateReleaseDownloadUrl') 'HTTPS release endpoint validation is missing.'
    Assert-True ($source -match 'pendingUpdateRequestPath') 'Restart-now/later staging flow is missing.'

    $updaterSource = Get-Content -LiteralPath (Join-Path $Root 'source\SafeUpdater.cs') -Raw
    foreach ($required in @('VerifyTrustedSignature', 'VerifyHash', 'RunHealthCheck', 'Rollback', 'RejectReparsePath', 'EnsureFreeSpace')) {
        Assert-True ($updaterSource -match [regex]::Escape($required)) "Updater security primitive is missing: $required"
    }
    Write-Host 'PASS updater source security contract'
}
finally {
    if (Test-Path -LiteralPath $Temp) { Remove-Item -LiteralPath $Temp -Recurse -Force }
}

Write-Host 'Safe updater tests passed.' -ForegroundColor Green
