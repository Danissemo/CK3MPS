$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$ExportRoot = Join-Path (Split-Path -Parent $Root) "CK3MPS_exports"
$PackageRoot = Join-Path $ExportRoot "github-package"
$NuGet = Join-Path $ExportRoot "nuget.exe"
$Nuspec = Join-Path $PackageRoot "CK3MPS.nuspec"

New-Item -ItemType Directory -Force -Path $PackageRoot | Out-Null

if (-not (Test-Path $NuGet)) {
    Invoke-WebRequest -UseBasicParsing "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $NuGet
}

$Commit = "unknown"
try {
    $Commit = (& git -C $Root rev-parse HEAD).Trim()
} catch {
    Write-Host "Git commit was not detected; package repository metadata will use 'unknown'."
}

@"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>CK3MPS</id>
    <version>0.1.0-beta.1</version>
    <authors>Danissemo</authors>
    <owners>Danissemo</owners>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <license type="file">LICENSE</license>
    <readme>README.md</readme>
    <projectUrl>https://github.com/Danissemo/CK3MPS</projectUrl>
    <repository type="git" url="https://github.com/Danissemo/CK3MPS.git" commit="$Commit" />
    <description>CK3 multiplayer stabilization utility for Windows.</description>
    <releaseNotes>Presentation refresh package for v0.1 beta. Regular users should download the Release executable.</releaseNotes>
    <copyright>Copyright (c) Danissemo</copyright>
    <tags>CK3 Crusader-Kings-III multiplayer stabilization OOS Windows Paradox Steam</tags>
  </metadata>
  <files>
    <file src="release\CK3MPS.exe" target="tools\CK3MPS.exe" />
    <file src="release\README.md" target="tools\README.md" />
    <file src="README.md" target="README.md" />
    <file src="CHANGELOG.md" target="CHANGELOG.md" />
    <file src="LICENSE" target="LICENSE" />
  </files>
</package>
"@ | Set-Content -Encoding UTF8 -Path $Nuspec

Remove-Item (Join-Path $PackageRoot "*.nupkg") -Force -ErrorAction SilentlyContinue
& $NuGet pack $Nuspec -BasePath $Root -OutputDirectory $PackageRoot -NoDefaultExcludes

Get-ChildItem $PackageRoot -Filter "*.nupkg" | Select-Object FullName, Length
