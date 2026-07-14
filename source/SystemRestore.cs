using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private const string Ck3MpsRestorePointPrefix = "CK3MPS before changes ";

        private void CreateWindowsRestorePoint()
        {
            if (!IsAdministrator())
                throw new InvalidOperationException("Administrator rights are required to create a Windows restore point.");

            if (!WindowsRestorePointInfrastructureOk())
            {
                DialogResult result = MessageBox.Show(
                    "Windows System Restore is not ready. CK3MPS can try to enable System Protection for the system drive and repair the VSS services before creating a restore point.\r\n\r\nContinue?",
                    "CK3MPS restore point",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.Yes)
                    throw new InvalidOperationException("Windows restore point was skipped because System Restore is not ready.");

                RepairWindowsRestorePointInfrastructure();
            }

            string description = Ck3MpsRestorePointPrefix + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string script =
                "$ErrorActionPreference = 'Stop'\r\n" +
                "Checkpoint-Computer -Description '" + EscapePowerShellSingleQuoted(description) + "' -RestorePointType 'MODIFY_SETTINGS'\r\n" +
                "Write-Output 'Restore point created: " + EscapePowerShellSingleQuoted(description) + "'\r\n";

            RunPowerShellScriptLogged(script, 180000);
            Log("OK   Windows restore point created: " + description);
        }

        private void CheckWindowsRestorePointReadOnly()
        {
            Check("System Restore PowerShell cmdlets available", SystemRestoreCmdletsAvailable());
            Check("Volume Shadow Copy service available", WindowsServiceExistsAndNotDisabled("VSS"));
            Check("Microsoft Software Shadow Copy Provider available", WindowsServiceExistsAndNotDisabled("swprv"));
            Check("System Restore status readable", SystemRestoreStatusReadable());
            string latest = LatestRestorePointSummary();
            if (!String.IsNullOrEmpty(latest))
                Log("INFO Latest restore point: " + latest);
            Log("INFO Stabilize can create a restore point before CK3MPS changes. If Windows System Restore is disabled, CK3MPS asks before trying to enable it.");
        }

        private bool WindowsRestorePointInfrastructureOk()
        {
            return SystemRestoreCmdletsAvailable()
                && WindowsServiceExistsAndNotDisabled("VSS")
                && WindowsServiceExistsAndNotDisabled("swprv")
                && SystemRestoreStatusReadable();
        }

        private bool SystemRestoreCmdletsAvailable()
        {
            string script = "if ((Get-Command Checkpoint-Computer -ErrorAction SilentlyContinue) -and (Get-Command Enable-ComputerRestore -ErrorAction SilentlyContinue) -and (Get-Command Get-ComputerRestorePoint -ErrorAction SilentlyContinue)) { exit 0 } else { exit 1 }";
            return RunPowerShellScriptExitCode(script, 30000) == 0;
        }

        private bool WindowsServiceExistsAndNotDisabled(string serviceName)
        {
            string escaped = EscapePowerShellSingleQuoted(serviceName);
            string script =
                "$svc = Get-CimInstance Win32_Service -Filter \"Name='" + escaped + "'\" -ErrorAction SilentlyContinue\r\n" +
                "if (-not $svc) { exit 1 }\r\n" +
                "if ($svc.StartMode -eq 'Disabled') { exit 2 }\r\n" +
                "exit 0\r\n";
            return RunPowerShellScriptExitCode(script, 30000) == 0;
        }

        private bool SystemRestoreStatusReadable()
        {
            string script = "$ErrorActionPreference = 'Stop'\r\nGet-ComputerRestorePoint | Select-Object -First 1 | Out-Null\r\n";
            return RunPowerShellScriptExitCode(script, 60000) == 0;
        }

        private string LatestRestorePointSummary()
        {
            string script =
                "$ErrorActionPreference = 'Stop'\r\n" +
                "$rp = Get-ComputerRestorePoint | Sort-Object CreationTime -Descending | Select-Object -First 1\r\n" +
                "if ($rp) { $rp.CreationTime + ' | ' + $rp.Description + ' | type=' + $rp.RestorePointType }\r\n";
            return RunPowerShellScriptQuiet(script, 60000).Trim();
        }

        private void DeleteCk3MpsRestorePoints()
        {
            List<string> restorePoints = ListCk3MpsRestorePoints();
            if (restorePoints.Count == 0)
            {
                MessageBox.Show("No CK3MPS-created restore points were found.", "CK3MPS restore points", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult result = MessageBox.Show(
                "Delete " + restorePoints.Count + " restore point(s) created by CK3MPS?\r\n\r\nThis removes only restore points whose description starts with \"" + Ck3MpsRestorePointPrefix.Trim() + "\".",
                "CK3MPS restore points",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
                return;

            string script =
                "$ErrorActionPreference = 'Stop'\r\n" +
                "$points = Get-ComputerRestorePoint | Where-Object { $_.Description -like '" + EscapePowerShellSingleQuoted(Ck3MpsRestorePointPrefix) + "*' } | Sort-Object SequenceNumber -Descending\r\n" +
                "$removed = 0\r\n" +
                "foreach ($point in $points) {\r\n" +
                "  $result = ([WMIClass]'root/default:SystemRestore').RemoveRestorePoint($point.SequenceNumber)\r\n" +
                "  if ($result.ReturnValue -ne 0) { throw 'Failed to remove restore point sequence ' + $point.SequenceNumber + ' (code ' + $result.ReturnValue + ')' }\r\n" +
                "  $removed++\r\n" +
                "}\r\n" +
                "Write-Output ('Removed restore points: ' + $removed)\r\n";

            RunPowerShellScriptLogged(script, 180000);
            statusLabel.Text = "Deleted CK3MPS restore points: " + restorePoints.Count + ".";
            Log("OK   Deleted CK3MPS restore points: " + restorePoints.Count + ".");
        }

        private List<string> ListCk3MpsRestorePoints()
        {
            string script =
                "$ErrorActionPreference = 'Stop'\r\n" +
                "Get-ComputerRestorePoint | Where-Object { $_.Description -like '" + EscapePowerShellSingleQuoted(Ck3MpsRestorePointPrefix) + "*' } | " +
                "Sort-Object CreationTime -Descending | ForEach-Object { $_.SequenceNumber.ToString() + '|' + $_.CreationTime + '|' + $_.Description }\r\n";
            string output = RunPowerShellScriptQuiet(script, 60000);
            List<string> items = new List<string>();
            foreach (string line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                items.Add(line.Trim());
            return items;
        }

        private void RepairWindowsRestorePointInfrastructure()
        {
            string script =
                "$ErrorActionPreference = 'Stop'\r\n" +
                "Set-Service -Name VSS -StartupType Manual\r\n" +
                "Set-Service -Name swprv -StartupType Manual\r\n" +
                "Start-Service -Name VSS -ErrorAction SilentlyContinue\r\n" +
                "Start-Service -Name swprv -ErrorAction SilentlyContinue\r\n" +
                "Enable-ComputerRestore -Drive ($env:SystemDrive + '\\')\r\n" +
                "Write-Output 'System Restore infrastructure is ready.'\r\n";
            RunPowerShellScriptLogged(script, 120000);
        }

        private string RunPowerShellScriptLogged(string script, int timeoutMs)
        {
            PowerShellResult result = RunPowerShellScript(script, timeoutMs);
            Log("CMD  powershell.exe -NoProfile -ExecutionPolicy Bypass -File <temporary script>");
            foreach (string line in result.CombinedOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                Log("  " + line.Trim());

            if (result.ExitCode != 0)
                throw new InvalidOperationException("PowerShell failed with exit code " + result.ExitCode + ": " + result.CombinedOutput);
            return result.CombinedOutput;
        }

        private string RunPowerShellScriptQuiet(string script, int timeoutMs)
        {
            return RunPowerShellScript(script, timeoutMs).CombinedOutput;
        }

        private int RunPowerShellScriptExitCode(string script, int timeoutMs)
        {
            return RunPowerShellScript(script, timeoutMs).ExitCode;
        }

        private PowerShellResult RunPowerShellScript(string script, int timeoutMs)
        {
            string tempScript = Path.Combine(Path.GetTempPath(), "CK3MPS-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(tempScript, script, Encoding.UTF8);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + tempScript + "\"";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { }
                        return new PowerShellResult(124, "PowerShell command timed out.");
                    }
                    return new PowerShellResult(process.ExitCode, (output + "\r\n" + error).Trim());
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempScript))
                        File.Delete(tempScript);
                }
                catch { }
            }
        }

        private string EscapePowerShellSingleQuoted(string value)
        {
            return (value ?? "").Replace("'", "''");
        }

        private sealed class PowerShellResult
        {
            public readonly int ExitCode;
            public readonly string CombinedOutput;

            public PowerShellResult(int exitCode, string combinedOutput)
            {
                ExitCode = exitCode;
                CombinedOutput = combinedOutput ?? "";
            }
        }
    }
}
