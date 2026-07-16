$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$failures = New-Object System.Collections.Generic.List[string]

function Add-Failure {
    param([string]$Message)
    $script:failures.Add($Message)
}

function Get-RelativeRepoPath {
    param([string]$Path)

    $relative = Resolve-Path -LiteralPath $Path | ForEach-Object {
        $repoUri = New-Object System.Uri((Resolve-Path -LiteralPath $repoRoot).Path.TrimEnd('\') + '\')
        $fileUri = New-Object System.Uri($_.Path)
        $repoUri.MakeRelativeUri($fileUri).ToString()
    }

    return ($relative -replace '\\', '/')
}

function Test-PathAllowed {
    param(
        [string]$RelativePath,
        [string[]]$AllowedPrefixes
    )

    foreach ($prefix in $AllowedPrefixes) {
        if ($RelativePath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Test-TextLooksBinary {
    param([string]$Path)

    try {
        $sample = [System.IO.File]::ReadAllBytes($Path)
    } catch {
        return $true
    }

    $limit = [Math]::Min($sample.Length, 4096)
    for ($i = 0; $i -lt $limit; $i++) {
        if ($sample[$i] -eq 0) {
            return $true
        }
    }

    return $false
}

function Test-LooksLikeLfsPointer {
    param([string]$Path)

    try {
        $content = Get-Content -LiteralPath $Path -Raw
    } catch {
        return $false
    }

    return $content -match '(?m)^version https://git-lfs\.github\.com/spec/v1$' -and
        $content -match '(?m)^oid sha256:[0-9a-f]{64}$' -and
        $content -match '(?m)^size \d+$'
}

$allowedDiagnosticPrefixes = @(
    'tests/fixtures/oos_smoke/'
)

$allowedScreenshotFiles = @(
    'main-window.png',
    'scan.png',
    'report.png',
    'restore.png'
)

$trackedFiles = @()
$gitFiles = git ls-files
if ($LASTEXITCODE -ne 0) {
    throw 'git ls-files failed.'
}

foreach ($line in $gitFiles) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $normalized = $line.Trim() -replace '\\', '/'
    $fullPath = Join-Path $repoRoot $normalized
    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        $trackedFiles += [pscustomobject]@{
            RelativePath = $normalized
            FullPath = $fullPath
        }
    }
}

$extractMatches = $trackedFiles | Where-Object {
    $_.RelativePath -match '(^|/)_oos_extract[^/]*($|/)'
}
foreach ($match in $extractMatches) {
    Add-Failure "Tracked OOS extract artifact is not allowed: $($match.RelativePath)"
}

$fixturesRoot = Join-Path $repoRoot 'tests\fixtures'
$maxFixtureFileBytes = 1MB
$maxFixtureTotalBytes = 5MB

if (Test-Path -LiteralPath $fixturesRoot -PathType Container) {
    $fixtureFiles = Get-ChildItem -LiteralPath $fixturesRoot -Recurse -File
    $fixtureTotalBytes = ($fixtureFiles | Measure-Object Length -Sum).Sum
    if ($null -eq $fixtureTotalBytes) {
        $fixtureTotalBytes = 0
    }

    foreach ($file in $fixtureFiles) {
        $relative = Get-RelativeRepoPath -Path $file.FullName
        if ($file.Length -gt $maxFixtureFileBytes) {
            Add-Failure "Fixture file exceeds $maxFixtureFileBytes bytes: $relative ($($file.Length) bytes)"
        }
    }

    if ($fixtureTotalBytes -gt $maxFixtureTotalBytes) {
        Add-Failure "tests/fixtures exceeds $maxFixtureTotalBytes bytes total: $fixtureTotalBytes"
    }
}

$unexpectedDiagnosticExtensions = @('.oos', '.tok', '.log', '.dmp', '.dump')
$unexpectedDiagnostics = $trackedFiles | Where-Object {
    $extension = [System.IO.Path]::GetExtension($_.RelativePath)
    if (-not $unexpectedDiagnosticExtensions.Contains($extension.ToLowerInvariant())) {
        return $false
    }

    return -not (Test-PathAllowed -RelativePath $_.RelativePath -AllowedPrefixes $allowedDiagnosticPrefixes)
}
foreach ($match in $unexpectedDiagnostics) {
    Add-Failure "Unexpected diagnostic artifact outside allowed fixtures: $($match.RelativePath)"
}

$screenshotsRoot = Join-Path $repoRoot 'assets\screenshots'
if (Test-Path -LiteralPath $screenshotsRoot -PathType Container) {
    $screenshotFiles = Get-ChildItem -LiteralPath $screenshotsRoot -File
    foreach ($file in $screenshotFiles) {
        if ($allowedScreenshotFiles -notcontains $file.Name) {
            Add-Failure "Unexpected screenshot file in assets/screenshots: $($file.Name)"
        }
    }

    foreach ($requiredName in $allowedScreenshotFiles) {
        $requiredPath = Join-Path $screenshotsRoot $requiredName
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            Add-Failure "Missing required screenshot file: assets/screenshots/$requiredName"
        }
    }
}

$privacyRules = @(
    @{
        Name = 'Windows user profile path'
        Pattern = '(?i)(?:[A-Z]:/|[A-Z]:\\)Users[/\\][A-Za-z0-9._ -]+'
        ExcludedPrefixes = @(
            'README.md',
            'docs/',
            'source/',
            'tests/ReadOnlyScanHarness.cs',
            'tests/WorkflowQuarantineHarness.cs',
            'tests/WorkflowRepairHarness.cs',
            'scripts/test.ps1'
        )
    },
    @{
        Name = 'Private IPv4 address'
        Pattern = '\b(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[0-1])\.\d{1,3}\.\d{1,3})\b'
        ExcludedPrefixes = @()
    },
    @{
        Name = 'Machine/session identifier'
        Pattern = '(?i)\b(?:machineid|sessionid|session_id|machine_id)[=:][A-Za-z0-9_-]{4,}\b'
        ExcludedPrefixes = @()
    }
)

$textExtensions = @(
    '.cs', '.csproj', '.sln', '.ps1', '.md', '.txt', '.json', '.yml', '.yaml', '.xml', '.config', '.ini', '.gitignore', '.gitattributes'
)

foreach ($file in $trackedFiles) {
    $extension = [System.IO.Path]::GetExtension($file.RelativePath).ToLowerInvariant()
    $name = [System.IO.Path]::GetFileName($file.RelativePath)
    $isKnownText = $textExtensions -contains $extension -or $name -in @('LICENSE')
    if (-not $isKnownText) {
        continue
    }

    if (Test-TextLooksBinary -Path $file.FullPath) {
        continue
    }

    $content = Get-Content -LiteralPath $file.FullPath -Raw

    foreach ($rule in $privacyRules) {
        if (Test-PathAllowed -RelativePath $file.RelativePath -AllowedPrefixes $rule.ExcludedPrefixes) {
            continue
        }

        if ($content -match $rule.Pattern) {
            Add-Failure "Potential privacy leak ($($rule.Name)) in $($file.RelativePath)"
        }
    }

    if (Test-LooksLikeLfsPointer -Path $file.FullPath) {
        Add-Failure "Git LFS pointer file is not allowed: $($file.RelativePath)"
    }
}

$gitattributesPath = Join-Path $repoRoot '.gitattributes'
if (Test-Path -LiteralPath $gitattributesPath -PathType Leaf) {
    $gitattributes = Get-Content -LiteralPath $gitattributesPath
    foreach ($line in $gitattributes) {
        if ($line -match '(?i)\bfilter\s*=\s*lfs\b' -or $line -match '(?i)\bdiff\s*=\s*lfs\b' -or $line -match '(?i)\bmerge\s*=\s*lfs\b') {
            Add-Failure '.gitattributes enables Git LFS, which this repository forbids.'
            break
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host 'Repository cleanliness checks failed:' -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host " - $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host 'Repository cleanliness checks passed.' -ForegroundColor Green
