$ErrorActionPreference = 'Continue'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$ArtifactDir = Join-Path $Root '.artifacts'
$ArtifactPath = Join-Path $ArtifactDir 'static-danger-output.txt'

$output = @(& pwsh -NoProfile -NonInteractive -File (Join-Path $ScriptDir 'check-static-danger-strict.ps1') 2>&1)
$exitCode = $LASTEXITCODE
New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null
$output | ForEach-Object { Write-Host ([string]$_) }
[IO.File]::WriteAllLines($ArtifactPath, @($output | ForEach-Object { [string]$_ }), (New-Object Text.UTF8Encoding($false)))
exit $exitCode
