$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir

$ForbiddenPaths = @(
    ".codex-remote-attachments",
    "_oos_extract",
    "CK3MPS_exports",
    ".artifacts",
    "assets/screenshots/main-window-v0.2-current.png"
)

$Found = @()
foreach ($relative in $ForbiddenPaths) {
    $path = Join-Path $Root $relative
    if (Test-Path $path) {
        $Found += $relative
    }
}

if ($Found.Count -gt 0) {
    throw ("Forbidden repo-local artifacts found:`n- " + ($Found -join "`n- "))
}

$RequiredScreenshots = @(
    "assets/screenshots/main-window.png",
    "assets/screenshots/scan.png",
    "assets/screenshots/report.png",
    "assets/screenshots/restore.png"
)

foreach ($relative in $RequiredScreenshots) {
    $path = Join-Path $Root $relative
    if (-not (Test-Path $path)) {
        throw "Required screenshot is missing: $relative"
    }
}

Write-Host "Repo clean check passed."
