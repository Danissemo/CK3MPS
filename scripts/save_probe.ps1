$ErrorActionPreference = "Stop"

$env:CK3MPS_SKIP_ELEVATION = "1"

Add-Type -AssemblyName System.Windows.Forms

$assemblyPath = (Resolve-Path ".\bin\CK3MPS.exe").Path
$asm = [Reflection.Assembly]::LoadFrom($assemblyPath)
$formType = $asm.GetType("CK3MPS.MainForm", $true)
$form = [Activator]::CreateInstance($formType)
$flags = [Reflection.BindingFlags] "Instance,NonPublic"

$analyze = $formType.GetMethod("AnalyzeHostSaveCandidate", $flags)
$saveDir = "C:\Users\Asuka\Documents\Paradox Interactive\Crusader Kings III\save games"
$paths = Get-ChildItem $saveDir -Filter *.ck3 | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 12 -ExpandProperty FullName

foreach ($path in $paths)
{
    $pathText = [string]$path
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Write-Output ("SAVE_START " + [IO.Path]::GetFileName($pathText))
    $result = $analyze.Invoke($form, @($pathText))
    $sw.Stop()
    $resultType = $result.GetType()
    $scoreField = $resultType.GetField("Score")
    $verdictField = $resultType.GetField("Verdict")
    $score = if ($scoreField) { $scoreField.GetValue($result) } else { "(no-score-field)" }
    $verdict = if ($verdictField) { $verdictField.GetValue($result) } else { "(no-verdict-field)" }
    Write-Output ("SAVE_DONE " + [IO.Path]::GetFileName($pathText) + " " + $sw.ElapsedMilliseconds + "ms score=" + $score + " verdict=" + $verdict)
}

$form.Dispose()
