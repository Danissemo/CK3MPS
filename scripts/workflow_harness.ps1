$ErrorActionPreference = "Stop"

$env:CK3MPS_SKIP_ELEVATION = "1"

Add-Type -AssemblyName System.Windows.Forms

$assemblyPath = (Resolve-Path ".\bin\CK3MPS.exe").Path
$asm = [Reflection.Assembly]::LoadFrom($assemblyPath)
$formType = $asm.GetType("CK3MPS.MainForm", $true)
$form = [Activator]::CreateInstance($formType)
$flags = [Reflection.BindingFlags] "Instance,NonPublic"

$workflowModeBox = $formType.GetField("workflowModeBox", $flags).GetValue($form)
$workflowSummaryBox = $formType.GetField("workflowSummaryBox", $flags).GetValue($form)
$workflowVerdictLabel = $formType.GetField("workflowVerdictLabel", $flags).GetValue($form)
$rebuild = $formType.GetMethod("RebuildWorkflowScenarioUi", $flags)
$result = [ordered]@{}
foreach ($scenario in @("Start Session", "After OOS", "Rehost", "Hotjoin"))
{
    Write-Output ("SCENARIO_START " + $scenario)
    $workflowModeBox.SelectedItem = $scenario
    Write-Output ("SCENARIO_SELECTED " + $scenario)
    $rebuild.Invoke($form, @()) | Out-Null
    Write-Output ("SCENARIO_REBUILT " + $scenario)
    $result[$scenario] = [ordered]@{
        verdict = $workflowVerdictLabel.Text
        summary = ($workflowSummaryBox.Text -split "`r?`n" | Select-Object -First 10)
    }
}

$form.Dispose()

$result | ConvertTo-Json -Depth 6
