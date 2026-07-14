using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private void BuildUi()
        {
            var title = new Label();
            title.Text = "CK3MPS";
            title.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(16, 14);
            Controls.Add(title);

            var subtitle = new Label();
            subtitle.Text = "Safely prepares a clean vanilla CK3 multiplayer profile. Files are moved to quarantine, not deleted.";
            subtitle.AutoSize = false;
            subtitle.AutoEllipsis = true;
            subtitle.Size = new Size(610, 20);
            subtitle.Location = new Point(18, 48);
            Controls.Add(subtitle);

            var presetLabel = new Label();
            presetLabel.Text = "Preset:";
            presetLabel.AutoSize = true;
            presetLabel.Location = new Point(20, 78);
            Controls.Add(presetLabel);

            presetBox.DropDownStyle = ComboBoxStyle.DropDownList;
            presetBox.Items.AddRange(new object[]
            {
                "Minimum",
                "Recommended",
                "Maximum",
                "Clean profile only",
                "Network only",
                "Diagnostic only"
            });
            presetBox.Location = new Point(76, 74);
            presetBox.Size = new Size(180, 24);
            presetBox.SelectedIndexChanged += delegate
            {
                if (presetBox.SelectedItem != null)
                    ApplyPreset(presetBox.SelectedItem.ToString());
            };
            Controls.Add(presetBox);

            selectAllButton.Text = "All";
            selectAllButton.Location = new Point(270, 72);
            selectAllButton.Size = new Size(58, 28);
            selectAllButton.Click += delegate
            {
                if (String.Equals(Convert.ToString(presetBox.SelectedItem), "Maximum", StringComparison.Ordinal))
                    ApplyPreset("Maximum");
                else
                    presetBox.SelectedItem = "Maximum";
            };
            Controls.Add(selectAllButton);

            selectNoneButton.Text = "None";
            selectNoneButton.Location = new Point(336, 72);
            selectNoneButton.Size = new Size(70, 28);
            selectNoneButton.Click += delegate
            {
                SetAllSteps(false);
                presetBox.SelectedIndex = -1;
                statusLabel.Text = "No steps selected. Choose a preset or tick steps manually.";
            };
            Controls.Add(selectNoneButton);

            var graphicsLabel = new Label();
            graphicsLabel.Text = "Graphics:";
            graphicsLabel.AutoSize = true;
            graphicsLabel.Location = new Point(420, 78);
            Controls.Add(graphicsLabel);

            graphicsProfileBox.DropDownStyle = ComboBoxStyle.DropDownList;
            graphicsProfileBox.Items.AddRange(new object[]
            {
                "Stability Low",
                "Balanced",
                "Quality",
                "Keep current"
            });
            graphicsProfileBox.Location = new Point(486, 74);
            graphicsProfileBox.Size = new Size(140, 24);
            Controls.Add(graphicsProfileBox);

            gamePathStatusLabel.Location = new Point(650, 18);
            gamePathStatusLabel.Size = new Size(290, 20);
            gamePathStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            gamePathStatusLabel.AutoEllipsis = true;
            Controls.Add(gamePathStatusLabel);

            settingsPathStatusLabel.Location = new Point(650, 44);
            settingsPathStatusLabel.Size = new Size(290, 20);
            settingsPathStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            settingsPathStatusLabel.AutoEllipsis = true;
            Controls.Add(settingsPathStatusLabel);

            steps.CheckOnClick = true;
            steps.Location = new Point(20, 108);
            steps.Size = new Size(430, 416);
            steps.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(steps);

            logBox.Location = new Point(470, 108);
            logBox.Size = new Size(470, 416);
            logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            logBox.Multiline = true;
            logBox.ScrollBars = RichTextBoxScrollBars.Both;
            logBox.WordWrap = false;
            logBox.ReadOnly = true;
            logBox.Font = new Font("Consolas", 9F);
            logBox.BackColor = Color.White;
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.DetectUrls = false;
            Controls.Add(logBox);

            progress.Location = new Point(20, 540);
            progress.Size = new Size(920, 24);
            progress.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(progress);

            statusLabel.Location = new Point(20, 574);
            statusLabel.Size = new Size(920, 28);
            statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            statusLabel.Text = "Ready.";
            Controls.Add(statusLabel);

            stabilizeButton.Text = "Stabilize CK3";
            stabilizeButton.Location = new Point(20, 615);
            stabilizeButton.Size = new Size(150, 34);
            stabilizeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            stabilizeButton.Click += delegate { RunStabilize(); };
            Controls.Add(stabilizeButton);

            checkButton.Text = "Check only";
            checkButton.Location = new Point(184, 615);
            checkButton.Size = new Size(130, 34);
            checkButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            checkButton.Click += delegate { RunCheckOnly(); };
            Controls.Add(checkButton);

            openFolderButton.Text = "Open quarantine";
            openFolderButton.Location = new Point(328, 615);
            openFolderButton.Size = new Size(150, 34);
            openFolderButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            openFolderButton.Click += delegate
            {
                if (!String.IsNullOrEmpty(lastQuarantine) && Directory.Exists(lastQuarantine))
                    Process.Start("explorer.exe", lastQuarantine);
                else if (Directory.Exists(ck3Docs))
                    Process.Start("explorer.exe", ck3Docs);
            };
            Controls.Add(openFolderButton);

            openReportsButton.Text = "Open reports";
            openReportsButton.Location = new Point(492, 615);
            openReportsButton.Size = new Size(130, 34);
            openReportsButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            openReportsButton.Click += delegate
            {
                OpenReportsLocation();
            };
            Controls.Add(openReportsButton);

            updateButton.Text = "Check updates";
            updateButton.Location = new Point(636, 615);
            updateButton.Size = new Size(130, 34);
            updateButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            updateButton.Click += delegate
            {
                CheckForUpdatesManual();
            };
            Controls.Add(updateButton);
        }

        private void FillSteps()
        {
            steps.Items.Clear();
            steps.Items.Add("1. Safety: check CK3 folders and running processes");
            steps.Items.Add("2. Safety: create timestamped quarantine backup");
            steps.Items.Add("3. Windows network: flush DNS cache");
            steps.Items.Add("4. Windows network: diagnose adapters, routes, DNS, MTU and TCP/IP");
            steps.Items.Add("5. Windows firewall: add CK3 allow rules when elevated");
            steps.Items.Add("6. Windows registry: apply game/network stability profile");
            steps.Items.Add("7. Windows adapters: tune power and adapter stability profile");
            steps.Items.Add("8. Windows apps: check overlays, VPNs and competing background apps");
            steps.Items.Add("9. Windows network: check Paradox and Steam online reachability");
            steps.Items.Add("10. Launchers: back up Steam and Paradox Launcher settings");
            steps.Items.Add("11. Steam: stabilize CK3 launch/cloud/overlay settings");
            steps.Items.Add("12. Paradox Launcher: rebuild CK3 database");
            steps.Items.Add("13. Launchers: check runtime hygiene");
            steps.Items.Add("14. CK3 external profile: force no-mod dlc_load.json");
            steps.Items.Add("15. CK3 external settings: stabilize pdx_settings.txt");
            steps.Items.Add("16. CK3 runtime verification: confirm launched profile");
            steps.Items.Add("17. CK3 in-game rules: write stable new-campaign profile");
            steps.Items.Add("18. CK3 user state: clear player UI state");
            steps.Items.Add("19. CK3 reports: archive OOS and crash reports");
            steps.Items.Add("20. CK3 cache: clear CK3 and launcher caches");
            steps.Items.Add("21. CK3 mods: quarantine local .mod descriptors");
            steps.Items.Add("22. CK3 binaries: inspect non-vanilla loader files");
            steps.Items.Add("23. CK3 saves: check active save and save-folder hygiene");
            steps.Items.Add("24. CK3 folder cleanup: remove nonessential files, keep saves");
            steps.Items.Add("25. OOS reports: analyze latest OOS metadata");
            steps.Items.Add("26. OOS evidence: write support package index");
            steps.Items.Add("27. OOS protocol: write prevention rules");
            steps.Items.Add("28. MP parity: write player comparison manifest");
            progress.Maximum = steps.Items.Count;
        }

        private void ValidateStepConfiguration()
        {
            if (steps.Items.Count != ExpectedStepCount)
                Log("WARN Step configuration mismatch: expected " + ExpectedStepCount + ", actual " + steps.Items.Count);

            if (steps.Items.Count > 0 && !steps.Items[0].ToString().StartsWith("1. Safety:", StringComparison.Ordinal))
                Log("WARN First checklist item is not the expected Safety block.");

            if (steps.Items.Count > 0 && !steps.Items[steps.Items.Count - 1].ToString().StartsWith(ExpectedStepCount + ". MP parity:", StringComparison.Ordinal))
                Log("WARN Last checklist item is not the expected MP parity block.");
        }

        private void RunStabilize()
        {
            SetBusy(true);
            logBox.Clear();
            progress.Value = 0;
            progress.Maximum = Math.Max(1, CountSelectedSteps());

            try
            {
                LogSection("Run started");
                Log("Preset: " + NullText(Convert.ToString(presetBox.SelectedItem)));
                Log("Selected steps: " + CountSelectedSteps());

                if (CountSelectedSteps() == 0)
                {
                    statusLabel.Text = "No steps selected.";
                    Log("No steps selected. Choose a preset or tick steps manually.");
                    return;
                }

                if (IsGameRunning())
                {
                    MessageBox.Show("Close CK3 and Paradox Launcher first. Steam may stay open.", "CK3 is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Log("Stopped: CK3 or Paradox Launcher is running.");
                    return;
                }

                RunOptionalStep(0, "Safety: checking paths", CheckBasePaths, true);
                RunOptionalStep(1, "Safety: creating quarantine", CreateQuarantine, true);
                RunOptionalStep(2, "Windows network: flushing DNS cache", FlushDnsCache, false);
                RunOptionalStep(3, "Windows network: diagnosing adapters and routes", RunNetworkDiagnostics, false);
                RunOptionalStep(4, "Windows firewall: adding CK3 rules", EnsureFirewallRules, false);
                RunOptionalStep(5, "Windows registry: applying game/network profile", ApplyWindowsGameNetworkProfile, false);
                RunOptionalStep(6, "Windows adapters: tuning power profile", ApplyPowerAdapterProfile, false);
                RunOptionalStep(7, "Windows apps: checking overlays and VPNs", CheckOverlaysAndVpn, false);
                RunOptionalStep(8, "Windows network: checking online services", CheckOnlineServices, false);
                RunOptionalStep(9, "Launchers: backing up settings", BackupSteamAndLauncherSettings, false);
                RunOptionalStep(10, "Steam: stabilizing CK3 settings", StabilizeSteamSettings, false);
                RunOptionalStep(11, "Paradox Launcher: rebuilding database", RebuildParadoxLauncherDatabase, false);
                RunOptionalStep(12, "Launchers: checking runtime hygiene", CheckLauncherRuntimeHygiene, false);
                RunOptionalStep(13, "CK3 external profile: writing no-mod profile", ForceNoMods, false);
                RunOptionalStep(14, "CK3 external settings: stabilizing settings", StabilizePdxSettings, false);
                RunOptionalStep(15, "CK3 runtime verification: writing launch report", WriteRuntimeVerificationReport, false);
                RunOptionalStep(16, "CK3 in-game rules: writing game-rule profile", WriteStableGameRuleProfile, false);
                RunOptionalStep(17, "CK3 user state: clearing player UI state", ClearPlayerState, false);
                RunOptionalStep(18, "CK3 reports: archiving OOS and crashes", ArchiveReports, false);
                RunOptionalStep(19, "CK3 cache: clearing CK3 and launcher caches", ClearCaches, false);
                RunOptionalStep(20, "CK3 mods: quarantining .mod descriptors", QuarantineModDescriptors, false);
                RunOptionalStep(21, "CK3 binaries: inspecting non-vanilla files", QuarantineLoaderFiles, false);
                RunOptionalStep(22, "CK3 saves: stabilizing save launch hygiene", StabilizeSaveHygiene, false);
                RunOptionalStep(23, "CK3 folder cleanup: removing nonessential files", CleanCk3DocumentsFolder, false);
                RunOptionalStep(24, "OOS reports: analyzing latest metadata", AnalyzeLatestOosReport, false);
                RunOptionalStep(25, "OOS evidence: writing support package index", WriteOosEvidencePack, false);
                RunOptionalStep(26, "OOS protocol: writing prevention rules", WriteOosPreventionProtocol, false);
                RunOptionalStep(27, "MP parity: writing comparison manifest", WriteMultiplayerParityManifest, false);
                if (IsStepChecked(13) || IsStepChecked(14) || IsStepChecked(15))
                    StartSettingsGuard();
                LogSection("Final readiness summary");
                RunReadinessChecks(true);
                LogSection("Automatic report");
                WriteStabilityReport();

                if (lastReadinessFailures == 0)
                {
                    statusLabel.Text = "Done. CK3 profile is prepared for stable vanilla multiplayer.";
                    Log("Done. Use host local save, no hotjoin, speed 1-2 after load.");
                }
                else
                {
                    statusLabel.Text = "Completed with blockers. Fix failed readiness checks before serious MP.";
                    Log("RESULT Completed with blockers. Fix failed readiness checks before serious MP.");
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Failed: " + ex.Message;
                Log("ERROR: " + ex);
                MessageBox.Show(ex.Message, "CK3MPS", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RunCheckOnly()
        {
            SetBusy(true);
            logBox.Clear();
            progress.Value = 0;
            progress.Maximum = steps.Items.Count;
            try
            {
                LogSection("Check only started");
                Log("Mode: read-only scan of every checklist item. No files or settings will be changed.");
                for (int i = 0; i < steps.Items.Count; i++)
                    RunCheckStep(i);

                LogSection("Final readiness summary");
                RunReadinessChecks(false);
                WriteCheckOnlyReport();
                statusLabel.Text = "Check complete. Every checklist item was checked in read-only mode.";
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RunStep(int index, string label, Action action)
        {
            statusLabel.Text = label + "...";
            LogSection(label);
            Application.DoEvents();
            action();
            progress.Value = Math.Min(progress.Maximum, progress.Value + 1);
            Application.DoEvents();
        }

    }
}



