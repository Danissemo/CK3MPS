using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private sealed class PreviewLine
        {
            public string Tone;
            public string Text;

            public PreviewLine(string tone, string text)
            {
                Tone = tone;
                Text = text;
            }
        }

        private bool ConfirmStabilizationPreview()
        {
            List<PreviewLine> previewLines = BuildStabilizationPreviewLines();
            string preview = BuildStabilizationPreviewText(previewLines);
            LogSection("Stabilization preview");
            foreach (string line in preview.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                if (!String.IsNullOrWhiteSpace(line))
                    Log("INFO " + line);
            return ShowPreviewDialog(previewLines, true) == DialogResult.Yes;
        }

        private void ShowStabilizationPreview(bool writeToLogOnly)
        {
            List<PreviewLine> previewLines = BuildStabilizationPreviewLines();
            string preview = BuildStabilizationPreviewText(previewLines);
            if (writeToLogOnly)
            {
                LogSection("Dry-run stabilization plan");
                foreach (string line in preview.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                    if (!String.IsNullOrWhiteSpace(line))
                        Log("INFO " + line);
                return;
            }

            ShowPreviewDialog(previewLines, false);
        }

        private DialogResult ShowPreviewDialog(List<PreviewLine> previewLines, bool confirm)
        {
            using (Form dialog = new Form())
            using (RichTextBox previewBox = new RichTextBox())
            using (Button actionButton = new Button())
            using (Button cancelButton = new Button())
            {
                dialog.Text = "CK3MPS preview";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Size = new Size(920, 760);
                dialog.MinimumSize = new Size(760, 560);
                dialog.MaximizeBox = true;
                dialog.MinimizeBox = false;
                dialog.ShowIcon = false;
                dialog.Font = this.Font;
                dialog.BackColor = Color.FromArgb(245, 247, 250);

                previewBox.Location = new Point(12, 12);
                previewBox.Size = new Size(dialog.ClientSize.Width - 24, dialog.ClientSize.Height - 72);
                previewBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                previewBox.ReadOnly = true;
                previewBox.ScrollBars = RichTextBoxScrollBars.Both;
                previewBox.WordWrap = true;
                previewBox.Font = new Font("Segoe UI", 9.5F);
                previewBox.BackColor = Color.FromArgb(252, 252, 253);
                previewBox.BorderStyle = BorderStyle.FixedSingle;
                previewBox.DetectUrls = false;
                RenderPreview(previewBox, previewLines, confirm);
                dialog.Controls.Add(previewBox);

                actionButton.Text = confirm ? "Continue" : "OK";
                actionButton.Size = new Size(110, 32);
                actionButton.Location = new Point(dialog.ClientSize.Width - 122, dialog.ClientSize.Height - 44);
                actionButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
                actionButton.DialogResult = confirm ? DialogResult.Yes : DialogResult.OK;
                dialog.Controls.Add(actionButton);

                if (confirm)
                {
                    cancelButton.Text = "Cancel";
                    cancelButton.Size = new Size(110, 32);
                    cancelButton.Location = new Point(actionButton.Left - 118, actionButton.Top);
                    cancelButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
                    cancelButton.DialogResult = DialogResult.No;
                    dialog.Controls.Add(cancelButton);
                    dialog.CancelButton = cancelButton;
                }
                else
                {
                    dialog.CancelButton = actionButton;
                }

                dialog.AcceptButton = actionButton;
                return dialog.ShowDialog(this);
            }
        }

        private void RenderPreview(RichTextBox box, List<PreviewLine> previewLines, bool confirm)
        {
            box.Clear();
            foreach (PreviewLine line in previewLines)
                AppendPreviewLine(box, line);

            if (confirm)
            {
                AppendPreviewLine(box, new PreviewLine("blank", ""));
                AppendPreviewLine(box, new PreviewLine("header", "Confirmation"));
                AppendPreviewLine(box, new PreviewLine("warn", "Continue and apply these selected actions?"));
            }
        }

        private void AppendPreviewLine(RichTextBox box, PreviewLine line)
        {
            Color color = Color.FromArgb(51, 65, 85);
            FontStyle style = FontStyle.Regular;

            switch (line.Tone)
            {
                case "header":
                    color = Color.FromArgb(15, 23, 42);
                    style = FontStyle.Bold;
                    break;
                case "change":
                    color = Color.FromArgb(3, 105, 161);
                    break;
                case "move":
                    color = Color.FromArgb(180, 83, 9);
                    break;
                case "report":
                    color = Color.FromArgb(22, 101, 52);
                    break;
                case "safe":
                    color = Color.FromArgb(9, 96, 76);
                    break;
                case "warn":
                    color = Color.FromArgb(185, 28, 28);
                    break;
                case "muted":
                    color = Color.FromArgb(100, 116, 139);
                    break;
            }

            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = color;
            box.SelectionFont = new Font(box.Font, style);
            box.AppendText(line.Text + Environment.NewLine);
        }

        private string BuildStabilizationPreviewText(List<PreviewLine> lines)
        {
            StringBuilder sb = new StringBuilder();
            foreach (PreviewLine line in lines)
                sb.AppendLine(line.Text);
            return sb.ToString().TrimEnd();
        }

        private List<PreviewLine> BuildStabilizationPreviewLines()
        {
            EnsurePlanningSnapshot();
            List<PreviewLine> lines = new List<PreviewLine>();
            int selectedOptional = CountSelectedChecklistSteps();
            int plannedSteps = CountPlannedStabilizeSteps();
            List<int> changingSteps = new List<int>();
            List<int> reportSteps = new List<int>();
            List<int> skippedSteps = new List<int>();

            for (int i = 0; i < steps.Items.Count; i++)
            {
                if (!IsStepChecked(i))
                    continue;

                if (i == 1 || i == 2)
                    continue;

                if (ShouldRunSelectedStabilizeStep(i))
                {
                    if (StepChangesState(i))
                        changingSteps.Add(i);
                    else
                        reportSteps.Add(i);
                }
                else
                    skippedSteps.Add(i);
            }

            lines.Add(new PreviewLine("header", "Overview"));
            lines.Add(new PreviewLine("muted", "Preset: " + NullText(Convert.ToString(presetBox.SelectedItem)) + " | Graphics: " + CurrentGraphicsProfile() + " | Portable mode: " + YesNo(portableMode)));
            lines.Add(new PreviewLine("muted", "Checklist items selected: " + selectedOptional + " | Steps that will actually run now: " + plannedSteps));
            lines.Add(new PreviewLine("muted", "Game folder: " + NullText(ck3Install)));
            lines.Add(new PreviewLine("muted", "Settings/saves: " + NullText(ck3Docs)));

            lines.Add(new PreviewLine("blank", ""));
            lines.Add(new PreviewLine("header", "Core actions that will run"));
            if (ShouldRunPathValidationCoreStep())
                lines.Add(new PreviewLine("safe", "- Validate the selected CK3 folders and confirm CK3/Paradox Launcher are closed."));
            if (ShouldRunQuarantineCoreStep())
                lines.Add(new PreviewLine("safe", "- Create or reuse the current quarantine run and record rollback data before changing files, registry values or system snapshots."));
            if (!ShouldRunPathValidationCoreStep() && !ShouldRunQuarantineCoreStep())
                lines.Add(new PreviewLine("warn", "- No selected step needs to run right now."));

            lines.Add(new PreviewLine("blank", ""));
            lines.Add(new PreviewLine("header", "Changes that will be applied"));
            if (changingSteps.Count == 0)
            {
                lines.Add(new PreviewLine("warn", "- No selected file, registry or launcher setting needs a change right now."));
            }
            else
            {
                foreach (int index in changingSteps)
                {
                    lines.Add(new PreviewLine(PreviewTone(index), "- " + StepTitle(index)));
                    foreach (string detail in BuildStepPreviewDetails(index))
                        lines.Add(new PreviewLine("muted", "  " + detail));
                }
            }

            lines.Add(new PreviewLine("blank", ""));
            lines.Add(new PreviewLine("header", "Reports and checks that will run"));
            if (reportSteps.Count == 0)
            {
                lines.Add(new PreviewLine("muted", "- No selected report-only step needs separate output right now."));
            }
            else
            {
                foreach (int index in reportSteps)
                {
                    lines.Add(new PreviewLine(PreviewTone(index), "- " + StepTitle(index)));
                    foreach (string detail in BuildStepPreviewDetails(index))
                        lines.Add(new PreviewLine("muted", "  " + detail));
                }
            }

            if (skippedSteps.Count > 0)
            {
                lines.Add(new PreviewLine("blank", ""));
                lines.Add(new PreviewLine("header", "Already in target state"));
                foreach (int index in skippedSteps)
                    lines.Add(new PreviewLine("safe", "- " + StepTitle(index) + " | " + GetStabilizeStepSkipReason(index)));
            }

            lines.Add(new PreviewLine("blank", ""));
            lines.Add(new PreviewLine("header", "Safety and outputs"));
            lines.Add(new PreviewLine("safe", "- Changed files are recorded in the restore manifest before CK3MPS overwrites or moves them."));
            lines.Add(new PreviewLine("safe", "- Restore can bring items back to the recorded previous state or reset supported overrides to default behavior."));
            lines.Add(new PreviewLine("report", "- Reports are written under: " + stabilizerRoot));
            lines.Add(new PreviewLine("warn", "- CK3 and Paradox Launcher should stay closed while file-changing steps are applied."));
            return lines;
        }

        private string PreviewTone(int index)
        {
            switch (index)
            {
                case 0:
                case 1:
                case 2:
                    return "safe";
                case 3:
                case 5:
                case 6:
                case 7:
                case 11:
                case 14:
                case 15:
                    return "change";
                case 10:
                case 12:
                case 18:
                case 19:
                case 20:
                case 21:
                case 23:
                case 24:
                    return "move";
                case 4:
                case 8:
                case 9:
                case 13:
                case 16:
                case 17:
                case 22:
                case 25:
                case 26:
                case 27:
                case 28:
                    return "report";
            }
            return "muted";
        }

        private bool WillRunInStabilize(int index)
        {
            if (index == 1)
                return ShouldRunPathValidationCoreStep();
            if (index == 2)
                return ShouldRunQuarantineCoreStep();
            return IsStepChecked(index) && ShouldRunSelectedStabilizeStep(index);
        }

        private string PreviewImpactText(int index)
        {
            List<string> details = BuildStepPreviewDetails(index);
            if (details.Count > 0)
                return String.Join(" ", details.ToArray());

            switch (index)
            {
                case 0:
                    return "Creates a Windows restore point named for CK3MPS before any selected changes start.";
                case 1:
                    return "Validates the selected folders and confirms CK3 and Paradox Launcher are not still running.";
                case 2:
                    return "Starts the quarantine/restore run so later changes can be rolled back from the Restore tab.";
                case 3:
                    return "Clears Windows DNS cache only.";
                case 4:
                    return "Reads adapter, route, MTU, DNS and TCP/IP state and writes diagnostics only.";
                case 5:
                    return "Updates CK3 firewall rules only when current rules are missing or no longer match the active ck3.exe path.";
                case 6:
                    return "Writes the selected Windows registry tuning values and records previous values for restore.";
                case 7:
                    return "Writes selected power and adapter stability settings and records the previous command state.";
                case 8:
                case 9:
                case 13:
                case 22:
                case 25:
                case 26:
                case 27:
                case 28:
                    return "Report-only step. It collects information without moving personal saves or mods.";
                case 10:
                    return "Backs up Steam, launcher, dlc_load.json and pdx_settings.txt sources into quarantine first.";
                case 11:
                    return "Rewrites Steam CK3 launch/cloud behavior after backup.";
                case 12:
                    return "Moves launcher database/cache state to quarantine so Paradox Launcher rebuilds a cleaner default state.";
                case 14:
                    return "Rewrites dlc_load.json to a no-active-mods multiplayer-safe state after backup.";
                case 15:
                    return "Rewrites pdx_settings.txt core MP settings and applies the selected graphics profile: " + CurrentGraphicsProfile() + ".";
                case 16:
                    return "Writes settings guard and runtime verification report files.";
                case 17:
                    return "Writes a fresh reference profile summary for stable new-campaign starts.";
                case 18:
                    return "Moves CK3 player UI state to quarantine so CK3 recreates defaults.";
                case 19:
                    return "Moves OOS, crash, dump and exception folders into quarantine reports.";
                case 20:
                    return "Moves CK3 and launcher caches to quarantine so they are rebuilt cleanly.";
                case 21:
                    return "Moves local .mod descriptor files to quarantine.";
                case 23:
                    return "Quarantines suspicious save pointers/files only when CK3MPS finds a safer manual save path.";
                case 24:
                    return "Performs broader CK3 Documents cleanup while keeping actual saves protected.";
            }
            return "See step help for details.";
        }

        private int CountSelectedChecklistSteps()
        {
            int count = 0;
            for (int i = 0; i < steps.Items.Count; i++)
                if (IsStepChecked(i))
                    count++;
            return count;
        }

        private int CountPlannedStabilizeSteps()
        {
            EnsurePlanningSnapshot();
            int count = 0;
            if (ShouldRunPathValidationCoreStep())
                count++;
            if (ShouldRunQuarantineCoreStep())
                count++;

            for (int i = 0; i < steps.Items.Count; i++)
                if (IsStepChecked(i) && i != 1 && i != 2 && ShouldRunSelectedStabilizeStep(i))
                    count++;

            return count;
        }

        private bool ShouldRunPathValidationCoreStep()
        {
            EnsurePlanningSnapshot();
            for (int i = 0; i < steps.Items.Count; i++)
                if (IsStepChecked(i) && i != 1 && i != 2 && ShouldRunSelectedStabilizeStep(i))
                    return true;
            return false;
        }

        private bool ShouldRunQuarantineCoreStep()
        {
            EnsurePlanningSnapshot();
            for (int i = 0; i < steps.Items.Count; i++)
                if (IsStepChecked(i) && i != 1 && i != 2 && ShouldRunSelectedStabilizeStep(i) && StepNeedsQuarantine(i))
                    return true;
            return false;
        }

        private bool ShouldRunSelectedStabilizeStep(int index)
        {
            if (!IsStepChecked(index))
                return false;

            EnsurePlanningSnapshot();
            if (index >= 0 && index < planningShouldRun.Length)
                return planningShouldRun[index];

            return ComputeShouldRunSelectedStabilizeStep(index);
        }

        private bool ComputeShouldRunSelectedStabilizeStep(int index)
        {
            if (!IsStepChecked(index))
                return false;

            switch (index)
            {
                case 1:
                case 2:
                    return false;
                case 4:
                    return NetworkDiagnosticsNeedsUpdate();
                case 5:
                    return FirewallRulesNeedUpdate();
                case 6:
                    return !WindowsGameNetworkProfileOk();
                case 7:
                    return PowerAdapterProfileNeedsUpdate();
                case 8:
                    return OverlayAndVpnSnapshotNeedsUpdate();
                case 9:
                    return OnlineServicesSnapshotNeedsUpdate();
                case 10:
                    return HasSteamAndLauncherBackupTargets();
                case 11:
                    return SteamSettingsNeedUpdate();
                case 12:
                    return LauncherRebuildHasTargets();
                case 13:
                    return RuntimeHygieneSnapshotNeedsUpdate();
                case 14:
                    return DlcLoadNeedsRewrite();
                case 15:
                    return PdxSettingsNeedsRewrite();
                case 16:
                    return RuntimeVerificationReportNeedsUpdate();
                case 17:
                    return StableGameRuleProfileNeedsUpdate();
                case 18:
                    return Directory.Exists(Path.Combine(ck3Docs, "player"));
                case 19:
                    return !ReportsClean();
                case 20:
                    return !CacheFoldersClean();
                case 21:
                    return CountModDescriptorFiles() > 0;
                case 22:
                    return CountSuspectBinaries() > 0;
                case 23:
                    return SaveHygieneNeedsChanges();
                case 24:
                    return FolderCleanupNeedsChanges();
                case 25:
                    return LatestOosSummaryNeedsUpdate() || OosHistoryTimelineNeedsUpdate();
                case 26:
                    return OosEvidencePackNeedsUpdate();
                case 27:
                    return OosPreventionProtocolNeedsUpdate();
                case 28:
                    return MultiplayerParityOutputsNeedUpdate();
                default:
                    return true;
            }
        }

        private bool StepChangesState(int index)
        {
            switch (index)
            {
                case 4:
                case 8:
                case 9:
                case 13:
                case 16:
                case 17:
                case 22:
                case 25:
                case 26:
                case 27:
                case 28:
                    return false;
                default:
                    return true;
            }
        }

        private bool StepNeedsQuarantine(int index)
        {
            switch (index)
            {
                case 5:
                case 6:
                case 7:
                case 10:
                case 11:
                case 12:
                case 14:
                case 15:
                case 18:
                case 19:
                case 20:
                case 21:
                case 23:
                case 24:
                    return true;
                default:
                    return false;
            }
        }

        private string GetStabilizeStepSkipReason(int index)
        {
            switch (index)
            {
                case 5:
                    if (String.IsNullOrEmpty(ck3Bin) || !File.Exists(Path.Combine(ck3Bin, "ck3.exe")))
                        return "ck3.exe is not available for firewall rules.";
                    if (!IsAdministrator())
                        return "run CK3MPS as administrator to change firewall rules.";
                    return "CK3 inbound/outbound firewall rules already match the current ck3.exe path.";
                case 6:
                    return "the selected Windows game/network registry profile already matches.";
                case 7:
                    return "the selected power profile values already match or no power change is needed right now.";
                case 4:
                    return "adapter, route, MTU, DNS and TCP diagnostics already match the last saved snapshot.";
                case 8:
                    return "overlay/background process and service snapshot already matches the last saved snapshot.";
                case 9:
                    return "Paradox/Steam reachability status already matches the last saved snapshot.";
                case 10:
                    return "no selected file-changing launcher or CK3 step currently needs a backup.";
                case 11:
                    return "Steam launch options already keep -noasync, risky launch flags are absent, and Steam Cloud is already off or hidden.";
                case 12:
                    return "no launcher database or launcher cache target exists to rebuild.";
                case 13:
                    return "runtime hygiene status already matches the last saved snapshot.";
                case 14:
                    return "dlc_load.json already contains no active mods, no disabled DLC entries, and no UTF-8 BOM.";
                case 15:
                    return "pdx_settings.txt already matches the selected CK3MPS profile for " + CurrentGraphicsProfile() + ".";
                case 16:
                    return "runtime verification output already matches the current runtime and profile state.";
                case 17:
                    return "the in-game MP rules reference file is already up to date.";
                case 18:
                    return "the CK3 player UI state folder is already absent.";
                case 19:
                    return "OOS, crash, dump and exception folders are already clean.";
                case 20:
                    return "CK3 and launcher caches are already clean.";
                case 21:
                    return "no local .mod descriptor files are present.";
                case 22:
                    return "no suspect non-vanilla loader files were found.";
                case 23:
                    return "no suspicious Continue pointer, local save or Steam Cloud save needs quarantine right now.";
                case 24:
                    return "the selected CK3 Documents cleanup targets are already absent and CK3 profile files are already stable.";
                case 25:
                    return "the latest OOS summary and history already match current OOS evidence.";
                case 26:
                    return "the evidence pack files already match the current host-side state.";
                case 27:
                    return "the OOS prevention protocol is already up to date.";
                case 28:
                    return "the parity manifest and OOS risk report already match the current machine state.";
            }

            return "already in target state.";
        }

        private List<string> BuildStepPreviewDetails(int index)
        {
            EnsurePlanningSnapshot();
            if (index >= 0 && index < planningDetails.Length && planningDetails[index] != null)
                return new List<string>(planningDetails[index]);

            return ComputeStepPreviewDetails(index);
        }

        private List<string> ComputeStepPreviewDetails(int index)
        {
            List<string> details = new List<string>();
            string ck3Exe = String.IsNullOrEmpty(ck3Bin) ? "" : Path.Combine(ck3Bin, "ck3.exe");

            switch (index)
            {
                case 0:
                    details.Add("Create a Windows restore point with the CK3MPS timestamped description before changes start.");
                    break;
                case 3:
                    details.Add("Run DNS cache flush only.");
                    break;
                case 4:
                    details.Add("Update network diagnostics snapshot `" + StabilizerFile("ck3_stabilizer_network_diagnostics.txt") + "` only if adapter, route, MTU, DNS or TCP state changed.");
                    break;
                case 5:
                    details.Add("Ensure firewall rule `CK3MPS - CK3 Inbound` allows `" + ck3Exe + "`.");
                    details.Add("Ensure firewall rule `CK3MPS - CK3 Outbound` allows `" + ck3Exe + "`.");
                    details.Add("Delete legacy `CK3 Stabilizer - CK3 Inbound/Outbound` rules when they still exist.");
                    break;
                case 6:
                    if (!RegistryDwordEquals(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0))
                        details.Add("Set `HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\GameDVR\\AppCaptureEnabled = 0`.");
                    if (!RegistryDwordEquals(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0))
                        details.Add("Set `HKCU\\System\\GameConfigStore\\GameDVR_Enabled = 0`.");
                    if (!RegistryDwordEquals(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2))
                        details.Add("Set `HKCU\\System\\GameConfigStore\\GameDVR_FSEBehaviorMode = 2`.");
                    if (!RegistryDwordEquals(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1))
                        details.Add("Set `HKCU\\System\\GameConfigStore\\GameDVR_HonorUserFSEBehaviorMode = 1`.");
                    if (!RegistryDwordEquals(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 1))
                        details.Add("Set `HKCU\\System\\GameConfigStore\\GameDVR_DXGIHonorFSEWindowsCompatible = 1`.");
                    if (!RegistryDwordEquals(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_EFSEFeatureFlags", 0))
                        details.Add("Set `HKCU\\System\\GameConfigStore\\GameDVR_EFSEFeatureFlags = 0`.");
                    if (File.Exists(ck3Exe))
                    {
                        if (!RegistryStringContains(Registry.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", ck3Exe, "GpuPreference=2"))
                            details.Add("Set `HKCU\\Software\\Microsoft\\DirectX\\UserGpuPreferences[" + ck3Exe + "] = GpuPreference=2;`.");
                        if (!RegistryStringContains(Registry.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", ck3Exe, "DISABLEDXMAXIMIZEDWINDOWEDMODE"))
                            details.Add("Set `HKCU\\Software\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers[" + ck3Exe + "] = ~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE`.");
                    }
                    if (IsAdministrator())
                    {
                        if (!RegistryDwordEquals(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xffffffff)))
                            details.Add("Set `HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\NetworkThrottlingIndex = 0xffffffff`.");
                        if (!RegistryDwordEquals(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 10))
                            details.Add("Set `HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\SystemResponsiveness = 10`.");
                    }
                    if (TcpGlobalSettingsNeedUpdate())
                        details.Add("Set TCP globals to `rss=enabled autotuninglevel=normal ecncapability=disabled timestamps=disabled`.");
                    break;
                case 7:
                    if (PciExpressAspmNeedsUpdate())
                        details.Add("Set `powercfg /setacvalueindex SCHEME_CURRENT SUB_PCIEXPRESS ASPM 0`.");
                    if (PowerIdleAdapterNeedsUpdate())
                        details.Add("Set `powercfg /setacvalueindex SCHEME_CURRENT 19cbb8fa-5279-450e-9fac-8a3d5fedd0c1 12bbebe6-58d6-4636-95bb-3217ef867c1a 0`.");
                    if (PciExpressAspmNeedsUpdate() || PowerIdleAdapterNeedsUpdate())
                        details.Add("Apply the current power scheme again with `powercfg /setactive SCHEME_CURRENT`.");
                    break;
                case 8:
                    details.Add("Update overlay/background snapshot `" + StabilizerFile("ck3_stabilizer_overlay_scan.txt") + "` only if watched processes, services or power-plan state changed.");
                    break;
                case 9:
                    details.Add("Update online reachability snapshot `" + StabilizerFile("ck3_stabilizer_online_services.txt") + "` only if Paradox/Steam reachability status changed.");
                    break;
                case 10:
                    foreach (string path in EnumerateSteamAndLauncherBackupTargets())
                        details.Add("Copy backup of `" + path + "` into the current quarantine.");
                    break;
                case 11:
                    if (LaunchOptionsNeedUpdate())
                        details.Add("Normalize Steam CK3 launch options to keep `-noasync` and remove risky flags such as `debug_mode`, `dx11`, `opengl` and similar. New value: `" + NormalizeLaunchOptions(ExtractSteamLaunchOptions(), true) + "`.");
                    if (SteamCloudFlagNeedsUpdate())
                        details.Add("Set Steam CK3 cloud flag `cloudenabled` to `0` in `sharedconfig.vdf`.");
                    break;
                case 12:
                    foreach (string path in EnumerateLauncherRebuildTargets())
                        details.Add("Move `" + path + "` to quarantine so Paradox Launcher rebuilds a fresh copy.");
                    break;
                case 13:
                    details.Add("Update runtime hygiene snapshot `" + StabilizerFile("ck3_stabilizer_runtime_hygiene.txt") + "` only if CK3/Launcher running state changed.");
                    break;
                case 14:
                    details.Add("Rewrite `dlc_load.json` to exactly `{\"enabled_mods\":[],\"disabled_dlcs\":[]}` with UTF-8 no BOM.");
                    break;
                case 15:
                    foreach (string detail in BuildPdxSettingsDiffPreview())
                        details.Add(detail);
                    break;
                case 16:
                    details.Add("Update `" + StabilizerFile("ck3_stabilizer_runtime_verification.txt") + "` only if the runtime/profile snapshot changed.");
                    break;
                case 17:
                    details.Add("Update the in-game MP rules reference file `" + StabilizerFile("ck3_stabilizer_in_game_mp_settings.txt") + "` only if its guidance content changed.");
                    break;
                case 18:
                    details.Add("Move `" + Path.Combine(ck3Docs, "player") + "` to quarantine so CK3 recreates fresh UI state.");
                    break;
                case 19:
                    foreach (string path in EnumerateReportArchiveTargets())
                        details.Add("Move report target `" + path + "` to quarantine.");
                    break;
                case 20:
                    foreach (string path in EnumerateCacheCleanupTargets())
                        details.Add("Move cache target `" + path + "` to quarantine if it exists.");
                    break;
                case 21:
                    foreach (string path in EnumerateModDescriptorFiles())
                        details.Add("Move local mod descriptor `" + path + "` to quarantine.");
                    break;
                case 22:
                    details.Add("Write a binary inspection report for the " + CountSuspectBinaries() + " suspect non-vanilla loader file(s) that were found.");
                    break;
                case 23:
                    foreach (string path in EnumerateSuspiciousSaveHygieneTargets())
                        details.Add("Move suspicious save target `" + path + "` to quarantine.");
                    details.Add("Write the clean save launch note `" + StabilizerFile("ck3_stabilizer_clean_save_note.txt") + "`.");
                    break;
                case 24:
                    foreach (string path in EnumerateFolderCleanupTargets())
                        details.Add("Move cleanup target `" + path + "` to quarantine if it exists.");
                    if (DlcLoadNeedsRewrite())
                        details.Add("Rewrite `dlc_load.json` to the clean no-mod profile as part of cleanup.");
                    if (PdxSettingsNeedsRewrite())
                        details.Add("Rewrite `pdx_settings.txt` to the selected CK3MPS profile as part of cleanup.");
                    details.Add("Write the folder cleanup report `" + StabilizerFile("ck3_stabilizer_folder_cleanup.txt") + "`.");
                    break;
                case 25:
                    if (LatestOosSummaryNeedsUpdate())
                        details.Add("Update the latest OOS summary `" + StabilizerFile("ck3_stabilizer_latest_oos_summary.txt") + "` from the newest OOS metadata and nearby logs.");
                    if (OosHistoryTimelineNeedsUpdate())
                        details.Add("Update the OOS history timeline `" + StabilizerFile("ck3_stabilizer_oos_history.txt") + "` from all discovered OOS metadata files.");
                    break;
                case 26:
                    if (PortableTransferNotesNeedUpdate())
                        details.Add("Update portable transfer notes `" + StabilizerFile("ck3_stabilizer_portable_notes.txt") + "`.");
                    if (ExpectedProfileSnapshotForEvidencePackNeedsUpdate())
                        details.Add("Update expected profile snapshot `" + StabilizerFile("ck3_stabilizer_expected_profile_hashes.txt") + "` for reason `evidence pack`.");
                    if (RuntimeVerificationReportNeedsUpdate())
                        details.Add("Update runtime verification report `" + StabilizerFile("ck3_stabilizer_runtime_verification.txt") + "`.");
                    if (PreSessionPlanNeedsUpdate())
                        details.Add("Update pre-session plan `" + StabilizerFile("ck3_stabilizer_pre_session_plan.txt") + "`.");
                    if (SessionVerdictReportNeedsUpdate())
                        details.Add("Update session verdict `" + StabilizerFile("ck3_stabilizer_session_verdict.txt") + "`.");
                    if (CleanSaveLaunchNoteNeedsUpdate())
                        details.Add("Update clean save launch note `" + StabilizerFile("ck3_stabilizer_clean_save_note.txt") + "`.");
                    if (OosEvidencePackIndexNeedsUpdate())
                        details.Add("Update evidence pack index `" + StabilizerFile("ck3_stabilizer_evidence_pack_index.txt") + "`.");
                    break;
                case 27:
                    details.Add("Update the OOS prevention protocol `" + StabilizerFile("ck3_stabilizer_oos_protocol.txt") + "` only if the protocol text changed.");
                    break;
                case 28:
                    if (MultiplayerParityManifestNeedsUpdate())
                        details.Add("Update multiplayer parity manifest `" + StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt") + "`.");
                    if (OosRiskScoreReportNeedsUpdate())
                        details.Add("Update OOS risk score report `" + StabilizerFile("ck3_stabilizer_oos_risk_score.txt") + "`.");
                    break;
            }

            return details;
        }

        private void EnsurePlanningSnapshot()
        {
            string key = BuildCheckOnlyScanKey();
            if (hasPlanningSnapshot && String.Equals(planningSnapshotKey, key, StringComparison.Ordinal))
                return;

            planningSnapshotKey = key;
            hasPlanningSnapshot = true;
            for (int i = 0; i < planningShouldRun.Length; i++)
            {
                planningShouldRun[i] = ComputeShouldRunSelectedStabilizeStep(i);
                planningDetails[i] = ComputeStepPreviewDetails(i);
            }
        }

        private bool RuntimeVerificationReportNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_runtime_verification.txt"), BuildRuntimeVerificationReportText(), true);
        }

        private bool NetworkDiagnosticsNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_network_diagnostics.txt"), BuildNetworkDiagnosticsSnapshotText(), true);
        }

        private bool OverlayAndVpnSnapshotNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_overlay_scan.txt"), BuildOverlayAndVpnSnapshotText(), true);
        }

        private bool OnlineServicesSnapshotNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_online_services.txt"), BuildOnlineServicesSnapshotText(), true);
        }

        private bool RuntimeHygieneSnapshotNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_runtime_hygiene.txt"), BuildRuntimeHygieneSnapshotText(), true);
        }

        private bool StableGameRuleProfileNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_in_game_mp_settings.txt"), BuildStableGameRuleProfileText(), true);
        }

        private bool LatestOosSummaryNeedsUpdate()
        {
            List<string> signalLines;
            string latest = FindLatestOosMetadataFile();
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_latest_oos_summary.txt"), BuildLatestOosSummaryText(latest, out signalLines), true);
        }

        private bool OosHistoryTimelineNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_oos_history.txt"), BuildOosHistoryTimelineText(), true);
        }

        private bool PortableTransferNotesNeedUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_portable_notes.txt"), BuildPortableTransferNotesText(), true);
        }

        private bool ExpectedProfileSnapshotForEvidencePackNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_expected_profile_hashes.txt"), BuildExpectedProfileSnapshotText("evidence pack"), true);
        }

        private bool PreSessionPlanNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_pre_session_plan.txt"), BuildPreSessionPlanText(), true);
        }

        private bool SessionVerdictReportNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_session_verdict.txt"), BuildSessionVerdictReportText(), true);
        }

        private bool CleanSaveLaunchNoteNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_clean_save_note.txt"), BuildCleanSaveLaunchNoteText(), true);
        }

        private bool OosEvidencePackIndexNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_evidence_pack_index.txt"), BuildOosEvidencePackIndexText(), true);
        }

        private bool OosEvidencePackNeedsUpdate()
        {
            return PortableTransferNotesNeedUpdate()
                || ExpectedProfileSnapshotForEvidencePackNeedsUpdate()
                || RuntimeVerificationReportNeedsUpdate()
                || PreSessionPlanNeedsUpdate()
                || SessionVerdictReportNeedsUpdate()
                || CleanSaveLaunchNoteNeedsUpdate()
                || OosEvidencePackIndexNeedsUpdate();
        }

        private bool OosPreventionProtocolNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_oos_protocol.txt"), BuildOosPreventionProtocolText(), true);
        }

        private bool MultiplayerParityManifestNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt"), BuildMultiplayerParityManifestText(), true);
        }

        private bool OosRiskScoreReportNeedsUpdate()
        {
            return FileMeaningfullyDiffers(StabilizerFile("ck3_stabilizer_oos_risk_score.txt"), BuildOosRiskScoreReportText(), true);
        }

        private bool MultiplayerParityOutputsNeedUpdate()
        {
            return MultiplayerParityManifestNeedsUpdate() || OosRiskScoreReportNeedsUpdate();
        }

        private bool FirewallRulesNeedUpdate()
        {
            string exe = String.IsNullOrEmpty(ck3Bin) ? "" : Path.Combine(ck3Bin, "ck3.exe");
            if (!File.Exists(exe) || !IsAdministrator())
                return false;

            string quotedExe = "\"" + exe + "\"";
            string inbound = ReadFirewallRule("CK3MPS - CK3 Inbound");
            string outbound = ReadFirewallRule("CK3MPS - CK3 Outbound");
            string legacyInbound = ReadFirewallRule("CK3 Stabilizer - CK3 Inbound");
            string legacyOutbound = ReadFirewallRule("CK3 Stabilizer - CK3 Outbound");

            return !FirewallRuleMatches(inbound, quotedExe, "in")
                || !FirewallRuleMatches(outbound, quotedExe, "out")
                || FirewallRuleOutputLooksPresent(legacyInbound)
                || FirewallRuleOutputLooksPresent(legacyOutbound);
        }

        private bool PowerAdapterProfileNeedsUpdate()
        {
            string pcie = RunCommandQuiet("powercfg.exe", "/query SCHEME_CURRENT SUB_PCIEXPRESS ASPM");
            string adapter = RunCommandQuiet("powercfg.exe", "/query SCHEME_CURRENT 19cbb8fa-5279-450e-9fac-8a3d5fedd0c1 12bbebe6-58d6-4636-95bb-3217ef867c1a");

            if (String.IsNullOrEmpty(pcie) || String.IsNullOrEmpty(adapter))
                return true;

            return pcie.IndexOf("0x00000000", StringComparison.OrdinalIgnoreCase) < 0
                || adapter.IndexOf("0x00000000", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private bool HasSteamAndLauncherBackupTargets()
        {
            return EnumerateSteamAndLauncherBackupTargets().Count > 0;
        }

        private List<string> EnumerateSteamAndLauncherBackupTargets()
        {
            List<string> paths = new List<string>();
            if (IsStepChecked(11) && LaunchOptionsNeedUpdate())
                AddExistingFile(paths, localConfig);
            if (IsStepChecked(11) && SteamCloudFlagNeedsUpdate())
                AddExistingFile(paths, sharedConfig);
            if (IsStepChecked(12) && LauncherRebuildHasTargets())
                AddExistingFile(paths, Path.Combine(ck3Docs, "launcher-v2.sqlite"));
            if (IsStepChecked(14) && DlcLoadNeedsRewrite())
                AddExistingFile(paths, Path.Combine(ck3Docs, "dlc_load.json"));
            if (IsStepChecked(15) && PdxSettingsNeedsRewrite())
                AddExistingFile(paths, Path.Combine(ck3Docs, "pdx_settings.txt"));
            return paths;
        }

        private bool SteamSettingsNeedUpdate()
        {
            return LaunchOptionsNeedUpdate() || SteamCloudFlagNeedsUpdate();
        }

        private bool LaunchOptionsNeedUpdate()
        {
            return !HasNoAsync() || HasRiskyLaunchOptions();
        }

        private bool SteamCloudFlagNeedsUpdate()
        {
            if (String.IsNullOrEmpty(sharedConfig) || !File.Exists(sharedConfig))
                return false;

            string text = File.ReadAllText(sharedConfig, Encoding.UTF8);
            int appIndex = text.IndexOf("\"1158310\"", StringComparison.OrdinalIgnoreCase);
            if (appIndex < 0)
                return false;

            int open = text.IndexOf('{', appIndex);
            int close = FindMatchingBrace(text, open);
            if (open < 0 || close < open)
                return false;

            string block = text.Substring(open, close - open + 1);
            Match m = Regex.Match(block, "\"cloudenabled\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success && m.Groups[1].Value != "0";
        }

        private bool LauncherRebuildHasTargets()
        {
            return EnumerateLauncherRebuildTargets().Count > 0;
        }

        private List<string> EnumerateLauncherRebuildTargets()
        {
            List<string> paths = new List<string>();
            AddExistingFile(paths, Path.Combine(ck3Docs, "launcher-v2.sqlite"));
            AddExistingDirectory(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paradox Interactive", "launcher-v2", "logs"));
            AddExistingDirectory(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paradox Interactive", "launcher-v2", "game-metadata"));
            AddExistingDirectory(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paradox Interactive", "launcher-v2", "telemetry-whitelist-cache"));
            return paths;
        }

        private bool DlcLoadNeedsRewrite()
        {
            string path = Path.Combine(ck3Docs, "dlc_load.json");
            if (!File.Exists(path))
                return true;

            string current = File.ReadAllText(path, Encoding.UTF8).Trim();
            return HasUtf8Bom(path)
                || !String.Equals(current, "{\"enabled_mods\":[],\"disabled_dlcs\":[]}", StringComparison.Ordinal);
        }

        private bool PdxSettingsNeedsRewrite()
        {
            string path = Path.Combine(ck3Docs, "pdx_settings.txt");
            string current = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
            string expected = ApplyStablePdxSettingsToText(current);
            return !File.Exists(path)
                || HasUtf8Bom(path)
                || !String.Equals(current, expected, StringComparison.Ordinal);
        }

        private int CountReportItemsForArchive()
        {
            return CountItems(Path.Combine(ck3Docs, "oos"))
                + CountItems(Path.Combine(ck3Docs, "crashes"))
                + CountItems(Path.Combine(ck3Docs, "dumps"))
                + CountItems(Path.Combine(ck3Docs, "exceptions"));
        }

        private List<string> EnumerateReportArchiveTargets()
        {
            List<string> paths = new List<string>();
            AddExistingDirectory(paths, Path.Combine(ck3Docs, "oos"));
            AddExistingDirectory(paths, Path.Combine(ck3Docs, "crashes"));
            AddExistingDirectory(paths, Path.Combine(ck3Docs, "dumps"));
            AddExistingDirectory(paths, Path.Combine(ck3Docs, "exceptions"));
            return paths;
        }

        private List<string> EnumerateCacheCleanupTargets()
        {
            List<string> paths = new List<string>();
            AddExistingDirectory(paths, Path.Combine(ck3Docs, "shadercache"));
            AddExistingDirectory(paths, Path.Combine(ck3Docs, ".launcher-cache"));

            string localLauncher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paradox Interactive", "launcher-v2", "chromium-data");
            AddExistingDirectory(paths, Path.Combine(localLauncher, "Cache"));
            AddExistingDirectory(paths, Path.Combine(localLauncher, "GPUCache"));
            AddExistingDirectory(paths, Path.Combine(localLauncher, "DawnGraphiteCache"));
            AddExistingDirectory(paths, Path.Combine(localLauncher, "DawnWebGPUCache"));
            AddExistingDirectory(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paradox Interactive", "launcher-v2", "cache"));
            return paths;
        }

        private int CountModDescriptorFiles()
        {
            string modDir = Path.Combine(ck3Docs, "mod");
            return Directory.Exists(modDir) ? Directory.GetFiles(modDir, "*.mod", SearchOption.TopDirectoryOnly).Length : 0;
        }

        private List<string> EnumerateModDescriptorFiles()
        {
            List<string> paths = new List<string>();
            string modDir = Path.Combine(ck3Docs, "mod");
            if (!Directory.Exists(modDir))
                return paths;

            foreach (string file in Directory.GetFiles(modDir, "*.mod", SearchOption.TopDirectoryOnly))
                paths.Add(file);
            return paths;
        }

        private bool SaveHygieneNeedsChanges()
        {
            if (!File.Exists(StabilizerFile("ck3_stabilizer_clean_save_note.txt")))
                return true;

            if (File.Exists(Path.Combine(ck3Docs, "continue_game.json")) && ActiveContinueSaveNameSuspicious())
                return true;

            if (CountSuspiciousLocalSaveFiles() > 0)
                return true;

            return CountSuspiciousSteamCloudSaveNames() > 0 && !ProcessRunningContains("steam");
        }

        private int CountSuspiciousLocalSaveFiles()
        {
            string saveDir = Path.Combine(ck3Docs, "save games");
            if (!Directory.Exists(saveDir))
                return 0;

            string bestClean = FindBestCleanManualSave();
            if (String.IsNullOrEmpty(bestClean) || IsSuspiciousSaveName(Path.GetFileName(bestClean)))
                return 0;

            int count = 0;
            foreach (string file in Directory.GetFiles(saveDir, "*.ck3", SearchOption.TopDirectoryOnly))
            {
                if (!IsSuspiciousSaveName(Path.GetFileName(file)))
                    continue;
                if (String.Equals(Path.GetFullPath(file), Path.GetFullPath(bestClean), StringComparison.OrdinalIgnoreCase))
                    continue;
                count++;
            }
            return count;
        }

        private List<string> EnumerateSuspiciousSaveHygieneTargets()
        {
            List<string> paths = new List<string>();
            string continuePath = Path.Combine(ck3Docs, "continue_game.json");
            if (File.Exists(continuePath) && ActiveContinueSaveNameSuspicious())
                paths.Add(continuePath);

            string saveDir = Path.Combine(ck3Docs, "save games");
            string bestClean = FindBestCleanManualSave();
            if (Directory.Exists(saveDir) && !String.IsNullOrEmpty(bestClean) && !IsSuspiciousSaveName(Path.GetFileName(bestClean)))
            {
                foreach (string file in Directory.GetFiles(saveDir, "*.ck3", SearchOption.TopDirectoryOnly))
                {
                    if (!IsSuspiciousSaveName(Path.GetFileName(file)))
                        continue;
                    if (String.Equals(Path.GetFullPath(file), Path.GetFullPath(bestClean), StringComparison.OrdinalIgnoreCase))
                        continue;
                    paths.Add(file);
                }
            }

            if (!ProcessRunningContains("steam"))
            {
                foreach (string dir in DetectSteamCloudSaveDirs())
                {
                    foreach (string file in Directory.GetFiles(dir, "*.ck3", SearchOption.TopDirectoryOnly))
                    {
                        if (IsSuspiciousSaveName(Path.GetFileName(file)))
                            paths.Add(file);
                    }
                }
            }

            return paths;
        }

        private bool FolderCleanupNeedsChanges()
        {
            if (DlcLoadNeedsRewrite() || PdxSettingsNeedsRewrite() || !NoLegacyStabilizerArtifactsInCk3Docs())
                return true;

            return EnumerateFolderCleanupTargets().Count > 0;
        }

        private List<string> EnumerateFolderCleanupTargets()
        {
            List<string> paths = new List<string>();
            AddExistingDirectory(paths, Path.Combine(ck3Docs, ".launcher-cache"));
            AddExistingDirectory(paths, Path.Combine(ck3Docs, "shadercache"));
            AddExistingDirectory(paths, Path.Combine(ck3Docs, "logs"));
            AddExistingDirectory(paths, Path.Combine(ck3Docs, "newsfeed"));
            AddExistingDirectory(paths, Path.Combine(ck3Docs, "oos"));
            AddExistingDirectory(paths, Path.Combine(ck3Docs, "crashes"));
            AddExistingDirectory(paths, Path.Combine(ck3Docs, "dumps"));
            AddExistingDirectory(paths, Path.Combine(ck3Docs, "exceptions"));
            AddExistingDirectory(paths, Path.Combine(ck3Docs, "playsets_backup"));
            AddExistingFile(paths, Path.Combine(ck3Docs, "game_data.json"));
            AddExistingFile(paths, Path.Combine(ck3Docs, "mods_registry.json"));
            AddExistingFile(paths, Path.Combine(ck3Docs, "content_load.json"));
            AddExistingFile(paths, Path.Combine(ck3Docs, "launcher-v2.sqlite"));
            AddExistingFile(paths, Path.Combine(ck3Docs, "launcher-v2_backup.sqlite"));

            if (Directory.Exists(ck3Docs))
            {
                foreach (string dir in Directory.GetDirectories(ck3Docs, "desync_*", SearchOption.TopDirectoryOnly))
                    paths.Add(dir);
                foreach (string dir in Directory.GetDirectories(ck3Docs, "mp_stability_hardening_*", SearchOption.TopDirectoryOnly))
                    paths.Add(dir);
                foreach (string dir in Directory.GetDirectories(ck3Docs, "oos_archive_*", SearchOption.TopDirectoryOnly))
                    paths.Add(dir);
                foreach (string dir in Directory.GetDirectories(ck3Docs, "modded_saves_quarantine*", SearchOption.TopDirectoryOnly))
                    paths.Add(dir);
                foreach (string file in Directory.GetFiles(ck3Docs, "ck3_stabilizer_*", SearchOption.TopDirectoryOnly))
                    paths.Add(file);
                foreach (string dir in Directory.GetDirectories(ck3Docs, "ck3_stabilizer_*", SearchOption.TopDirectoryOnly))
                    paths.Add(dir);
                foreach (string dir in Directory.GetDirectories(ck3Docs, "_ck3_stabilizer_quarantine_*", SearchOption.TopDirectoryOnly))
                    paths.Add(dir);
            }

            return paths;
        }

        private void AddExistingFile(List<string> paths, string path)
        {
            if (!String.IsNullOrEmpty(path) && File.Exists(path))
                paths.Add(path);
        }

        private void AddExistingDirectory(List<string> paths, string path)
        {
            if (!String.IsNullOrEmpty(path) && Directory.Exists(path))
                paths.Add(path);
        }

        private List<string> BuildPdxSettingsDiffPreview()
        {
            List<string> lines = new List<string>();
            string path = Path.Combine(ck3Docs, "pdx_settings.txt");
            string current = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
            string expected = ApplyStablePdxSettingsToText(current);

            AddPdxSettingDiff(lines, current, expected, "game", "autosave");
            AddPdxSettingDiff(lines, current, expected, "game", "debug_saves");
            AddPdxSettingDiff(lines, current, expected, "game", "cloud_save");
            AddPdxSettingDiff(lines, current, expected, "game", "save_on_exit");
            AddPdxSettingDiff(lines, current, expected, "game", "rich_presence");
            AddPdxSettingDiff(lines, current, expected, "game", "file_transfer_speed");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "renderer");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "display_mode");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "vsync");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "adaptive_framerate");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "setting_framerate_cap");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "quality");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "texture_quality");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "shadowmap_resolution");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "refraction_quality");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "mesh_lod_bias");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "mapobject_quality");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "anti_aliasing");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "anisotropic_filtering");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "portrait_multi_sampling");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "terrain_smoothing");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "bloom_enabled");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "ssao");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "depthoffield");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "lensflare");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "secondary_lensflare");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "mesh_lod_fade");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "animated_portraits");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "portraits_ssao");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "portraits_bloom");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "advanced_shaders");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "winter_particle_effects");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "cloud_shadow_enabled");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "tree_dithering_enabled");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "court_scene_low_priority_characters");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "royal_court_anim_camera_idle");
            AddPdxSettingDiff(lines, current, expected, "Graphics", "royal_court_anim_camera_transition");
            AddPdxSettingDiff(lines, current, expected, "System", "language");
            AddPdxSettingDiff(lines, current, expected, "Audio", "audio_debug_log_level");

            if (lines.Count == 0)
                lines.Add(HasUtf8Bom(path)
                    ? "Rewrite `pdx_settings.txt` only to remove the UTF-8 BOM while keeping the same values."
                    : "No `pdx_settings.txt` value change is needed.");

            return lines;
        }

        private void AddPdxSettingDiff(List<string> lines, string current, string expected, string section, string key)
        {
            string before = NormalizePreviewSettingBlock(ExtractPreviewSettingBlock(current, section, key));
            string after = NormalizePreviewSettingBlock(ExtractPreviewSettingBlock(expected, section, key));
            if (String.Equals(before, after, StringComparison.Ordinal))
                return;

            lines.Add("Set `" + section + "." + key + "` from `" + NullText(before) + "` to `" + NullText(after) + "`.");
        }

        private string ExtractPreviewSettingBlock(string text, string section, string key)
        {
            string body = ExtractSectionBody(text, section);
            if (String.IsNullOrEmpty(body))
                return "";

            Match match = Regex.Match(body, "\"" + Regex.Escape(key) + "\"\\s*=\\s*\\{(.*?)\\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "";
        }

        private string NormalizePreviewSettingBlock(string block)
        {
            if (String.IsNullOrWhiteSpace(block))
                return "(missing)";

            return Regex.Replace(block.Trim(), "\\s+", " ");
        }
    }
}
