$ErrorActionPreference = "Stop"

$env:CK3MPS_SKIP_ELEVATION = "1"
$env:CK3MPS_TEST_MODE = "1"

Add-Type -AssemblyName System.Windows.Forms

$assemblyPath = (Resolve-Path ".\bin\CK3MPS.exe").Path
$asm = [Reflection.Assembly]::LoadFrom($assemblyPath)
$formType = $asm.GetType("CK3MPS.MainForm", $true)
$form = [Activator]::CreateInstance($formType)
$flags = [Reflection.BindingFlags] "Instance,NonPublic"

$methods = @(
    "EnsureStabilizerRoot",
    "CreateQuarantine",
    "BackupSteamAndLauncherSettings",
    "StabilizeSteamSettings",
    "ForceNoMods",
    "StabilizePdxSettings",
    "WriteStableGameRuleProfile",
    "WriteHostSavePreparationReport",
    "WriteHostSuitabilityReport",
    "WritePreSessionPlan",
    "WriteSessionVerdictReport",
    "WriteWorkflowStatusReport",
    "WriteRehostPack"
)

$result = New-Object System.Collections.Generic.List[object]
foreach ($name in $methods)
{
    $method = $formType.GetMethod($name, $flags)
    if ($null -eq $method)
    {
        $result.Add([pscustomobject]@{ method = $name; status = "missing" }) | Out-Null
        continue
    }

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $method.Invoke($form, @()) | Out-Null
    $sw.Stop()
    $result.Add([pscustomobject]@{ method = $name; status = "ok"; ms = $sw.ElapsedMilliseconds }) | Out-Null
}

$form.Dispose()
$result | ConvertTo-Json -Depth 4
