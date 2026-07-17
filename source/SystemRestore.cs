using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private const string Ck3MpsRestorePointPrefix = "CK3MPS before changes ";
        private const string Ck3MpsRestorePointSchemaVersion = "1";
        private const string Ck3MpsRestorePointMarkerPrefix = "CK3MPS-RP:";

        [DllImport("srclient.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint SRRemoveRestorePoint(int dwRPNum);

        private sealed class RestorePointListItem
        {
            public string SequenceNumber;
            public string CreationTime;
            public string Description;
            public bool IsCk3Mps;
            public string OperationId;
            public string OwnershipError;

            public override string ToString()
            {
                string label = IsCk3Mps ? "[CK3MPS] " : "[Read-only] ";
                return label + CreationTime + " | " + Description;
            }
        }

        private sealed class RestorePointOwnershipRecord
        {
            public string SchemaVersion;
            public string OperationId;
            public string Marker;
            public string SequenceNumber;
            public string CreationTime;
            public string Description;
            public string CreatedUtc;
            public string Digest;
        }

        private bool IsOwnedCk3MpsRestorePointDescription(string description)
        {
            string operationId;
            return TryExtractRestorePointOperationId(description, out operationId);
        }

        private bool IsOwnedCk3MpsRestorePointItem(RestorePointListItem item)
        {
            string error;
            return TryValidateOwnedRestorePointForDeletion(item, out error);
        }

        private bool ShouldAllowRestorePointItemCheck(RestorePointListItem item, CheckState newValue)
        {
            if (newValue != CheckState.Checked)
                return true;
            return item == null || item.IsCk3Mps;
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
                if (item != null && item.IsCk3Mps)
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

            string operationId = Guid.NewGuid().ToString("N");
            string description = Ck3MpsRestorePointPrefix
                + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                + " [" + BuildRestorePointMarker(operationId) + "]";
            string script =
                "$ErrorActionPreference = 'Stop'\r\n" +
                "Checkpoint-Computer -Description '" + EscapePowerShellSingleQuoted(description) + "' -RestorePointType 'MODIFY_SETTINGS'\r\n" +
                "Write-Output 'Restore point created: " + EscapePowerShellSingleQuoted(description) + "'\r\n";

            RunPowerShellScriptLogged(script, 180000);
            RecordCreatedRestorePointOwnership(operationId, description);
            Log("OK   Windows restore point created: " + description);
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
                if (item.IsCk3Mps)
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
                    ? "Only manifest-verified CK3MPS restore points can be deleted. Other restore points are informational and stay read-only."
                    : "Select one or more manifest-verified CK3MPS restore points first.";
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

            int removed = DeleteRestorePointsBySequenceNumbers(sequenceNumbers.ToArray(), "Deleted selected restore points: " + sequenceNumbers.Count + ".");
            if (removed == 0)
                MessageBox.Show("No restore points were deleted. CK3MPS rechecked ownership immediately before deletion and skipped unverified or changed items.", "Restore points", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _ = RefreshRestorePointsListAsync();
        }

        private List<RestorePointListItem> ListRestorePointItems()
        {
            List<RestorePointListItem> items = new List<RestorePointListItem>();
            foreach (string line in ListRestorePoints())
            {
                string[] parts = line.Split(new[] { '|' }, 3);
                if (parts.Length < 3)
                    continue;
                string description = parts[2].Trim();
                RestorePointListItem item = new RestorePointListItem
                {
                    SequenceNumber = parts[0].Trim(),
                    CreationTime = parts[1].Trim(),
                    Description = description,
                    IsCk3Mps = false
                };
                string error;
                item.IsCk3Mps = TryValidateOwnedRestorePointForDeletion(item, out error);
                item.OwnershipError = error;
                items.Add(item);
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

        private int DeleteRestorePointsBySequenceNumbers(string[] sequenceNumbers, string successMessage)
        {
            List<int> validatedSequenceNumbers = ValidateOwnedRestorePointDeletionSnapshot(sequenceNumbers, ListRestorePointItems());
            if (validatedSequenceNumbers.Count == 0)
            {
                Log("PROTECT Restore point delete skipped: no selected item had current app-owned metadata.");
                return 0;
            }

            int removed = 0;
            foreach (int id in validatedSequenceNumbers)
            {
                uint result = SRRemoveRestorePoint(id);
                if (result != 0)
                    throw new InvalidOperationException("Removing restore point " + id + " failed with code " + result + ".");
                removed++;
            }

            SetStatusText(successMessage);
            Log("OK   " + successMessage + " Removed=" + removed + ".");
            return removed;
        }

        private List<int> ValidateOwnedRestorePointDeletionSnapshot(IEnumerable<string> sequenceNumbers, IEnumerable<RestorePointListItem> currentItems)
        {
            List<string> requested = new List<string>();
            foreach (string item in sequenceNumbers ?? new string[0])
            {
                if (!String.IsNullOrWhiteSpace(item))
                    requested.Add(item.Trim());
            }
            if (requested.Count == 0)
                return new List<int>();

            Dictionary<string, RestorePointListItem> currentBySequence = new Dictionary<string, RestorePointListItem>(StringComparer.OrdinalIgnoreCase);
            foreach (RestorePointListItem item in currentItems ?? new RestorePointListItem[0])
            {
                if (item == null || String.IsNullOrWhiteSpace(item.SequenceNumber))
                    continue;
                currentBySequence[item.SequenceNumber.Trim()] = item;
            }

            List<int> validated = new List<int>();
            foreach (string idText in requested)
            {
                int id;
                if (!Int32.TryParse(idText, out id))
                {
                    Log("PROTECT Restore point delete skipped invalid sequence number: " + idText);
                    continue;
                }

                RestorePointListItem currentItem;
                if (!currentBySequence.TryGetValue(idText, out currentItem))
                {
                    Log("PROTECT Restore point delete skipped because it disappeared before deletion: " + idText);
                    continue;
                }

                string error;
                if (!TryValidateOwnedRestorePointForDeletion(currentItem, out error))
                {
                    Log("PROTECT Restore point delete skipped unowned item " + idText + ": " + error);
                    continue;
                }

                validated.Add(id);
            }

            return validated;
        }

        private void RecordCreatedRestorePointOwnership(string operationId, string description)
        {
            try
            {
                RestorePointListItem created = null;
                foreach (RestorePointListItem item in ListRestorePointItemsWithoutOwnership())
                {
                    if (String.Equals(item.Description, description, StringComparison.Ordinal)
                        && String.Equals(ExtractRestorePointOperationIdOrEmpty(item.Description), operationId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (created != null)
                            throw new InvalidOperationException("Duplicate restore point descriptions were returned for a newly created CK3MPS restore point.");
                        created = item;
                    }
                }

                if (created == null)
                {
                    Log("PROTECT Restore point ownership was not recorded because the created point could not be re-read.");
                    return;
                }

                AppendRestorePointOwnershipRecord(created, operationId);
            }
            catch (Exception ex)
            {
                Log("PROTECT Restore point ownership manifest was not updated: " + ex.Message);
            }
        }

        private List<RestorePointListItem> ListRestorePointItemsWithoutOwnership()
        {
            List<RestorePointListItem> items = new List<RestorePointListItem>();
            foreach (string line in ListRestorePoints())
            {
                string[] parts = line.Split(new[] { '|' }, 3);
                if (parts.Length < 3)
                    continue;
                items.Add(new RestorePointListItem
                {
                    SequenceNumber = parts[0].Trim(),
                    CreationTime = parts[1].Trim(),
                    Description = parts[2].Trim(),
                    IsCk3Mps = false
                });
            }
            return items;
        }

        private void AppendRestorePointOwnershipRecord(RestorePointListItem item, string operationId)
        {
            if (item == null)
                throw new InvalidOperationException("Restore point item is missing.");

            EnsureStabilizerRoot();
            string manifest = RestorePointOwnershipManifestFile();
            string parent = Path.GetDirectoryName(manifest);
            if (!String.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            List<string> lines = new List<string>();
            if (File.Exists(manifest))
                lines.AddRange(File.ReadAllLines(manifest, Encoding.UTF8));
            if (lines.Count == 0)
                lines.Add(RestorePointOwnershipHeader());

            RestorePointOwnershipRecord record = new RestorePointOwnershipRecord();
            record.SchemaVersion = Ck3MpsRestorePointSchemaVersion;
            record.OperationId = operationId;
            record.Marker = BuildRestorePointMarker(operationId);
            record.SequenceNumber = item.SequenceNumber;
            record.CreationTime = item.CreationTime;
            record.Description = item.Description;
            record.CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            record.Digest = ComputeRestorePointOwnershipDigest(record);
            lines.Add(SerializeRestorePointOwnershipRecord(record));
            SafeAtomicFile.WriteAllLines(manifest, lines.ToArray(), Encoding.UTF8);
        }

        private bool TryValidateOwnedRestorePointForDeletion(RestorePointListItem item, out string error)
        {
            error = "";
            if (item == null)
            {
                error = "restore point item is missing";
                return false;
            }
            if (String.IsNullOrWhiteSpace(item.SequenceNumber))
            {
                error = "restore point sequence number is missing";
                return false;
            }

            int ignored;
            if (!Int32.TryParse(item.SequenceNumber, out ignored))
            {
                error = "restore point sequence number is invalid";
                return false;
            }

            string operationId;
            if (!TryExtractRestorePointOperationId(item.Description, out operationId))
            {
                error = "description does not contain a CK3MPS ownership marker";
                return false;
            }
            item.OperationId = operationId;

            List<RestorePointOwnershipRecord> records;
            if (!TryReadRestorePointOwnershipManifest(out records, out error))
                return false;

            RestorePointOwnershipRecord match = null;
            int matchCount = 0;
            foreach (RestorePointOwnershipRecord record in records)
            {
                if (String.Equals(record.SequenceNumber, item.SequenceNumber, StringComparison.OrdinalIgnoreCase)
                    && String.Equals(record.OperationId, operationId, StringComparison.OrdinalIgnoreCase)
                    && String.Equals(record.Description, item.Description, StringComparison.Ordinal)
                    && String.Equals(record.CreationTime, item.CreationTime, StringComparison.Ordinal))
                {
                    match = record;
                    matchCount++;
                }
            }

            if (matchCount != 1 || match == null)
            {
                error = matchCount == 0 ? "matching app-owned manifest record was not found" : "duplicate app-owned manifest records were found";
                return false;
            }

            return TryValidateRestorePointOwnershipRecord(match, item, operationId, out error);
        }

        private bool TryReadRestorePointOwnershipManifest(out List<RestorePointOwnershipRecord> records, out string error)
        {
            records = new List<RestorePointOwnershipRecord>();
            error = "";
            string manifest = RestorePointOwnershipManifestFile();
            if (String.IsNullOrWhiteSpace(manifest) || !File.Exists(manifest))
            {
                error = "ownership manifest is missing";
                return false;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(manifest, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                error = "ownership manifest is unreadable: " + ex.Message;
                return false;
            }

            if (lines.Length == 0 || !String.Equals(lines[0], RestorePointOwnershipHeader(), StringComparison.Ordinal))
            {
                error = "ownership manifest header is invalid";
                return false;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                if (String.IsNullOrWhiteSpace(lines[i]))
                    continue;
                RestorePointOwnershipRecord record;
                if (!TryParseRestorePointOwnershipRecord(lines[i], out record))
                {
                    error = "ownership manifest row " + (i + 1).ToString(CultureInfo.InvariantCulture) + " is malformed";
                    return false;
                }
                string rowError;
                if (!TryValidateRestorePointOwnershipRecordShape(record, out rowError))
                {
                    error = "ownership manifest row " + (i + 1).ToString(CultureInfo.InvariantCulture) + " is invalid: " + rowError;
                    return false;
                }
                records.Add(record);
            }

            return true;
        }

        private bool TryValidateRestorePointOwnershipRecord(RestorePointOwnershipRecord record, RestorePointListItem item, string operationId, out string error)
        {
            error = "";
            string rowError;
            if (!TryValidateRestorePointOwnershipRecordShape(record, out rowError))
            {
                error = rowError;
                return false;
            }
            if (!String.Equals(record.SchemaVersion, Ck3MpsRestorePointSchemaVersion, StringComparison.Ordinal))
            {
                error = "ownership schema version is unsupported";
                return false;
            }
            if (!String.Equals(record.OperationId, operationId, StringComparison.OrdinalIgnoreCase))
            {
                error = "operation id does not match description marker";
                return false;
            }
            if (!String.Equals(record.Marker, BuildRestorePointMarker(operationId), StringComparison.OrdinalIgnoreCase))
            {
                error = "ownership marker does not match operation id";
                return false;
            }
            if (!String.Equals(record.SequenceNumber, item.SequenceNumber, StringComparison.OrdinalIgnoreCase)
                || !String.Equals(record.CreationTime, item.CreationTime, StringComparison.Ordinal)
                || !String.Equals(record.Description, item.Description, StringComparison.Ordinal))
            {
                error = "restore point identity changed after ownership was recorded";
                return false;
            }
            string expectedDigest = ComputeRestorePointOwnershipDigest(record);
            if (!String.Equals(expectedDigest, record.Digest, StringComparison.OrdinalIgnoreCase))
            {
                error = "ownership manifest digest does not match";
                return false;
            }
            return true;
        }

        private bool TryValidateRestorePointOwnershipRecordShape(RestorePointOwnershipRecord record, out string error)
        {
            error = "";
            if (record == null)
            {
                error = "record is missing";
                return false;
            }
            if (!String.Equals(record.SchemaVersion, Ck3MpsRestorePointSchemaVersion, StringComparison.Ordinal))
            {
                error = "schema version is unsupported";
                return false;
            }
            if (!IsValidRestorePointOperationId(record.OperationId))
            {
                error = "operation id is invalid";
                return false;
            }
            if (!String.Equals(record.Marker, BuildRestorePointMarker(record.OperationId), StringComparison.OrdinalIgnoreCase))
            {
                error = "marker is invalid";
                return false;
            }
            int ignored;
            if (!Int32.TryParse(record.SequenceNumber, out ignored))
            {
                error = "sequence number is invalid";
                return false;
            }
            if (String.IsNullOrWhiteSpace(record.CreationTime) || String.IsNullOrWhiteSpace(record.Description))
            {
                error = "restore point identity fields are missing";
                return false;
            }
            DateTime ignoredDate;
            if (!DateTime.TryParse(record.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out ignoredDate))
            {
                error = "record creation timestamp is invalid";
                return false;
            }
            if (String.IsNullOrWhiteSpace(record.Digest) || record.Digest.Length != 64)
            {
                error = "digest is invalid";
                return false;
            }
            return true;
        }

        private string RestorePointOwnershipManifestFile()
        {
            try
            {
                EnsureStabilizerRoot();
            }
            catch
            {
            }
            return String.IsNullOrEmpty(stabilizerRoot) ? "" : Path.Combine(stabilizerRoot, "restore_point_ownership.tsv");
        }

        private string RestorePointOwnershipSecretFile()
        {
            try
            {
                EnsureStabilizerRoot();
            }
            catch
            {
            }
            return String.IsNullOrEmpty(stabilizerRoot) ? "" : Path.Combine(stabilizerRoot, "restore_point_ownership.secret");
        }

        private string GetRestorePointOwnershipSecret()
        {
            string path = RestorePointOwnershipSecretFile();
            if (String.IsNullOrWhiteSpace(path))
                return "CK3MPS-restore-point-fallback-secret";

            string parent = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path, Encoding.UTF8).Trim();
                if (existing.Length >= 64)
                    return existing;
            }

            byte[] bytes = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            string secret = BytesToHex(bytes);
            SafeAtomicFile.WriteAllText(path, secret + Environment.NewLine, Encoding.UTF8);
            return secret;
        }

        private string ComputeRestorePointOwnershipDigest(RestorePointOwnershipRecord record)
        {
            string payload = String.Join("|", new[]
            {
                NullText(record.SchemaVersion),
                NullText(record.OperationId),
                NullText(record.Marker),
                NullText(record.SequenceNumber),
                NullText(record.CreationTime),
                NullText(record.Description),
                NullText(record.CreatedUtc)
            });
            string signedPayload = GetRestorePointOwnershipSecret() + "\n" + payload;
            using (SHA256 sha = SHA256.Create())
                return BytesToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(signedPayload)));
        }

        private string RestorePointOwnershipHeader()
        {
            return "schema\toperation_id\tmarker\tsequence_number\tcreation_time\tdescription\tcreated_utc\tdigest";
        }

        private string SerializeRestorePointOwnershipRecord(RestorePointOwnershipRecord record)
        {
            return String.Join("\t", new[]
            {
                EscapeRestorePointOwnershipField(record.SchemaVersion),
                EscapeRestorePointOwnershipField(record.OperationId),
                EscapeRestorePointOwnershipField(record.Marker),
                EscapeRestorePointOwnershipField(record.SequenceNumber),
                EscapeRestorePointOwnershipField(record.CreationTime),
                EscapeRestorePointOwnershipField(record.Description),
                EscapeRestorePointOwnershipField(record.CreatedUtc),
                EscapeRestorePointOwnershipField(record.Digest)
            });
        }

        private bool TryParseRestorePointOwnershipRecord(string line, out RestorePointOwnershipRecord record)
        {
            record = null;
            string[] parts = (line ?? "").Split('\t');
            if (parts.Length != 8)
                return false;
            record = new RestorePointOwnershipRecord();
            record.SchemaVersion = parts[0];
            record.OperationId = parts[1];
            record.Marker = parts[2];
            record.SequenceNumber = parts[3];
            record.CreationTime = parts[4];
            record.Description = parts[5];
            record.CreatedUtc = parts[6];
            record.Digest = parts[7];
            return true;
        }

        private string EscapeRestorePointOwnershipField(string value)
        {
            return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private string BuildRestorePointMarker(string operationId)
        {
            return Ck3MpsRestorePointMarkerPrefix + operationId;
        }

        private bool TryExtractRestorePointOperationId(string description, out string operationId)
        {
            operationId = "";
            string text = description ?? "";
            if (!text.StartsWith(Ck3MpsRestorePointPrefix, StringComparison.Ordinal))
                return false;
            string needle = "[" + Ck3MpsRestorePointMarkerPrefix;
            int start = text.IndexOf(needle, StringComparison.Ordinal);
            if (start < 0)
                return false;
            start += needle.Length;
            int end = text.IndexOf(']', start);
            if (end <= start)
                return false;
            string id = text.Substring(start, end - start);
            if (!IsValidRestorePointOperationId(id))
                return false;
            operationId = id;
            return true;
        }

        private string ExtractRestorePointOperationIdOrEmpty(string description)
        {
            string operationId;
            return TryExtractRestorePointOperationId(description, out operationId) ? operationId : "";
        }

        private bool IsValidRestorePointOperationId(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length != 32)
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private string BytesToHex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder((bytes == null ? 0 : bytes.Length) * 2);
            foreach (byte b in bytes ?? new byte[0])
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
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