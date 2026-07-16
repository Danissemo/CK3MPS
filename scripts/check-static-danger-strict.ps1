$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir

& (Join-Path $ScriptDir 'check-static-danger.ps1')

$ReviewedBlobs = [ordered]@{
    'source/AppConfig.cs' = '87e66749af2a63356071c57c1d26973e3047fd60'
    'source/Cleanup.cs' = '9240e1fe9fc3f50c9fb770f0475adc1ce08a11f3'
    'source/GameSettings.cs' = '50c83c7ffca2ad4fe742fd517bdb10d7236eb29e'
    'source/Helpers.cs' = 'cd912feb9ce896f5d3c64683d7d98130d6171371'
    'source/Launchers.cs' = '0fbe6046e4a5c332462f6778ede347d9ab284936'
    'source/Readiness.cs' = '71670460aedcb1033273e5134fef39e0c4e22b1f'
    'source/Reports.cs' = '9f872149a526267721acb7d02ad5afc0df4b37bf'
    'source/SaveAnalysis.cs' = '2638181c295e5b47ecb0523d715799d05161d123'
    'source/Workflow.cs' = 'b4eecf5fd98b56e00c4e0e30afdc589a0b65a865'
}

foreach ($relativePath in $ReviewedBlobs.Keys) {
    $fullPath = Join-Path $Root ($relativePath -replace '/', '\')
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Reviewed mutation file is missing: $relativePath"
    }

    # Use the staged Git object, not checkout bytes, so CRLF/LF conversion
    # cannot change the reviewed identity.
    $indexLines = @(git -C $Root ls-files --stage -- $relativePath)
    if ($LASTEXITCODE -ne 0) { throw "Could not read the Git index entry for $relativePath" }
    if ($indexLines.Count -ne 1) {
        throw "Expected one Git index entry for $relativePath, found $($indexLines.Count)"
    }

    $parts = $indexLines[0] -split '\s+', 4
    if ($parts.Count -lt 4 -or $parts[1] -notmatch '^[0-9a-f]{40}$') {
        throw "Invalid Git index entry for $relativePath: $($indexLines[0])"
    }

    $actual = $parts[1]
    git -C $Root cat-file -e ('{0}^{{blob}}' -f $actual)
    if ($LASTEXITCODE -ne 0) { throw "Git object for $relativePath is not a valid blob: $actual" }

    $expected = $ReviewedBlobs[$relativePath]
    if ($actual -ne $expected) {
        throw "Broad mutation allowlist review expired for $relativePath. Expected reviewed blob $expected, found $actual. Review every dangerous call and update the pin deliberately."
    }
}

Write-Host ("Strict static danger review passed. Pinned files: {0}" -f $ReviewedBlobs.Count) -ForegroundColor Green
