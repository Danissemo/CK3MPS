param(
    [switch]$UpdateReleaseArtifacts
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$OutDir = Join-Path $Root "bin"
$OutExe = Join-Path $OutDir "CK3MPS.exe"
$ReleaseDir = Join-Path $Root "release"
$ReleaseExe = Join-Path $ReleaseDir "CK3MPS.exe"
$Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$Project = Join-Path $Root "CK3MPS.csproj"

function Find-MSBuild {
    $VsWhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $VsWhere) {
        $Found = & $VsWhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($Found -and (Test-Path $Found)) {
            return $Found
        }
    }

    $Candidates = @(
        "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )

    return $Candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$MSBuild = Find-MSBuild
if ($MSBuild) {
    & $MSBuild $Project /p:Configuration=Release /nologo /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed with exit code $LASTEXITCODE"
    }
} else {
    if (-not (Test-Path $Csc)) {
        throw "MSBuild and C# compiler were not found."
    }

    $Sources = Get-ChildItem (Join-Path $Root "source") -Filter "*.cs" -File |
        Sort-Object Name |
        ForEach-Object { $_.FullName }

    & $Csc /nologo `
        /target:winexe `
        /win32icon:"$Root\assets\app.ico" `
        /win32manifest:"$Root\assets\app.manifest" `
        /out:"$OutExe" `
        /r:System.dll `
        /r:System.Core.dll `
        /r:System.IO.Compression.dll `
        /r:System.IO.Compression.FileSystem.dll `
        /r:System.Runtime.Serialization.dll `
        /r:System.Xml.dll `
        /r:System.Drawing.dll `
        /r:System.Windows.Forms.dll `
        $Sources

    if ($LASTEXITCODE -ne 0) {
        throw "C# compiler failed with exit code $LASTEXITCODE"
    }
}

$Hash = Get-FileHash $OutExe -Algorithm SHA256
Write-Host "Built: $OutExe"
Write-Host "SHA256: $($Hash.Hash)"

if ($UpdateReleaseArtifacts) {
    New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null
    Copy-Item -LiteralPath $OutExe -Destination $ReleaseExe -Force
    $ReleaseChecksum = Join-Path $ReleaseDir "CK3MPS.exe.sha256"
    Set-Content -LiteralPath $ReleaseChecksum -Value ($Hash.Hash.ToLowerInvariant() + "  CK3MPS.exe") -Encoding ascii
    Write-Host "Release exe: $ReleaseExe"
    Write-Host "Release checksum: $ReleaseChecksum"
} else {
    Write-Host "Release artifacts unchanged. Local builds do not update the release folder."
}
