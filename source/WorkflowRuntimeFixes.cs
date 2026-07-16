using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ConfigureWorkflowRuntimeFixes();
        }

        private void ConfigureWorkflowRuntimeFixes()
        {
            ConfigureMainButtonLabels();
            ConfigureChecklistLabels();
            ConfigureCombinedWorkflowFixButton();
            ConfigureWorkflowParityControls();
            UpdateWorkflowRuntimeFixLayout();

            workflowModeBox.SelectedIndexChanged += delegate
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    ConfigureCombinedWorkflowFixButton();
                    ConfigureWorkflowParityControls();
                    UpdateWorkflowRuntimeFixLayout();
                });
            };
        }

        private void ConfigureMainButtonLabels()
        {
            checkButton.Text = "Scan Settings";
            checkButton.Size = new Size(130, checkButton.Height);
            exportScanReportButton.Text = "Scan Export";
            exportScanReportButton.Size = new Size(130, exportScanReportButton.Height);
            previewButton.Text = "Review Settings";
            previewButton.Size = new Size(130, previewButton.Height);
            ReplaceClickHandlers(checkButton, delegate { RunCheckOnlyAndUnlockExport(); });
            if (!String.IsNullOrWhiteSpace(lastCheckOnlyReportText))
                exportScanReportButton.Enabled = true;
            LayoutMainTabControls();
        }

        private void ConfigureChecklistLabels()
        {
            foreach (StepGroupUi group in stepGroups)
            {
                if (String.Equals(group.Title, "Safety Options", StringComparison.OrdinalIgnoreCase))
                {
                    group.Title = "Backup";
                    group.TitleLabel.Text = "Backup";
                }
            }
        }

        private async void RunCheckOnlyAndUnlockExport()
        {
            await RunCheckOnlyAsync();
            if (!String.IsNullOrWhiteSpace(lastCheckOnlyReportText))
                exportScanReportButton.Enabled = true;
        }

        private void ConfigureCombinedWorkflowFixButton()
        {
            workflowApplySafeStartButton.Text = "Fix save + host";
            workflowApplySafeStartButton.Size = new Size(132, workflowApplySafeStartButton.Height);
            workflowRepairSaveButton.Visible = false;
            workflowRepairSaveButton.Enabled = false;
            ReplaceClickHandlers(workflowApplySafeStartButton, delegate { RunWorkflowSaveAndHostFix(); });
            stepToolTip.SetToolTip(workflowApplySafeStartButton, BuildWorkflowFixSaveAndHostHintText());
        }

        private void ConfigureWorkflowParityControls()
        {
            workflowParityRoomButton.Text = "Parity room";
            ReplaceClickHandlers(workflowParityRoomButton, delegate { OpenParityRoomWithOnlineDashboardText(); });
            stepToolTip.SetToolTip(workflowParityRoomButton, "Online-capable parity room dashboard. Host creates a TCP room and shares host/IP, port, code and secret with other players.");

            workflowCompareParityButton.Visible = false;
            workflowCompareParityButton.Enabled = false;
            AddCompareParityToMoreMenu();
        }

        private void AddCompareParityToMoreMenu()
        {
            foreach (ToolStripItem item in workflowMoreMenu.Items)
            {
                if (String.Equals(item.Text, "Compare parity", StringComparison.OrdinalIgnoreCase))
                    return;
            }

            workflowMoreMenu.Items.Insert(0, new ToolStripMenuItem("Compare parity", null, delegate { CompareWorkflowParity(); }));
        }

        private void UpdateWorkflowRuntimeFixLayout()
        {
            const int actionGap = 8;
            workflowParityRoomButton.Location = new Point(workflowApplySafeStartButton.Right + actionGap, workflowParityRoomButton.Top);
            workflowMoreButton.Location = new Point(workflowParityRoomButton.Right + actionGap, workflowMoreButton.Top);
            workflowSaveBox.Width = Math.Max(160, workflowHeaderPanel.ClientSize.Width - workflowSaveBox.Left - workflowSaveBrowseButton.Width - 28);
            workflowSaveBrowseButton.Location = new Point(workflowSaveBox.Right + 10, workflowSaveBrowseButton.Top);
        }

        private void ReplaceClickHandlers(Button button, EventHandler replacement)
        {
            if (button == null || replacement == null)
                return;

            try
            {
                PropertyInfo eventsProperty = typeof(Component).GetProperty("Events", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo clickField = typeof(Control).GetField("EventClick", BindingFlags.Static | BindingFlags.NonPublic);
                if (eventsProperty != null && clickField != null)
                {
                    EventHandlerList events = eventsProperty.GetValue(button, null) as EventHandlerList;
                    object clickKey = clickField.GetValue(null);
                    Delegate existing = events == null || clickKey == null ? null : events[clickKey];
                    if (existing != null)
                    {
                        foreach (Delegate handler in existing.GetInvocationList())
                            button.Click -= (EventHandler)handler;
                    }
                }
            }
            catch
            {
            }

            button.Click += replacement;
        }

        private string BuildWorkflowFixSaveAndHostHintText()
        {
            return "Runs both repair paths in the correct between-session order: save baseline/safe copy first, then host profile, reports and parity manifest. Close CK3 before using it.";
        }

        private void RunWorkflowSaveAndHostFix()
        {
            if (IsGameRunning())
            {
                MessageBox.Show("Close CK3 and Paradox Launcher before running Fix save + host.", "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            UseWaitCursor = true;
            workflowApplySafeStartButton.Enabled = false;
            int beforeHostScore = -1;
            int afterHostScore = -1;
            int beforeSaveScore = -1;
            int afterSaveScore = -1;
            List<string> warnings = new List<string>();

            try
            {
                EnsureStabilizerRoot();
                if (String.IsNullOrEmpty(GetKnownQuarantine()))
                    CreateQuarantine();

                HostSuitabilityResult beforeHost = AnalyzeHostSuitability();
                HostSaveCandidateResult beforeSave = AnalyzeWorkflowHostSaveCandidate();
                beforeHostScore = beforeHost.Score;
                beforeSaveScore = beforeSave.Score;

                try
                {
                    bool repaired = EnsureSafeWorkflowHostSave();
                    PrepareWorkflowSaveSurgeryBaseline();
                    Log(repaired ? "OK   Workflow save repair applied." : "WARN Workflow save could not be auto-repaired; baseline report prepared.");
                }
                catch (Exception ex)
                {
                    warnings.Add("Save repair: " + ex.Message);
                    Log("WARN Workflow save repair failed: " + ex.Message);
                }

                try
                {
                    if (!ValidateBeforeRun())
                        throw new InvalidOperationException("Configured game/settings paths are not valid.");

                    BackupSteamAndLauncherSettings();
                    StabilizeSteamSettings();
                    ForceNoMods();
                    StabilizePdxSettings();
                    WriteStableGameRuleProfile();
                    Log("OK   Workflow host profile repair applied.");
                }
                catch (Exception ex)
                {
                    warnings.Add("Host repair: " + ex.Message);
                    Log("WARN Workflow host repair failed: " + ex.Message);
                }

                WriteHostSavePreparationReport();
                WriteMultiplayerParityManifest();
                WriteHostSuitabilityReport();
                WriteRuntimeVerificationReport();
                InvalidateHostSuitabilityCache();
                InvalidateHostSaveAnalysisCache();
                ClearWorkflowScenarioSnapshots();

                HostSuitabilityResult afterHost = AnalyzeHostSuitability();
                HostSaveCandidateResult afterSave = AnalyzeWorkflowHostSaveCandidate();
                afterHostScore = afterHost.Score;
                afterSaveScore = afterSave.Score;

                List<string> blockers = CollectRemainingWorkflowAutoBlockers();
                bool ready = blockers.Count == 0;
                string status = "Fix save + host finished. Host " + beforeHostScore + " -> " + afterHostScore + ", save " + beforeSaveScore + " -> " + afterSaveScore + ".";
                if (!ready)
                    status += " Remaining blockers: " + blockers.Count + ".";
                if (warnings.Count > 0)
                    status += " Warnings: " + warnings.Count + ".";

                BeginWorkflowScenarioRefresh();
                SetStatusText(status);
                Log((ready ? "OK   " : "WARN ") + status);

                if (!ready || warnings.Count > 0)
                {
                    List<string> lines = new List<string>();
                    lines.Add(status);
                    if (blockers.Count > 0)
                    {
                        lines.Add("");
                        lines.Add("Still not OK:");
                        foreach (string blocker in blockers)
                            lines.Add("- " + blocker);
                    }
                    if (warnings.Count > 0)
                    {
                        lines.Add("");
                        lines.Add("Warnings:");
                        foreach (string warning in warnings)
                            lines.Add("- " + warning);
                    }
                    MessageBox.Show(String.Join("\r\n", lines.ToArray()), "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(status, "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                SetStatusText("Fix save + host failed: " + ex.Message);
                Log("WARN Fix save + host failed: " + ex.Message);
                MessageBox.Show("Fix save + host failed.\r\n\r\n" + ex.Message, "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
                workflowApplySafeStartButton.Enabled = true;
                UpdateWorkflowActionAvailability();
                ConfigureCombinedWorkflowFixButton();
            }
        }

        private List<string> CollectRemainingWorkflowAutoBlockers()
        {
            List<string> blockers = new List<string>();
            WorkflowScenarioSnapshot snapshot = BuildWorkflowScenarioSnapshotCore(currentWorkflowScenario, CancellationToken.None);
            foreach (WorkflowStepState state in snapshot.States)
            {
                if (state.Required && state.AutoManaged && !state.Passed)
                    blockers.Add(MakeWorkflowStepLabelReadable(state));
            }
            return blockers;
        }

        private void OpenParityRoomWithOnlineDashboardText()
        {
            Timer patchTimer = new Timer();
            patchTimer.Interval = 250;
            patchTimer.Tick += delegate
            {
                Form form = FindOpenParityRoomForm();
                if (form == null)
                    return;
                PatchParityRoomOnlineText(form);
                if (form.IsDisposed)
                    patchTimer.Stop();
            };
            patchTimer.Start();
            try
            {
                OpenParityRoom();
            }
            finally
            {
                patchTimer.Stop();
                patchTimer.Dispose();
            }
        }

        private Form FindOpenParityRoomForm()
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form != null && form != this && form.Text.IndexOf("parity room", StringComparison.OrdinalIgnoreCase) >= 0)
                    return form;
            }
            return null;
        }

        private void PatchParityRoomOnlineText(Control root)
        {
            foreach (Control control in root.Controls)
            {
                Button button = control as Button;
                if (button != null)
                {
                    if (String.Equals(button.Text, "Create local room", StringComparison.OrdinalIgnoreCase))
                        button.Text = "Create online room";
                }

                Label label = control as Label;
                if (label != null)
                    label.Text = PatchParityRoomOnlineString(label.Text);

                RichTextBox richText = control as RichTextBox;
                if (richText != null)
                {
                    string patched = PatchParityRoomOnlineString(richText.Text);
                    if (!String.Equals(richText.Text, patched, StringComparison.Ordinal))
                        richText.Text = patched;
                }

                if (control.HasChildren)
                    PatchParityRoomOnlineText(control);
            }
        }

        private string PatchParityRoomOnlineString(string value)
        {
            string text = value ?? "";
            string host = GetOnlineParityAdvertisedAddress();
            text = text.Replace("Create a local loopback room or join an existing local host room.", "Create an online direct room or join a host by reachable IP. Do not use 127.0.0.1 for another player.");
            text = text.Replace("Create a local loopback room or join an existing local host room", "Create an online direct room or join a host by reachable IP");
            text = text.Replace("local room", "online room");
            text = text.Replace("local host room", "online host room");
            text = text.Replace("Live local room", "Live online-capable room");
            text = text.Replace("Host: 127.0.0.1", "Host/IP to share: " + host);
            return text;
        }

        private string GetOnlineParityAdvertisedAddress()
        {
            string address = GetPrimaryNonLoopbackIPv4Address();
            return String.IsNullOrWhiteSpace(address) ? "<your public/VPN IP>" : address;
        }

        private string GetPrimaryNonLoopbackIPv4Address()
        {
            try
            {
                foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;
                    IPInterfaceProperties properties = networkInterface.GetIPProperties();
                    foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                            return address.Address.ToString();
                    }
                }
            }
            catch
            {
            }
            return "";
        }
    }
}
