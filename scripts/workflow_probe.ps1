$ErrorActionPreference = "Stop"

$env:CK3MPS_SKIP_ELEVATION = "1"

Add-Type -AssemblyName System.Windows.Forms

$assemblyPath = (Resolve-Path ".\bin\CK3MPS.exe").Path
$asm = [Reflection.Assembly]::LoadFrom($assemblyPath)
$formType = $asm.GetType("CK3MPS.MainForm", $true)
$form = [Activator]::CreateInstance($formType)
$flags = [Reflection.BindingFlags] "Instance,NonPublic"

$methods = @(
    "AnalyzeBestHostSaveCandidate",
    "AnalyzeHostSuitability",
    "BuildLocalParityFingerprint",
    "DetectSteamBranch",
    "FindLatestOosMetadataFile"
)

foreach ($name in $methods)
{
    $method = $formType.GetMethod($name, $flags)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Write-Output ("METHOD_START " + $name)
    $value = $method.Invoke($form, @())
    $sw.Stop()
    $text = if ($null -eq $value) { "(null)" } else { $value.ToString() }
    Write-Output ("METHOD_DONE " + $name + " " + $sw.ElapsedMilliseconds + "ms " + $text)
}

$form.Dispose()
