$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$OutDir = Join-Path $Root "bin\tests"
$TestExe = Join-Path $OutDir "CK3MPS.UtilityTests.exe"
$ReadOnlyScanExe = Join-Path $OutDir "CK3MPS.ReadOnlyScanHarness.exe"
$WorkflowQuarantineExe = Join-Path $OutDir "CK3MPS.WorkflowQuarantineHarness.exe"
$RestoreManifestExe = Join-Path $OutDir "CK3MPS.RestoreManifestHarness.exe"
$SaveRepairExe = Join-Path $OutDir "CK3MPS.SaveRepairHarness.exe"
$OosWatcherExe = Join-Path $OutDir "CK3MPS.OosWatcherHarness.exe"
$SettingsGuardExe = Join-Path $OutDir "CK3MPS.SettingsGuardHarness.exe"
$UpdatesExe = Join-Path $OutDir "CK3MPS.UpdatesHarness.exe"
$SteamConfigBlockerExe = Join-Path $OutDir "CK3MPS.SteamConfigBlockerHarness.exe"
$RestorePointOwnershipExe = Join-Path $OutDir "CK3MPS.RestorePointOwnershipHarness.exe"
$ParityRoomConsentExe = Join-Path $OutDir "CK3MPS.ParityRoomConsentHarness.exe"
$ParityRoomSecurityExe = Join-Path $OutDir "CK3MPS.ParityRoomSecurityHarness.exe"
$ParityRoomSlowClientExe = Join-Path $OutDir "CK3MPS.ParityRoomSlowClientHarness.exe"
$RestorePointUiExe = Join-Path $OutDir "CK3MPS.RestorePointUiHarness.exe"
$WorkflowDuplicateExe = Join-Path $OutDir "CK3MPS.WorkflowDuplicateHarness.exe"
$WorkflowCrashRecoveryExe = Join-Path $OutDir "CK3MPS.WorkflowCrashRecoveryHarness.exe"
$Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Invoke-PowerShellSnippet {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptText,
        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    $wrappedScript = '$ProgressPreference=''SilentlyContinue''; $ErrorActionPreference=''Stop''; ' + $ScriptText
    $bytes = [System.Text.Encoding]::Unicode.GetBytes($wrappedScript)
    $encoded = [Convert]::ToBase64String($bytes)
    & powershell -NoProfile -NonInteractive -EncodedCommand $encoded
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)"
    }
}

if (-not (Test-Path $Csc)) {
    throw "C# compiler was not found: $Csc"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

& $Csc /nologo `
    /target:exe `
    /out:"$TestExe" `
    /r:System.dll `
    (Join-Path $Root "source\Utilities.cs") `
    (Join-Path $Root "source\RuntimeModeUtilities.cs") `
    (Join-Path $Root "source\StepCatalog.cs") `
    (Join-Path $Root "tests\UtilityTests.cs")

if ($LASTEXITCODE -ne 0) {
    throw "Test compilation failed with exit code $LASTEXITCODE"
}

& $TestExe
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE"
}

$BuiltExe = Join-Path $Root "bin\CK3MPS.exe"
if (Test-Path $BuiltExe) {
    & $Csc /nologo `
        /target:exe `
        /out:"$ReadOnlyScanExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\ReadOnlyScanHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "ReadOnlyScan harness compilation failed with exit code $LASTEXITCODE"
    }

    & $ReadOnlyScanExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "ReadOnlyScan harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$WorkflowQuarantineExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\WorkflowQuarantineHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "WorkflowQuarantine harness compilation failed with exit code $LASTEXITCODE"
    }

    & $WorkflowQuarantineExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "WorkflowQuarantine harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$RestoreManifestExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\RestoreManifestHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "RestoreManifest harness compilation failed with exit code $LASTEXITCODE"
    }

    & $RestoreManifestExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "RestoreManifest harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$SaveRepairExe" `
        /r:System.dll `
        /r:System.IO.Compression.dll `
        /r:System.IO.Compression.FileSystem.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\SaveRepairHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "SaveRepair harness compilation failed with exit code $LASTEXITCODE"
    }

    & $SaveRepairExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "SaveRepair harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$OosWatcherExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\OosWatcherHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "OosWatcher harness compilation failed with exit code $LASTEXITCODE"
    }

    & $OosWatcherExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "OosWatcher harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$SettingsGuardExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\SettingsGuardHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "SettingsGuard harness compilation failed with exit code $LASTEXITCODE"
    }

    & $SettingsGuardExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "SettingsGuard harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$UpdatesExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\UpdatesHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "Updates harness compilation failed with exit code $LASTEXITCODE"
    }

    & $UpdatesExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "Updates harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$SteamConfigBlockerExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\SteamConfigBlockerHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "SteamConfigBlocker harness compilation failed with exit code $LASTEXITCODE"
    }

    & $SteamConfigBlockerExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "SteamConfigBlocker harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$RestorePointOwnershipExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\RestorePointOwnershipHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "RestorePointOwnership harness compilation failed with exit code $LASTEXITCODE"
    }

    & $RestorePointOwnershipExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "RestorePointOwnership harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$ParityRoomConsentExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\ParityRoomConsentHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "ParityRoomConsent harness compilation failed with exit code $LASTEXITCODE"
    }

    & $ParityRoomConsentExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "ParityRoomConsent harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$ParityRoomSecurityExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\ParityRoomSecurityHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "ParityRoomSecurity harness compilation failed with exit code $LASTEXITCODE"
    }

    & $ParityRoomSecurityExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "ParityRoomSecurity harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$ParityRoomSlowClientExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\ParityRoomSlowClientHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "ParityRoomSlowClient harness compilation failed with exit code $LASTEXITCODE"
    }

    & $ParityRoomSlowClientExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "ParityRoomSlowClient harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$RestorePointUiExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\RestorePointUiHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "RestorePointUi harness compilation failed with exit code $LASTEXITCODE"
    }

    & $RestorePointUiExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "RestorePointUi harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$WorkflowDuplicateExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\WorkflowDuplicateHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "WorkflowDuplicate harness compilation failed with exit code $LASTEXITCODE"
    }

    & $WorkflowDuplicateExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "WorkflowDuplicate harness failed with exit code $LASTEXITCODE"
    }

    & $Csc /nologo `
        /target:exe `
        /out:"$WorkflowCrashRecoveryExe" `
        /r:System.dll `
        /r:System.Windows.Forms.dll `
        (Join-Path $Root "tests\WorkflowCrashRecoveryHarness.cs")
    if ($LASTEXITCODE -ne 0) {
        throw "WorkflowCrashRecovery harness compilation failed with exit code $LASTEXITCODE"
    }

    & $WorkflowCrashRecoveryExe $BuiltExe
    if ($LASTEXITCODE -ne 0) {
        throw "WorkflowCrashRecovery harness failed with exit code $LASTEXITCODE"
    }
}

if (Test-Path $BuiltExe) {
Invoke-PowerShellSnippet @'
Add-Type -AssemblyName System.Windows.Forms
$root = Resolve-Path "."
$sample = Join-Path $root "tests\fixtures\oos_smoke"
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("CK3MPS-oos-smoke-" + [guid]::NewGuid().ToString("N"))
$docs = Join-Path $temp "Crusader Kings III"
$oos = Join-Path $docs "oos\sample"
$exeCopy = Join-Path $temp "CK3MPS.exe"
New-Item -ItemType Directory -Force -Path $oos | Out-Null
Copy-Item (Join-Path $sample "oos_metadata_1.txt") (Join-Path $oos "oos_metadata_1.txt")
Copy-Item (Join-Path $sample "savegame_oos_machineid_1.oos") (Join-Path $oos "savegame_oos_machineid_1.oos")
Copy-Item (Join-Path $sample "modifiers_oos_machineid_1.oos") (Join-Path $oos "modifiers_oos_machineid_1.oos")
Copy-Item (Join-Path $sample "error_1.log") (Join-Path $oos "error_1.log")
Copy-Item (Resolve-Path ".\bin\CK3MPS.exe") $exeCopy
$asm = [Reflection.Assembly]::LoadFrom($exeCopy)
$type = $asm.GetType("CK3MPS.MainForm", $true)
$form = [Activator]::CreateInstance($type)
$flags = [Reflection.BindingFlags]"Instance, NonPublic"
$type.GetField("ck3Docs", $flags).SetValue($form, $docs)
$type.GetField("stabilizerRoot", $flags).SetValue($form, $temp)
$method = $type.GetMethod("AnalyzeLatestOosDeepInsight", $flags)
$insight = $method.Invoke($form, @())
$insightType = $insight.GetType()
$recovery = [string]$insightType.GetField("RecoveryPath").GetValue($insight)
$score = [int]$insightType.GetField("SessionContaminationScore").GetValue($insight)
$hotjoinForbidden = [bool]$insightType.GetField("HotjoinForbidden").GetValue($insight)
$findings = [System.Collections.IList]$insightType.GetField("Findings").GetValue($insight)
$chars = [int]$insightType.GetField("CharacterMentions").GetValue($insight)
$mods = [int]$insightType.GetField("ModifierMentions").GetValue($insight)
$ai = [int]$insightType.GetField("AiMentions").GetValue($insight)
if ([string]::IsNullOrWhiteSpace($recovery)) { throw "Deep OOS parser did not produce a recovery path." }
if ($score -lt 0 -or $score -gt 100) { throw "Deep OOS parser score is out of range: $score" }
if ($findings.Count -lt 1) { throw "Deep OOS parser produced no findings." }
if ($chars -lt 1 -and $mods -lt 1 -and $ai -lt 1) { throw "Deep OOS parser did not detect any OOS signals." }
Write-Output ("PASS deep OOS parser recovery path: " + $recovery)
Write-Output ("PASS deep OOS parser contamination score: " + $score)
Write-Output ("PASS deep OOS parser hotjoin forbidden flag: " + $hotjoinForbidden)
$form.Dispose()
[System.GC]::Collect()
[System.GC]::WaitForPendingFinalizers()
Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
exit 0
'@ "Deep OOS smoke test failed"

Invoke-PowerShellSnippet @'
Add-Type -AssemblyName System.Windows.Forms
$root = Resolve-Path "."
$sample = Join-Path $root "tests\fixtures\oos_smoke"
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("CK3MPS-incident-history-" + [guid]::NewGuid().ToString("N"))
$docs = Join-Path $temp "Crusader Kings III"
$oos = Join-Path $docs "oos\sample"
$exeCopy = Join-Path $temp "CK3MPS.exe"
New-Item -ItemType Directory -Force -Path $oos | Out-Null
Copy-Item (Join-Path $sample "oos_metadata_1.txt") (Join-Path $oos "oos_metadata_1.txt")
Copy-Item (Join-Path $sample "savegame_oos_machineid_1.oos") (Join-Path $oos "savegame_oos_machineid_1.oos")
Copy-Item (Join-Path $sample "modifiers_oos_machineid_1.oos") (Join-Path $oos "modifiers_oos_machineid_1.oos")
Copy-Item (Join-Path $sample "error_1.log") (Join-Path $oos "error_1.log")
Copy-Item (Resolve-Path ".\bin\CK3MPS.exe") $exeCopy
$asm = [Reflection.Assembly]::LoadFrom($exeCopy)
$type = $asm.GetType("CK3MPS.MainForm", $true)
$flags = [Reflection.BindingFlags]"Instance, NonPublic"
$form1 = [Activator]::CreateInstance($type)
$type.GetField("ck3Docs", $flags).SetValue($form1, $docs)
$type.GetField("stabilizerRoot", $flags).SetValue($form1, $temp)
$analyze = $type.GetMethod("AnalyzeOosIncidentState", $flags)
$record = $type.GetMethod("RecordIncidentHistoryEvent", $flags)
$incident1 = $analyze.Invoke($form1, @())
$record.Invoke($form1, @("test_attempt_1", $incident1, "first test attempt"))
$record.Invoke($form1, @("test_attempt_2", $incident1, "second test attempt"))
$record.Invoke($form1, @("test_attempt_3", $incident1, "third test attempt"))
$form1.Dispose()
$form2 = [Activator]::CreateInstance($type)
$type.GetField("ck3Docs", $flags).SetValue($form2, $docs)
$type.GetField("stabilizerRoot", $flags).SetValue($form2, $temp)
$incident2 = $analyze.Invoke($form2, @())
$incidentType = $incident2.GetType()
$priorAttempts = [int]$incidentType.GetField("PriorAttempts").GetValue($incident2)
$stage = [string]$incidentType.GetField("Stage").GetValue($incident2)
$escalation = [int]$incidentType.GetField("EscalationLevel").GetValue($incident2)
$responsibilities = [System.Collections.IList]$incidentType.GetField("PlayerResponsibilities").GetValue($incident2)
$historyPath = Join-Path $temp "incident_history.jsonl"
if ($priorAttempts -lt 3) { throw "Incident history was not persisted across runs." }
if ($escalation -lt 3) { throw "Incident history did not escalate the repeated incident." }
if (-not (Test-Path $historyPath)) { throw "Incident history file was not created." }
$repeatLineFound = $false
foreach ($line in $responsibilities) {
    if ([string]$line -like "*already failed*") { $repeatLineFound = $true; break }
}
if (-not $repeatLineFound) { throw "Repeated-incident responsibility guidance was not generated." }
Write-Output ("PASS incident history attempts persisted: " + $priorAttempts)
Write-Output ("PASS incident history escalation stage: " + $stage)
Write-Output ("PASS incident history escalation level: " + $escalation)
$form2.Dispose()
[System.GC]::Collect()
[System.GC]::WaitForPendingFinalizers()
Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
exit 0
'@ "Incident history smoke test failed"
}

& (Join-Path $ScriptDir "check-static-danger.ps1")
if ($LASTEXITCODE -ne 0) {
    throw "Static danger check failed with exit code $LASTEXITCODE"
}

& (Join-Path $ScriptDir "check-repo-clean.ps1")
if ($LASTEXITCODE -ne 0) {
    throw "Repository cleanliness check failed with exit code $LASTEXITCODE"
}
