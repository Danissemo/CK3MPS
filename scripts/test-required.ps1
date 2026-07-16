$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$BuiltExe = Join-Path $Root 'bin\CK3MPS.exe'
$TestScript = Join-Path $ScriptDir 'test.ps1'
$ReadOnlyHarnessSource = Join-Path $Root 'tests\ReadOnlyScanHarness.cs'
$GeneratedDir = Join-Path $Root 'bin\tests\required-suite'
$GeneratedHarness = Join-Path $GeneratedDir 'ReadOnlyScanHarness.ci.cs'
$GeneratedTestScript = Join-Path $GeneratedDir 'test-required.generated.ps1'

foreach ($requiredPath in @($BuiltExe, $TestScript, $ReadOnlyHarnessSource)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required integration-test input is missing: $requiredPath"
    }
}

New-Item -ItemType Directory -Force -Path $GeneratedDir | Out-Null

# Build a compile-only copy of the WinForms harness. The assertions and scan
# behavior stay intact; only UI handle creation and message-queue shutdown are
# made deterministic for headless Windows runners. The tracked C# test file is
# never modified.
$harnessText = (Get-Content -LiteralPath $ReadOnlyHarnessSource -Raw) -replace "`r`n", "`n"

$createNeedle = '                ((Form)form).CreateControl();'
$createReplacement = '                CreateControlTree((Control)form);'
if (-not $harnessText.Contains($createNeedle)) {
    throw 'Could not locate the ReadOnlyScan form-handle initialization.'
}
$harnessText = $harnessText.Replace($createNeedle, $createReplacement)

$deadlineNeedle = '                DateTime deadline = DateTime.MaxValue;'
$deadlineReplacement = $deadlineNeedle + "`n                DateTime completionDrainDeadline = DateTime.MaxValue;"
if (-not $harnessText.Contains($deadlineNeedle)) {
    throw 'Could not locate the ReadOnlyScan deadline initialization.'
}
$harnessText = $harnessText.Replace($deadlineNeedle, $deadlineReplacement)

$oldMonitorBlock = @'
                        if (scanTask.IsCompleted || DateTime.UtcNow >= deadline)
                        {
                            monitor.Stop();
                            context.ExitThread();
                        }
'@
$newMonitorBlock = @'
                        if (scanTask.IsCompleted)
                        {
                            if (completionDrainDeadline == DateTime.MaxValue)
                            {
                                completionDrainDeadline = DateTime.UtcNow.AddMilliseconds(1500);
                                return;
                            }
                            if (DateTime.UtcNow < completionDrainDeadline)
                                return;
                        }

                        if (DateTime.UtcNow >= deadline || scanTask.IsCompleted)
                        {
                            monitor.Stop();
                            context.ExitThread();
                        }
'@
$oldMonitorBlock = $oldMonitorBlock -replace "`r`n", "`n"
$newMonitorBlock = $newMonitorBlock -replace "`r`n", "`n"
if (-not $harnessText.Contains($oldMonitorBlock)) {
    throw 'Could not locate the ReadOnlyScan monitor shutdown block.'
}
$harnessText = $harnessText.Replace($oldMonitorBlock, $newMonitorBlock)

$helperNeedle = '    private static void SetField(Type type, object instance, string fieldName, object value, BindingFlags flags)'
$helperBlock = @'
    private static void CreateControlTree(Control control)
    {
        control.CreateControl();
        foreach (Control child in control.Controls)
            CreateControlTree(child);
    }

'@
$helperBlock = $helperBlock -replace "`r`n", "`n"
$helperIndex = $harnessText.IndexOf($helperNeedle, [System.StringComparison]::Ordinal)
if ($helperIndex -lt 0) {
    throw 'Could not locate the ReadOnlyScan helper insertion point.'
}
$harnessText = $harnessText.Insert($helperIndex, $helperBlock)
Set-Content -LiteralPath $GeneratedHarness -Value $harnessText -Encoding UTF8

# Execute the existing required suite unchanged except that it compiles the
# deterministic harness copy above. Keep ScriptDir bound to the real scripts
# directory because this generated launcher lives under bin.
$testText = (Get-Content -LiteralPath $TestScript -Raw) -replace "`r`n", "`n"
$scriptDirNeedle = '$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path'
$escapedScriptDir = $ScriptDir.Replace("'", "''")
$scriptDirReplacement = '$ScriptDir = ''' + $escapedScriptDir + ''''
if (-not $testText.Contains($scriptDirNeedle)) {
    throw 'Could not locate ScriptDir initialization in scripts\test.ps1.'
}
$testText = $testText.Replace($scriptDirNeedle, $scriptDirReplacement)

$harnessPathNeedle = '(Join-Path $Root "tests\ReadOnlyScanHarness.cs")'
$escapedHarnessPath = $GeneratedHarness.Replace("'", "''")
$harnessPathReplacement = "'$escapedHarnessPath'"
if (-not $testText.Contains($harnessPathNeedle)) {
    throw 'Could not locate ReadOnlyScan harness compilation in scripts\test.ps1.'
}
$testText = $testText.Replace($harnessPathNeedle, $harnessPathReplacement)
Set-Content -LiteralPath $GeneratedTestScript -Value $testText -Encoding UTF8

$candidateName = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' }
$PowerShellExecutable = Join-Path $PSHOME $candidateName
if (-not (Test-Path -LiteralPath $PowerShellExecutable -PathType Leaf)) {
    $command = Get-Command $candidateName -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "Could not locate $candidateName for required integration-test execution."
    }
    $PowerShellExecutable = $command.Source
}

& $PowerShellExecutable -NoLogo -NoProfile -NonInteractive -File $GeneratedTestScript
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    throw "Required integration test suite failed with exit code $exitCode."
}

Write-Host 'Required integration test suite completed.' -ForegroundColor Green
