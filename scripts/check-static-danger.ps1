$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir

$Targets = @(
    (Join-Path $Root "source"),
    (Join-Path $Root "scripts")
)

$ExcludedRelativePaths = @(
    "scripts/check-static-danger.ps1"
)

$DangerRules = @(
    @{ Name = "File.Delete"; Pattern = '\bFile\.Delete\s*\(' },
    @{ Name = "Directory.Delete"; Pattern = '\bDirectory\.Delete\s*\(' },
    @{ Name = "File.Move"; Pattern = '\bFile\.Move\s*\(' },
    @{ Name = "Directory.Move"; Pattern = '\bDirectory\.Move\s*\(' },
    @{ Name = "File.Copy"; Pattern = '\bFile\.Copy\s*\(' },
    @{ Name = "File.WriteAllText"; Pattern = '\bFile\.WriteAllText\s*\(' },
    @{ Name = "File.AppendAllText"; Pattern = '\bFile\.AppendAllText\s*\(' },
    @{ Name = '\[System.IO.File\]::ReadAllBytes'; Pattern = '\[System\.IO\.File\]::ReadAllBytes\s*\(' },
    @{ Name = "File.ReadAllBytes"; Pattern = '\bFile\.ReadAllBytes\s*\(' },
    @{ Name = "File.ReadAllText"; Pattern = '\bFile\.ReadAllText\s*\(' },
    @{ Name = "ReadToEnd"; Pattern = '\bReadToEnd\s*\(' },
    @{ Name = "SearchOption.AllDirectories"; Pattern = '\bSearchOption\.AllDirectories\b' },
    @{ Name = "Process.Start"; Pattern = '\bProcess\.Start\s*\(' },
    @{ Name = "ExecutionPolicy Bypass"; Pattern = 'ExecutionPolicy Bypass' }
)

$Allowlist = @(
    @{ Path = "source/AppConfig.cs"; Rule = "File.Delete"; Reason = "Config migration and explicit cleanup of app-owned files." },
    @{ Path = "source/AppConfig.cs"; Rule = "File.Copy"; Reason = "Portable/non-portable config migration for app-owned files." },
    @{ Path = "source/AppConfig.cs"; Rule = "Directory.Delete"; Reason = "Portable/non-portable app-owned directory cleanup." },
    @{ Path = "source/AppConfig.cs"; Rule = "File.AppendAllText"; Reason = "Incident/export summary append for app-owned logs." },
    @{ Path = "source/AppConfig.cs"; Rule = "File.WriteAllText"; Reason = "App-owned export summary output." },
    @{ Path = "source/AppConfig.cs"; Rule = "Process.Start"; Reason = "Explorer shell open for user-selected export folders." },
    @{ Path = "source/Cleanup.cs"; Rule = "File.WriteAllText"; Reason = "App-owned cleanup report output." },
    @{ Path = "source/GameSettings.cs"; Rule = "File.ReadAllText"; Reason = "Reads CK3 config text before controlled rewrite." },
    @{ Path = "source/GameSettings.cs"; Rule = "File.WriteAllText"; Reason = "Writes CK3 config text under restore tracking." },
    @{ Path = "source/Helpers.cs"; Rule = "File.Copy"; Reason = "Shared copy helpers for restore-backed app operations." },
    @{ Path = "source/Helpers.cs"; Rule = "File.Move"; Reason = "Shared quarantine/move helpers for app-managed paths." },
    @{ Path = "source/Helpers.cs"; Rule = "Directory.Move"; Reason = "Shared quarantine/move helpers for app-managed directories." },
    @{ Path = "source/Helpers.cs"; Rule = "File.ReadAllText"; Reason = "Reads app-owned config and log files." },
    @{ Path = "source/Helpers.cs"; Rule = "Process.Start"; Reason = "Explorer shell open for local folders/files only." },
    @{ Path = "source/Helpers.cs"; Rule = "File.Delete"; Reason = "Cleanup of app-owned temp/live-log artifacts." },
    @{ Path = "source/Helpers.cs"; Rule = "Directory.Delete"; Reason = "Cleanup of app-owned temp/live-log directories." },
    @{ Path = "source/Helpers.cs"; Rule = "File.WriteAllText"; Reason = "Live-log persistence to app-owned files." },
    @{ Path = "source/Helpers.cs"; Rule = "File.AppendAllText"; Reason = "Buffered live-log append to app-owned files." },
    @{ Path = "source/Launchers.cs"; Rule = "File.ReadAllText"; Reason = "Reads launcher and Steam config text before targeted edits." },
    @{ Path = "source/Launchers.cs"; Rule = "File.WriteAllText"; Reason = "Writes launcher and Steam config text under restore tracking." },
    @{ Path = "source/MainWindow.cs"; Rule = "Process.Start"; Reason = "Explorer shell open for local folder selected by the UI." },
    @{ Path = "source/Network.cs"; Rule = "File.ReadAllText"; Reason = "Reads app-owned continue marker text." },
    @{ Path = "source/OosDeepAnalysis.cs"; Rule = "SearchOption.AllDirectories"; Reason = "Recursive OOS fixture/report enumeration under user-selected OOS root." },
    @{ Path = "source/Readiness.cs"; Rule = "File.WriteAllText"; Reason = "Writes readiness reports under app-owned report paths." },
    @{ Path = "source/Readiness.cs"; Rule = "ReadToEnd"; Reason = "Consumes bounded process/stdout or text-reader content for diagnostics." },
    @{ Path = "source/Readiness.cs"; Rule = "File.ReadAllText"; Reason = "Reads config/report text for readiness checks." },
    @{ Path = "source/Readiness.cs"; Rule = "File.ReadAllBytes"; Reason = "Reads bounded binary signatures for validation." },
    @{ Path = "source/Readiness.cs"; Rule = "Process.Start"; Reason = "Starts tightly-scoped diagnostic subprocesses." },
    @{ Path = "source/Reports.cs"; Rule = "SearchOption.AllDirectories"; Reason = "Recursive OOS metadata discovery under CK3/OOS roots." },
    @{ Path = "source/Reports.cs"; Rule = "File.ReadAllText"; Reason = "Reads report and metadata text for summaries." },
    @{ Path = "source/Reports.cs"; Rule = "File.Copy"; Reason = "Copies report artifacts into app-owned export bundles." },
    @{ Path = "source/Restore.cs"; Rule = "File.Copy"; Reason = "Restore backup and restore execution for allowlisted paths." },
    @{ Path = "source/Restore.cs"; Rule = "SearchOption.AllDirectories"; Reason = "Restore tree comparison over already-validated backup trees." },
    @{ Path = "source/Restore.cs"; Rule = "File.ReadAllText"; Reason = "Diff/readback for restore diagnostics." },
    @{ Path = "source/Restore.cs"; Rule = "File.Delete"; Reason = "Restore execution deletes only revalidated allowlisted file targets." },
    @{ Path = "source/Restore.cs"; Rule = "Directory.Delete"; Reason = "Restore execution deletes only revalidated allowlisted directory targets." },
    @{ Path = "source/Review.cs"; Rule = "File.ReadAllText"; Reason = "Review reads current file state for before-apply preview only." },
    @{ Path = "source/SaveAnalysis.cs"; Rule = "File.Copy"; Reason = "Copies save baseline/export artifacts into app-owned work paths." },
    @{ Path = "source/SaveAnalysis.cs"; Rule = "File.ReadAllBytes"; Reason = "Reads bounded save payloads for repair/analysis." },
    @{ Path = "source/SaveAnalysis.cs"; Rule = "ReadToEnd"; Reason = "Consumes already-bounded archive/text entry content." },
    @{ Path = "source/SaveAnalysis.cs"; Rule = "File.WriteAllText"; Reason = "Writes app-owned rehost/export index files." },
    @{ Path = "source/SaveAnalysis.cs"; Rule = "Process.Start"; Reason = "Explorer shell open for local export folder." },
    @{ Path = "source/Start.cs"; Rule = "Process.Start"; Reason = "UAC relaunch entrypoint." },
    @{ Path = "source/SystemRestore.cs"; Rule = "ExecutionPolicy Bypass"; Reason = "Legacy privileged PowerShell bridge; tracked until hardening/removal lands." },
    @{ Path = "source/SystemRestore.cs"; Rule = "Process.Start"; Reason = "Starts tightly-scoped PowerShell subprocess for system restore operations." },
    @{ Path = "source/Updates.cs"; Rule = "Process.Start"; Reason = "Opens vetted release page and legacy updater process path." },
    @{ Path = "source/Utilities.cs"; Rule = "File.Move"; Reason = "SafeAtomicFile commit path uses temp-file rename." },
    @{ Path = "source/Utilities.cs"; Rule = "File.Delete"; Reason = "SafeAtomicFile temp cleanup on failed atomic write." },
    @{ Path = "source/Workflow.cs"; Rule = "File.Move"; Reason = "Workflow quarantine/duplicate operations for validated workflow saves." },
    @{ Path = "source/Workflow.cs"; Rule = "File.ReadAllText"; Reason = "Reads workflow text artifacts and parity manifest text." },
    @{ Path = "source/Workflow.cs"; Rule = "Process.Start"; Reason = "Explorer shell open for local files/folders and packs." },
    @{ Path = "source/Workflow.cs"; Rule = "File.WriteAllText"; Reason = "Writes app-owned workflow/parity/export reports." },
    @{ Path = "source/Workflow.cs"; Rule = "File.Copy"; Reason = "Workflow duplicate/baseline copy operations on validated saves." },
    @{ Path = "source/Workflow.cs"; Rule = "File.Delete"; Reason = "Workflow temp-file cleanup only." },
    @{ Path = "source/Workflow.cs"; Rule = "ReadToEnd"; Reason = "Consumes bounded HTTP/text response content for workflow diagnostics." },
    @{ Path = "scripts/check-repo-clean.ps1"; Rule = "\[System.IO.File\]::ReadAllBytes"; Reason = "Small binary sniff for repo-clean privacy/LFS checks." }
)

function Get-RelativePath([string]$Path) {
    $fullRoot = (Resolve-Path -LiteralPath $Root).Path
    $fullPath = (Resolve-Path -LiteralPath $Path).Path

    if ($fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return ($fullPath.Substring($fullRoot.Length).TrimStart('\') -replace '\\', '/')
    }

    return ($Path -replace '\\', '/')
}

function Is-Allowlisted($RelativePath, $RuleName) {
    foreach ($entry in $Allowlist) {
        if ($entry.Path -ieq $RelativePath -and $entry.Rule -eq $RuleName) {
            return $entry
        }
    }

    return $null
}

$matches = New-Object System.Collections.Generic.List[object]

foreach ($target in $Targets) {
    if (-not (Test-Path -LiteralPath $target)) {
        continue
    }

    $files = Get-ChildItem -LiteralPath $target -Recurse -File | Where-Object {
        $_.Extension -in @(".cs", ".ps1")
    }

    foreach ($file in $files) {
        $relativePath = Get-RelativePath $file.FullName
        if ($ExcludedRelativePaths -contains $relativePath) {
            continue
        }

        foreach ($rule in $DangerRules) {
            $hits = Select-String -Path $file.FullName -Pattern $rule.Pattern
            foreach ($hit in $hits) {
                $allow = Is-Allowlisted -RelativePath $relativePath -RuleName $rule.Name
                $matches.Add([pscustomobject]@{
                    RelativePath = $relativePath
                    LineNumber = $hit.LineNumber
                    Rule = $rule.Name
                    Line = $hit.Line.Trim()
                    Allowed = [bool]$allow
                    Reason = if ($allow) { $allow.Reason } else { "" }
                })
            }
        }
    }
}

$unexpected = $matches | Where-Object { -not $_.Allowed }

if ($unexpected.Count -gt 0) {
    Write-Host "Static danger check failed:" -ForegroundColor Red
    foreach ($item in $unexpected | Sort-Object RelativePath, LineNumber, Rule) {
        Write-Host (" - {0}:{1} [{2}] {3}" -f $item.RelativePath, $item.LineNumber, $item.Rule, $item.Line) -ForegroundColor Red
    }
    exit 1
}

$allowedCount = ($matches | Measure-Object).Count
Write-Host ("Static danger check passed. Reviewed matches: {0}" -f $allowedCount) -ForegroundColor Green
