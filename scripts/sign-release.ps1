param(
    [Parameter(Mandatory = $true)][string]$CertificateBase64,
    [Parameter(Mandatory = $true)][string]$CertificatePassword,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$Exe = Join-Path $Root 'bin\CK3MPS.exe'
if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) { throw "Build output is missing: $Exe" }
if ([string]::IsNullOrWhiteSpace($CertificateBase64)) { throw 'Signing certificate secret is empty.' }

$PfxPath = Join-Path $env:RUNNER_TEMP ('ck3mps-signing-' + [guid]::NewGuid().ToString('N') + '.pfx')
try {
    [System.IO.File]::WriteAllBytes($PfxPath, [Convert]::FromBase64String($CertificateBase64))
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter signtool.exe -Recurse -File |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe was not found on the runner.' }

    & $signtool.FullName sign /fd SHA256 /td SHA256 /tr $TimestampServer /f $PfxPath /p $CertificatePassword $Exe
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE." }

    $signature = Get-AuthenticodeSignature -LiteralPath $Exe
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate) {
        throw "Signed executable failed Authenticode verification: $($signature.Status)"
    }
    if ($signature.SignerCertificate.Subject -cne 'CN=Danissemo') {
        throw "Unexpected signer subject '$($signature.SignerCertificate.Subject)'. Expected 'CN=Danissemo'."
    }
    Write-Host "Signed and verified: $Exe"
}
finally {
    if (Test-Path -LiteralPath $PfxPath) { Remove-Item -LiteralPath $PfxPath -Force }
}
