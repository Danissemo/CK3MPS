using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
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
            ConfigureCombinedWorkflowFixButton();
            ConfigureOnlineParityRoomButton();
            UpdateWorkflowRuntimeFixLayout();
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

        private void ConfigureOnlineParityRoomButton()
        {
            workflowParityRoomButton.Text = "Online parity";
            ReplaceClickHandlers(workflowParityRoomButton, delegate { OpenOnlineParityRoom(); });
            stepToolTip.SetToolTip(workflowParityRoomButton, "Online direct parity room. Host creates a TCP room and shares host/IP, port, code and secret with other players.");
        }

        private void UpdateWorkflowRuntimeFixLayout()
        {
            const int actionGap = 8;
            workflowCompareParityButton.Location = new Point(workflowApplySafeStartButton.Right + actionGap, workflowCompareParityButton.Top);
            workflowParityRoomButton.Location = new Point(workflowCompareParityButton.Right + actionGap, workflowParityRoomButton.Top);
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
                // If reflection fails, keep the replacement at the end. The combined action is idempotent enough to stay safe.
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
                BeginWorkflowScenarioRefresh();

                HostSuitabilityResult afterHost = AnalyzeHostSuitability();
                HostSaveCandidateResult afterSave = AnalyzeWorkflowHostSaveCandidate();
                afterHostScore = afterHost.Score;
                afterSaveScore = afterSave.Score;

                string status = "Fix save + host finished. Host " + beforeHostScore + " -> " + afterHostScore + ", save " + beforeSaveScore + " -> " + afterSaveScore + ".";
                if (warnings.Count > 0)
                    status += " Warnings: " + warnings.Count + ".";
                SetStatusText(status);
                Log("OK   " + status);

                if (warnings.Count > 0)
                    MessageBox.Show(status + "\r\n\r\n" + String.Join("\r\n", warnings.ToArray()), "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    MessageBox.Show(status, "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            }
        }

        private void OpenOnlineParityRoom()
        {
            ParityRoomSession session = new ParityRoomSession();
            session.LocalPlayerLabel = Environment.UserName;

            Form form = new Form();
            form.Text = "CK3MPS online parity room";
            form.StartPosition = FormStartPosition.CenterParent;
            form.Size = new Size(860, 560);
            form.MinimumSize = new Size(760, 500);
            form.Padding = new Padding(12);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            form.Controls.Add(layout);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.AutoSize = true;
            bar.Dock = DockStyle.Fill;
            bar.WrapContents = true;
            bar.Margin = new Padding(0, 0, 0, 8);
            layout.Controls.Add(bar, 0, 0);

            Button createButton = new Button { Text = "Create online room", Size = new Size(150, 32), Margin = new Padding(0, 0, 8, 8) };
            Button joinButton = new Button { Text = "Join room", Size = new Size(100, 32), Margin = new Padding(0, 0, 8, 8) };
            Button sendParityButton = new Button { Text = "Send parity", Size = new Size(104, 32), Margin = new Padding(0, 0, 8, 8) };
            Button sendOosButton = new Button { Text = "Send OOS", Size = new Size(96, 32), Margin = new Padding(0, 0, 8, 8) };
            Button compareButton = new Button { Text = "Compare", Size = new Size(92, 32), Margin = new Padding(0, 0, 8, 8) };
            bar.Controls.Add(createButton);
            bar.Controls.Add(joinButton);
            bar.Controls.Add(sendParityButton);
            bar.Controls.Add(sendOosButton);
            bar.Controls.Add(compareButton);

            Label infoLabel = new Label();
            infoLabel.AutoSize = false;
            infoLabel.Height = 56;
            infoLabel.Dock = DockStyle.Fill;
            infoLabel.Text = "Create an online room or join a host by address. This uses direct TCP, not loopback/LAN-only 127.0.0.1.";
            layout.Controls.Add(infoLabel, 0, 1);

            RichTextBox output = new RichTextBox();
            ConfigureLogView(output);
            output.Dock = DockStyle.Fill;
            output.Text = "Online parity room is ready.\r\n\r\nHost: click Create online room, then share host/IP, port, code and secret.\r\nPlayer: click Join room and paste the host's details.";
            layout.Controls.Add(output, 0, 2);

            Action refreshStatus = delegate { RefreshOnlineParityRoomStatus(session, infoLabel, output, false); };

            createButton.Click += delegate
            {
                try
                {
                    StartParityRoomHost(session, delegate
                    {
                        if (!form.IsDisposed && form.IsHandleCreated)
                            form.BeginInvoke((MethodInvoker)delegate { RefreshOnlineParityRoomStatus(session, infoLabel, output, false); });
                    });
                    RefreshParityRoomLocalState(session, true, true);
                    RefreshOnlineParityRoomStatus(session, infoLabel, output, false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not create online parity room.\r\n\r\n" + ex.Message, "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Log("WARN Online parity room create failed: " + ex.Message);
                }
            };

            joinButton.Click += async delegate
            {
                string host;
                int port;
                string code;
                string secret;
                string playerLabel;
                if (!ShowParityJoinDialog(form, out host, out port, out code, out secret, out playerLabel))
                    return;

                session.Joined = true;
                session.Hosting = false;
                session.JoinHost = host;
                session.JoinPort = port;
                session.RoomCode = code;
                session.SharedSecret = secret;
                session.LocalPlayerLabel = playerLabel;

                await RunOnlineParityAction(form, joinButton, "join room", delegate
                {
                    SendParityRoomPayload(session, true, false);
                }, delegate
                {
                    RefreshOnlineParityRoomStatus(session, infoLabel, output, false);
                    MessageBox.Show("Joined the online parity room and sent your manifest.", "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            };

            sendParityButton.Click += async delegate
            {
                if (!session.Joined)
                {
                    MessageBox.Show("Join a room first.", "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                await RunOnlineParityAction(form, sendParityButton, "send parity", delegate
                {
                    SendParityRoomPayload(session, true, false);
                }, delegate { RefreshOnlineParityRoomStatus(session, infoLabel, output, false); });
            };

            sendOosButton.Click += async delegate
            {
                if (!session.Joined)
                {
                    MessageBox.Show("Join a room first.", "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!TryConfirmParityRoomOosShare(session))
                    return;
                await RunOnlineParityAction(form, sendOosButton, "send OOS", delegate
                {
                    SendParityRoomPayload(session, false, true);
                }, delegate { RefreshOnlineParityRoomStatus(session, infoLabel, output, false); });
            };

            compareButton.Click += delegate { RefreshOnlineParityRoomStatus(session, infoLabel, output, true); };

            form.FormClosed += delegate { StopParityRoomHost(session); };
            form.ShowDialog(this);
        }

        private async Task RunOnlineParityAction(Form owner, Button button, string actionName, Action work, Action success)
        {
            try
            {
                button.Enabled = false;
                await Task.Run(work);
                if (!owner.IsDisposed && success != null)
                    success();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not " + actionName + ".\r\n\r\n" + ex.Message, "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("WARN Online parity room " + actionName + " failed: " + ex.Message);
            }
            finally
            {
                if (!owner.IsDisposed)
                    button.Enabled = true;
            }
        }

        private void RefreshOnlineParityRoomStatus(ParityRoomSession session, Label infoLabel, RichTextBox output, bool compare)
        {
            if (session == null)
                return;

            if (session.Hosting && session.Listener != null)
            {
                string host = GetOnlineParityAdvertisedAddress();
                infoLabel.Text = "Online direct room | Host/IP to share: " + host + " | Port: " + session.JoinPort + " | Code: " + session.RoomCode + " | Secret: " + session.SharedSecret;
            }
            else if (session.Joined)
            {
                infoLabel.Text = "Joined online room | Host: " + session.JoinHost + ":" + session.JoinPort + " | Code: " + session.RoomCode + " | Player: " + session.LocalPlayerLabel;
            }
            else
            {
                infoLabel.Text = "Create an online room or join one by host/IP. Do not use 127.0.0.1 for another player.";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(infoLabel.Text);
            sb.AppendLine();
            sb.AppendLine("Connection notes");
            sb.AppendLine("- Other players must connect to the host's reachable online address, not 127.0.0.1.");
            sb.AppendLine("- If the host is behind NAT/router, forward the shown TCP port to the host PC or use a VPN/mesh network that gives players a reachable address.");
            sb.AppendLine("- Windows Firewall must allow CK3MPS on that TCP port.");
            sb.AppendLine();

            List<ParityRoomPeer> peers;
            lock (session.Sync)
                peers = new List<ParityRoomPeer>(session.Peers);
            sb.AppendLine("Connected players: " + peers.Count);
            foreach (ParityRoomPeer peer in peers)
                sb.AppendLine("- " + NullText(peer.PlayerLabel) + " | endpoint=" + NullText(peer.Endpoint) + " | parity=" + (String.IsNullOrWhiteSpace(peer.ManifestText) ? "missing" : "received") + " | OOS=" + (String.IsNullOrWhiteSpace(peer.OosSummaryText) && String.IsNullOrWhiteSpace(peer.OosMetadataText) ? "missing" : "received"));

            if (compare)
            {
                string differencesText;
                string actionsText;
                List<ParityDifferenceRow> rows;
                bool safeToStart;
                BuildParityRoomComparisonTexts(session, out differencesText, out actionsText, out rows, out safeToStart);
                sb.AppendLine();
                sb.AppendLine(differencesText);
                sb.AppendLine();
                sb.AppendLine(actionsText);
            }

            output.Text = sb.ToString();
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
