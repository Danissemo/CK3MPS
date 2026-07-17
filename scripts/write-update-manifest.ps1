param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [switch]$RequireSignature
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$ExpectedPublisher = 'CN=Danissemo'
$ExpectedPackageName = "CK3MPS-$Version.zip"
if ([System.IO.Path]::GetFileName($PackagePath) -cne $ExpectedPackageName) {
    throw "Unexpected update package name. Expected '$ExpectedPackageName'."
}

$files = @()
Get-ChildItem -LiteralPath $PackageDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($PackageDirectory.Length).TrimStart('\') -replace '\\', '/'
    if ($relative -match '(^|/)(\.\.?)(/|$)') { throw "Unsafe manifest path: $relative" }
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $signed = $false
    if ($_.Extension -ieq '.exe' -or $_.Extension -ieq '.dll') {
        $signature = Get-AuthenticodeSignature -LiteralPath $_.FullName
        $signed = $signature.Status -eq 'Valid' -and $null -ne $signature.SignerCertificate
        if ($RequireSignature) {
            if (-not $signed) { throw "Release executable is not validly Authenticode-signed: $relative ($($signature.Status))" }
            if ($signature.SignerCertificate.Subject -cne $ExpectedPublisher) {
                throw "Unexpected release publisher '$($signature.SignerCertificate.Subject)'. Expected '$ExpectedPublisher'."
            }
        }
    }
    $files += [ordered]@{ path = $relative; sha256 = $hash; signed = [bool]$signed }
}

if (-not ($files | Where-Object { $_.path -ceq 'CK3MPS.exe' })) {
    throw 'Update package does not contain CK3MPS.exe.'
}

$manifest = [ordered]@{
    schemaVersion = 1
    repository = 'Danissemo/CK3MPS'
    version = "v$Version"
    packageAsset = $ExpectedPackageName
    packageSha256 = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    publisherSubject = $ExpectedPublisher
    healthTimeoutSeconds = 30
    files = $files
}

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Update manifest: $OutputPath"
