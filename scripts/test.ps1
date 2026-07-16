$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$OutDir = Join-Path $Root "bin\tests"
$TestExe = Join-Path $OutDir "CK3MPS.UtilityTests.exe"
$Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

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
@'
Add-Type -AssemblyName System.Windows.Forms
$root = Resolve-Path "."
$sample = Join-Path $root "_oos_extract_2\f086d9a587b17fed_0_3"
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
'@ | powershell -NoProfile -
    if ($LASTEXITCODE -ne 0) {
        throw "Deep OOS smoke test failed with exit code $LASTEXITCODE"
    }

@'
Add-Type -AssemblyName System.Windows.Forms
$root = Resolve-Path "."
$sample = Join-Path $root "_oos_extract_2\f086d9a587b17fed_0_3"
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
'@ | powershell -NoProfile -
    if ($LASTEXITCODE -ne 0) {
        throw "Incident history smoke test failed with exit code $LASTEXITCODE"
    }
}
