using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private const string Ck3MpsRestorePointPrefix = "CK3MPS before changes ";

        [DllImport("srclient.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint SRRemoveRestorePoint(int dwRPNum);

        private sealed class RestorePointListItem
        {
            public string SequenceNumber;
            public string CreationTime;
            public string Description;
            public bool IsCk3Mps;

            public override string ToString()
            {
                return (IsCk3Mps ? "[CK3MPS] " : "[Other] ") + CreationTime + " | " + Description;
            }
        }

        private string RestorePointOwnershipFile()
        {
            RefreshStabilizerRoot();
            return Path.Combine(stabilizerRoot, "restore_points.tsv");
        }

        private HashSet<string> ReadOwnedRestorePointSequenceNumbers()
        {
            HashSet<string> owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string path = RestorePointOwnershipFile();
            if (!File.Exists(path))
                return owned;

            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string[] parts = line.Split('\t');
                if (parts.Length >= 1 && !String.IsNullOrWhiteSpace(parts[0]) && !String.Equals(parts[0], "sequence", StringComparison.OrdinalIgnoreCase))
                    owned.Add(parts[0].Trim());
            }

            return owned;
        }

        private void RecordOwnedRestorePoint(RestorePointListItem item)
        {
            if (item == null || String.IsNullOrWhiteSpace(item.SequenceNumber) || !IsOwnedCk3MpsRestorePointDescription(item.Description))
                return;

            EnsureStabilizerRoot();
            string path = RestorePointOwnershipFile();
            HashSet<string> existing = ReadOwnedRestorePointSequenceNumbers();
            if (existing.Contains(item.SequenceNumber.Trim()))
                return;

            List<string> lines = new List<string>();
            if (File.Exists(path))
                lines.AddRange(File.ReadAllLines(path, Encoding.UTF8));
            if (lines.Count == 0)
                lines.Add("sequence\tcreated\tdescription");
            lines.Add(RestoreManifestUtilities.EscapeTsv(item.SequenceNumber.Trim())
                + "\t" + RestoreManifestUtilities.EscapeTsv(item.CreationTime)
                + "\t" + RestoreManifestUtilities.EscapeTsv(item.Description));
            SafeAtomicFile.WriteAllLines(path, lines.ToArray(), Encoding.UTF8);
        }

        private void PruneOwnedRestorePointRegistry(IEnumerable<RestorePointListItem> currentItems)
        {
            string path = RestorePointOwnershipFile();
            if (!File.Exists(path))
                return;

            HashSet<string> currentOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RestorePointListItem item in currentItems ?? new RestorePointListItem[0])
                if (item != null
                    && !String.IsNullOrWhiteSpace(item.SequenceNumber)
                    && IsOwnedCk3MpsRestorePointDescription(item.Description))
                    currentOwned.Add(item.SequenceNumber.Trim());

            List<string> kept = new List<string>();
            kept.Add("sequence\tcreated\tdescription");
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string[] parts = line.Split('\t');
                if (parts.Length < 1 || String.Equals(parts[0], "sequence", StringComparison.OrdinalIgnoreCase))
                    continue;
                string sequence = parts[0].Trim();
                if (!String.IsNullOrWhiteSpace(sequence) && currentOwned.Contains(sequence))
                    kept.Add(line);
            }

            SafeAtomicFile.WriteAllLines(path, kept.ToArray(), Encoding.UTF8);
        }

        private bool IsOwnedCk3MpsRestorePointDescription(string description)
        {
            return !String.IsNullOrWhiteSpace(description)
                && description.StartsWith(Ck3MpsRestorePointPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsOwnedCk3MpsRestorePointItem(RestorePointListItem item)
        {
            return item != null
                && !String.IsNullOrWhiteSpace(item.SequenceNumber)
                && item.IsCk3Mps
                && IsOwnedCk3MpsRestorePointDescription(item.Description);
        }

        private bool ShouldAllowRestorePointItemCheck(RestorePointListItem item, CheckState newValue)
        {
            if (newValue != CheckState.Checked)
                return true;
            return IsOwnedCk3MpsRestorePointItem(item);
        }

        private void UpdateRestorePointDeleteButtonState()
        {
            if (restorePointsLoading)
            {
                deleteSelectedRestorePointsButton.Enabled = false;
                return;
            }

            foreach (object checkedItem in restorePointsListBox.CheckedItems)
            {
                RestorePointListItem item = checkedItem as RestorePointListItem;
                if (IsOwnedCk3MpsRestorePointItem(item))
                {
                    deleteSelectedRestorePointsButton.Enabled = true;
                    return;
                }
            }

            deleteSelectedRestorePointsButton.Enabled = false;
        }

        private void CreateWindowsRestorePoint()
        {
            MutationAudit.RecordMutation("system-restore-command", "create restore point");
            if (!IsAdministrator())
                throw new InvalidOperationException("Administrator rights are required to create a Windows restore point.");

            if (!WindowsRestorePointInfrastructureOk())
            {
                DialogResult result = ShowMessageBoxSafe(
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
            RegisterCreatedRestorePoint(description);
            Log("OK   Windows restore point created: " + description);
        }

        private void RegisterCreatedRestorePoint(string description)
        {
            try
            {
                RestorePointListItem created = FindRestorePointByDescription(description);
                if (created == null)
                    throw new InvalidOperationException("Created restore point was not returned by Windows.");
                RecordOwnedRestorePoint(created);
                Log("INFO Registered CK3MPS restore point ownership: " + created.SequenceNumber);
            }
            catch (Exception ex)
            {
                Log("WARN CK3MPS could not register restore point ownership. It will stay protected from Delete Data: " + ex.Message);
            }
        }

        private RestorePointListItem FindRestorePointByDescription(string description)
        {
            foreach (RestorePointListItem item in ListRestorePointItemsRaw())
                if (String.Equals(item.Description, description, StringComparison.OrdinalIgnoreCase))
                    return item;
            return null;
        }

        private void CheckWindowsRestorePointReadOnly()
        {
            Log("INFO Scan does not query Windows restore points.");
            Log("INFO Stabilize can create a restore point before CK3MPS changes when this step is selected.");
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

        private async Task RefreshRestorePointsListAsync()
        {
            if (restorePointsLoading)
                return;

            restorePointsLoading = true;
            restorePointsListBox.Enabled = false;
            deleteSelectedRestorePointsButton.Enabled = false;
            restorePointsListBox.Items.Clear();
            restorePointsListBox.Items.Add(new RestorePointListItem
            {
                SequenceNumber = "",
                CreationTime = "",
                Description = "Loading restore points...",
                IsCk3Mps = false
            });

            try
            {
                List<RestorePointListItem> items = await Task.Run(delegate { return ListRestorePointItems(); });
                restorePointsListBox.Items.Clear();
                foreach (RestorePointListItem item in items)
                    restorePointsListBox.Items.Add(item);
                if (items.Count == 0)
                    restorePointsListBox.Items.Add(new RestorePointListItem { Description = "No restore points found." });
                UpdateRestorePointDeleteButtonState();
            }
            catch (Exception ex)
            {
                restorePointsListBox.Items.Clear();
                restorePointsListBox.Items.Add(new RestorePointListItem { Description = "Restore points could not be loaded: " + ex.Message });
            }
            finally
            {
                restorePointsLoading = false;
                restorePointsListBox.Enabled = true;
                UpdateRestorePointDeleteButtonState();
            }
        }

        private void DeleteSelectedRestorePoints()
        {
            List<string> sequenceNumbers = new List<string>();
            int ck3MpsCount = 0;
            int otherCount = 0;
            foreach (object checkedItem in restorePointsListBox.CheckedItems)
            {
                RestorePointListItem item = checkedItem as RestorePointListItem;
                if (item == null || String.IsNullOrWhiteSpace(item.SequenceNumber))
                    continue;
                if (IsOwnedCk3MpsRestorePointItem(item))
                {
                    sequenceNumbers.Add(item.SequenceNumber);
                    ck3MpsCount++;
                }
                else
                {
                    otherCount++;
                }
            }

            if (sequenceNumbers.Count == 0)
            {
                string message = otherCount > 0
                    ? "Only restore points recorded as created by this CK3MPS installation can be deleted. Other restore points are informational and stay read-only."
                    : "Select one or more CK3MPS restore points first.";
                MessageBox.Show(message, "Restore points", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult result = MessageBox.Show(
                "Delete selected restore points?\r\n\r\nSelected CK3MPS restore points: " + sequenceNumbers.Count + "\r\nIgnored other restore points: " + otherCount,
                "Restore points",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
                return;

            DeleteRestorePointsBySequenceNumbers(sequenceNumbers.ToArray(), "Deleted selected restore points: " + sequenceNumbers.Count + ".");
            _ = RefreshRestorePointsListAsync();
        }

        private List<RestorePointListItem> ListRestorePointItems()
        {
            List<RestorePointListItem> items = ListRestorePointItemsRaw();
            HashSet<string> ownedSequences = ReadOwnedRestorePointSequenceNumbers();
            foreach (RestorePointListItem item in items)
                item.IsCk3Mps = IsOwnedCk3MpsRestorePointDescription(item.Description)
                    && !String.IsNullOrWhiteSpace(item.SequenceNumber)
                    && ownedSequences.Contains(item.SequenceNumber.Trim());
            PruneOwnedRestorePointRegistry(items);
            return items;
        }

        private List<RestorePointListItem> ListRestorePointItemsRaw()
        {
            List<RestorePointListItem> items = new List<RestorePointListItem>();
            foreach (string line in ListRestorePoints())
            {
                string[] parts = line.Split(new[] { '|' }, 3);
                if (parts.Length < 3)
                    continue;
                string description = parts[2].Trim();
                items.Add(new RestorePointListItem
                {
                    SequenceNumber = parts[0].Trim(),
                    CreationTime = parts[1].Trim(),
                    Description = description,
                    IsCk3Mps = false
                });
            }
            return items;
        }

        private List<string> ListRestorePoints()
        {
            string script =
                "$ErrorActionPreference = 'Stop'\r\n" +
                "Get-ComputerRestorePoint | Sort-Object CreationTime -Descending | ForEach-Object { $_.SequenceNumber.ToString() + '|' + $_.CreationTime + '|' + $_.Description }\r\n";
            string output = RunPowerShellScriptQuiet(script, 60000);
            List<string> items = new List<string>();
            foreach (string line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                items.Add(line.Trim());
            return items;
        }

        private void DeleteRestorePointsBySequenceNumbers(string[] sequenceNumbers, string successMessage)
        {
            List<RestorePointListItem> currentItems = ListRestorePointItems();
            List<int> validatedSequenceNumbers = ValidateOwnedRestorePointDeletionSnapshot(sequenceNumbers, currentItems);
            if (validatedSequenceNumbers.Count == 0)
                return;

            int removed = 0;
            foreach (int id in validatedSequenceNumbers)
            {
                uint result = SRRemoveRestorePoint(id);
                if (result != 0)
                    throw new InvalidOperationException("Removing restore point " + id + " failed with code " + result + ".");
                removed++;
            }

            PruneOwnedRestorePointRegistry(ListRestorePointItemsRaw());
            SetStatusText(successMessage);
            Log("OK   " + successMessage + " Removed=" + removed + ".");
        }

        private List<int> ValidateOwnedRestorePointDeletionSnapshot(IEnumerable<string> sequenceNumbers, IEnumerable<RestorePointListItem> currentItems)
        {
            List<string> valid = new List<string>();
            foreach (string item in sequenceNumbers ?? new string[0])
            {
                if (!String.IsNullOrWhiteSpace(item))
                    valid.Add(item.Trim());
            }
            if (valid.Count == 0)
                return new List<int>();

            Dictionary<string, RestorePointListItem> currentBySequence = new Dictionary<string, RestorePointListItem>(StringComparer.OrdinalIgnoreCase);
            foreach (RestorePointListItem item in currentItems ?? new RestorePointListItem[0])
            {
                if (item == null || String.IsNullOrWhiteSpace(item.SequenceNumber))
                    continue;
                currentBySequence[item.SequenceNumber.Trim()] = item;
            }

            List<int> validated = new List<int>();
            foreach (string idText in valid)
            {
                int id;
                if (!Int32.TryParse(idText, out id))
                    throw new InvalidOperationException("Restore point sequence number is invalid: " + idText);

                RestorePointListItem currentItem;
                if (!currentBySequence.TryGetValue(idText, out currentItem))
                    throw new InvalidOperationException("Restore point " + idText + " is no longer present. Refresh the list and try again.");
                if (!IsOwnedCk3MpsRestorePointItem(currentItem))
                    throw new InvalidOperationException("Restore point " + idText + " is not recorded as created by this CK3MPS installation and cannot be deleted.");

                validated.Add(id);
            }

            return validated;
        }

        private void RepairWindowsRestorePointInfrastructure()
        {
            MutationAudit.RecordMutation("system-restore-command", "repair restore infrastructure");
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
            Log("CMD  powershell.exe -NoProfile -NonInteractive -EncodedCommand <inline script>");
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
            Stopwatch sw = Stopwatch.StartNew();
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script ?? ""));
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encoded;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;

            using (Process process = Process.Start(psi))
            {
                StringBuilder output = new StringBuilder();
                StringBuilder error = new StringBuilder();
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                        output.AppendLine(e.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                        error.AppendLine(e.Data);
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!WaitForProcessResponsive(process, timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    return new PowerShellResult(124, "PowerShell command timed out.");
                }
                process.WaitForExit();
                sw.Stop();
                if (sw.ElapsedMilliseconds >= 1000)
                    Log("INFO PowerShell duration: " + FormatDurationMs(sw.ElapsedMilliseconds));
                return new PowerShellResult(process.ExitCode, (output.ToString() + "\r\n" + error.ToString()).Trim());
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