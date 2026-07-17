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
    @{ Path = "source/AppConfig.cs"; Rule = "File.Delete"; LinePattern = '^File\.Delete\((path|sourceFile)\);$'; Reason = "Config migration and explicit cleanup of app-owned files." },
    @{ Path = "source/AppConfig.cs"; Rule = "File.Copy"; LinePattern = '^File\.Copy\((sourceFile, tempFile, false|source, dest, true)\);$'; Reason = "Portable/non-portable config migration for app-owned files." },
    @{ Path = "source/AppConfig.cs"; Rule = "File.Move"; LinePattern = '^File\.Move\(tempFile, targetFile\);$'; Reason = "Verified portable migration commits a temporary copy without overwrite." },
    @{ Path = "source/AppConfig.cs"; Rule = "Directory.Delete"; LinePattern = '^Directory\.Delete\(path, false\);$'; Reason = "Portable/non-portable app-owned directory cleanup." },
    @{ Path = "source/AppConfig.cs"; Rule = "File.AppendAllText"; Reason = "Incident/export summary append for app-owned logs." },
    @{ Path = "source/AppConfig.cs"; Rule = "File.WriteAllText"; Reason = "App-owned export summary output." },
    @{ Path = "source/AppConfig.cs"; Rule = "Process.Start"; LinePattern = '^Process\.Start\("explorer\.exe", exportDir\);$'; Reason = "Explorer shell open for user-selected export folders." },
    @{ Path = "source/Cleanup.cs"; Rule = "File.WriteAllText"; Reason = "App-owned cleanup report output." },
    @{ Path = "source/GameSettings.cs"; Rule = "File.ReadAllText"; Reason = "Reads CK3 config text before controlled rewrite." },
    @{ Path = "source/GameSettings.cs"; Rule = "File.WriteAllText"; Reason = "Writes CK3 config text under restore tracking." },
    @{ Path = "source/Helpers.cs"; Rule = "File.Copy"; LinePattern = '^File\.Copy\((path, (UniquePath\(dest\)|dest), true|file, (dest|Path\.Combine\(destDir, Path\.GetFileName\(file\)\)), false)\);$'; Reason = "Shared copy helpers for restore-backed app operations." },
    @{ Path = "source/Helpers.cs"; Rule = "File.Move"; LinePattern = '^File\.Move\((source|path), dest\);$'; Reason = "Shared quarantine/move helpers for app-managed paths." },
    @{ Path = "source/Helpers.cs"; Rule = "Directory.Move"; LinePattern = '^Directory\.Move\((source|path), dest\);$'; Reason = "Shared quarantine/move helpers for app-managed directories." },
    @{ Path = "source/Helpers.cs"; Rule = "File.ReadAllText"; Reason = "Reads app-owned config and log files." },
    @{ Path = "source/Helpers.cs"; Rule = "Process.Start"; LinePattern = '^Process\.Start\("explorer\.exe", .+\);$'; Reason = "Explorer shell open for local folders/files only." },
    @{ Path = "source/Helpers.cs"; Rule = "File.Delete"; LinePattern = '^File\.Delete\(file\);$'; Reason = "Cleanup of app-owned temp/live-log artifacts." },
    @{ Path = "source/Helpers.cs"; Rule = "Directory.Delete"; LinePattern = '^Directory\.Delete\((dir, true|liveLogsDir, false|root, true)\);$'; Reason = "Cleanup of app-owned temp/live-log directories." },
    @{ Path = "source/Helpers.cs"; Rule = "File.WriteAllText"; Reason = "Live-log persistence to app-owned files." },
    @{ Path = "source/Helpers.cs"; Rule = "File.AppendAllText"; Reason = "Buffered live-log append to app-owned files." },
    @{ Path = "source/Launchers.cs"; Rule = "File.ReadAllText"; Reason = "Reads launcher and Steam config text before targeted edits." },
    @{ Path = "source/Launchers.cs"; Rule = "File.WriteAllText"; Reason = "Writes launcher and Steam config text under restore tracking." },
    @{ Path = "source/MainWindow.cs"; Rule = "Process.Start"; LinePattern = '^Process\.Start\("explorer\.exe", dir\);$'; Reason = "Explorer shell open for local folder selected by the UI." },
    @{ Path = "source/Network.cs"; Rule = "File.ReadAllText"; Reason = "Reads app-owned continue marker text." },
    @{ Path = "source/OosDeepAnalysis.cs"; Rule = "SearchOption.AllDirectories"; Reason = "Recursive OOS fixture/report enumeration under user-selected OOS root." },
    @{ Path = "source/Readiness.cs"; Rule = "File.WriteAllText"; Reason = "Writes readiness reports under app-owned report paths." },
    @{ Path = "source/Readiness.cs"; Rule = "ReadToEnd"; Reason = "Consumes bounded process/stdout or text-reader content for diagnostics." },
    @{ Path = "source/Readiness.cs"; Rule = "File.ReadAllText"; Reason = "Reads config/report text for readiness checks." },
    @{ Path = "source/Readiness.cs"; Rule = "File.ReadAllBytes"; Reason = "Reads bounded binary signatures for validation." },
    @{ Path = "source/Readiness.cs"; Rule = "Process.Start"; LinePattern = '^using \(Process p = Process\.Start\(psi\)\)$'; Reason = "Starts tightly-scoped diagnostic subprocesses." },
    @{ Path = "source/Reports.cs"; Rule = "SearchOption.AllDirectories"; Reason = "Recursive OOS metadata discovery under CK3/OOS roots." },
    @{ Path = "source/Reports.cs"; Rule = "File.ReadAllText"; Reason = "Reads report and metadata text for summaries." },
    @{ Path = "source/Reports.cs"; Rule = "File.Copy"; LinePattern = '^File\.Copy\(source, dest, true\);$'; Reason = "Copies report artifacts into app-owned export bundles." },
    @{ Path = "source/Restore.cs"; Rule = "File.Copy"; LinePattern = '^File\.Copy\((path, dest, true|entry\.BackupPath, tempPath, false)\);$'; Reason = "Restore backup and verified staging copy for allowlisted paths." },
    @{ Path = "source/Restore.cs"; Rule = "SearchOption.AllDirectories"; Reason = "Restore tree comparison over already-validated backup trees." },
    @{ Path = "source/Restore.cs"; Rule = "File.ReadAllText"; Reason = "Diff/readback for restore diagnostics." },
    @{ Path = "source/Restore.cs"; Rule = "File.Delete"; LinePattern = '^File\.Delete\((removed\.BackupPath|entry\.SourcePath)\);$'; Reason = "Restore execution deletes only revalidated allowlisted file targets." },
    @{ Path = "source/Restore.cs"; Rule = "Directory.Delete"; LinePattern = '^Directory\.Delete\((removed\.BackupPath|entry\.SourcePath), true\);$'; Reason = "Restore execution deletes only revalidated allowlisted directory targets." },
    @{ Path = "source/Review.cs"; Rule = "File.ReadAllText"; Reason = "Review reads current file state for before-apply preview only." },
    @{ Path = "source/SaveAnalysis.cs"; Rule = "File.Copy"; LinePattern = '^File\.Copy\(workflowSelectedSavePath, baselinePath, true\);$'; Reason = "Copies save baseline/export artifacts into app-owned work paths." },
    @{ Path = "source/SaveAnalysis.cs"; Rule = "File.ReadAllBytes"; Reason = "Reads bounded save payloads for repair/analysis." },
    @{ Path = "source/SaveAnalysis.cs"; Rule = "ReadToEnd"; Reason = "Consumes already-bounded archive/text entry content." },
    @{ Path = "source/SaveAnalysis.cs"; Rule = "File.WriteAllText"; Reason = "Writes app-owned rehost/export index files." },
    @{ Path = "source/SaveAnalysis.cs"; Rule = "Process.Start"; LinePattern = '^Process\.Start\("explorer\.exe", exportDir\);$'; Reason = "Explorer shell open for local export folder." },
    @{ Path = "source/Start.cs"; Rule = "Process.Start"; LinePattern = '^Process\.Start\(info\);$'; Reason = "UAC relaunch entrypoint." },
    @{ Path = "source/SystemRestore.cs"; Rule = "Process.Start"; LinePattern = '^using \(Process process = Process\.Start\(psi\)\)$'; Reason = "Starts tightly-scoped PowerShell subprocess for system restore operations." },
    @{ Path = "source/Updates.cs"; Rule = "Process.Start"; LinePattern = '^Process\.Start\(info\);$'; Reason = "Starts only the staged updater executable after exact release endpoint and metadata validation." },
    @{ Path = "source/Updates.cs"; Rule = "File.Copy"; LinePattern = '^File\.Copy\(Application\.ExecutablePath, updaterCopy, false\);$'; Reason = "Copies the currently running signed application into app-owned staging as the separate updater process." },
    @{ Path = "source/Updates.cs"; Rule = "File.ReadAllText"; Reason = "Reads the downloaded checksum from canonical app-owned staging." },
    @{ Path = "source/Updates.cs"; Rule = "Directory.Delete"; LinePattern = '^Directory\.Delete\(directory, true\);$'; Reason = "Deletes only stale app-owned updater staging directories older than seven days." },
    @{ Path = "source/SafeUpdater.cs"; Rule = "File.WriteAllText"; Reason = "Writes the health token only inside the validated updater transaction root." },
    @{ Path = "source/SafeUpdater.cs"; Rule = "File.ReadAllText"; Reason = "Reads the updater health token from the validated transaction root." },
    @{ Path = "source/SafeUpdater.cs"; Rule = "File.Move"; LinePattern = '(^File\.Move\((source|backup|temporary), (destination|path)\);$|^if \(File\.Exists\(path\)\) File\.Replace\(temporary, path, null, true\); else File\.Move\(temporary, path\);$)'; Reason = "Commits new app files, restores same-volume backups, or atomically commits updater journals." },
    @{ Path = "source/SafeUpdater.cs"; Rule = "File.Delete"; LinePattern = '^File\.Delete\(destination\);$'; Reason = "Rollback removes only a newly-created manifest-declared app file." },
    @{ Path = "source/SafeUpdater.cs"; Rule = "Directory.Delete"; LinePattern = '^try \{ if \(Directory\.Exists\(path\)\) Directory\.Delete\(path, true\); \} catch \{ \}$'; Reason = "Cleans only the canonical app-owned update transaction directory after successful health check." },
    @{ Path = "source/SafeUpdater.cs"; Rule = "Process.Start"; LinePattern = '^(Process process = Process\.Start\(info\);|Process\.Start\(info\);)$'; Reason = "Starts only the newly verified CK3MPS health-check process or final application process." },
    @{ Path = "source/TransactionalOperations.cs"; Rule = "File.Copy"; LinePattern = '^File\.Copy\((source, staged|staged, target), false\);$'; Reason = "Copies validated state into staging or commits a verified staged file to a prepared migration target." },
    @{ Path = "source/TransactionalOperations.cs"; Rule = "File.Move"; LinePattern = '^File\.Move\((staged, target|target, backup|backup, target)\);$'; Reason = "Commits verified staged files and performs same-volume backup or rollback renames for validated migration targets." },
    @{ Path = "source/TransactionalOperations.cs"; Rule = "File.Delete"; LinePattern = '^File\.Delete\((target|source|path)\);$'; Reason = "Rolls back created targets, cleans committed source files, or removes app-owned journals." },
    @{ Path = "source/TransactionalOperations.cs"; Rule = "Directory.Delete"; LinePattern = '^Directory\.Delete\((path, (true|false)|current, false)\);$'; Reason = "Cleans only validated staging or empty app-state directories." },
    @{ Path = "source/RestoreTransactions.cs"; Rule = "File.Copy"; LinePattern = '^File\.Copy\((manifest, manifestBackup|normalized, record\.BackupPath|backupPath, temp), false\);$'; Reason = "Captures and verifies rollback snapshots inside the app-owned transaction root." },
    @{ Path = "source/RestoreTransactions.cs"; Rule = "File.Delete"; LinePattern = '^File\.Delete\((target|manifest)\);$'; Reason = "Removes only revalidated restore targets or a newly-created manifest during rollback." },
    @{ Path = "source/RestoreTransactions.cs"; Rule = "Directory.Move"; LinePattern = '^Directory\.Move\((target, rollback|stage, target|rollback, target)\);$'; Reason = "Uses same-parent renames for atomic directory commit and rollback." },
    @{ Path = "source/RestoreTransactions.cs"; Rule = "Directory.Delete"; LinePattern = '^Directory\.Delete\((target|rollback|stage|path), true\);$|^Directory\.Delete\(parent, false\);$'; Reason = "Cleans validated restore staging, rollback, or empty transaction directories." },
    @{ Path = "source/Utilities.cs"; Rule = "File.Move"; LinePattern = '^File\.Move\((tempPath, targetPath|source, destination)\);$'; Reason = "SafeAtomicFile commit and bounded history rotation use same-directory rename." },
    @{ Path = "source/Utilities.cs"; Rule = "File.Delete"; LinePattern = '^File\.Delete\((path|destination)\);$'; Reason = "SafeAtomicFile temp cleanup and bounded history rotation." },
    @{ Path = "source/Utilities.cs"; Rule = "File.ReadAllText"; LinePattern = '^string existing = expected\.Exists \? File\.ReadAllText\(target, effectiveEncoding\) : "";$'; Reason = "Append reads only the bounded current history file after snapshot verification." },
    @{ Path = "source/Workflow.cs"; Rule = "File.Move"; LinePattern = '^File\.Move\((normalizedPath, destinationPath|destinationPath, normalizedPath|tempPath, copyPath)\);$'; Reason = "Workflow quarantine/duplicate operations for validated workflow saves." },
    @{ Path = "source/Workflow.cs"; Rule = "File.ReadAllText"; Reason = "Reads workflow text artifacts and parity manifest text." },
    @{ Path = "source/Workflow.cs"; Rule = "Process.Start"; LinePattern = '^Process\.Start\("explorer\.exe", .+\);$'; Reason = "Explorer shell open for local files/folders and packs." },
    @{ Path = "source/Workflow.cs"; Rule = "File.WriteAllText"; Reason = "Writes app-owned workflow/parity/export reports." },
    @{ Path = "source/Workflow.cs"; Rule = "File.Copy"; LinePattern = '^File\.Copy\((workflowSelectedSavePath, baselinePath, true|normalizedPath, tempPath, false)\);$'; Reason = "Workflow duplicate/baseline copy operations on validated saves." },
    @{ Path = "source/Workflow.cs"; Rule = "File.Delete"; LinePattern = '^try \{ File\.Delete\(tempPath\); \} catch \{ \}$'; Reason = "Workflow temp-file cleanup only." },
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

function Is-Allowlisted($RelativePath, $RuleName, $Line) {
    $sitePatternRequired = $RuleName -in @("File.Delete", "Directory.Delete", "File.Move", "Directory.Move", "File.Copy", "Process.Start", "ExecutionPolicy Bypass")
    foreach ($entry in $Allowlist) {
        if ($entry.Path -ieq $RelativePath -and $entry.Rule -eq $RuleName) {
            if ($sitePatternRequired -and -not $entry.ContainsKey('LinePattern')) {
                continue
            }
            if ($entry.ContainsKey('LinePattern') -and $Line -notmatch $entry.LinePattern) {
                continue
            }
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
                $allow = Is-Allowlisted -RelativePath $relativePath -RuleName $rule.Name -Line $hit.Line.Trim()
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
