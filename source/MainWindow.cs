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

            mainTabs.Location = new Point(16, 74);
            mainTabs.Size = new Size(924, 488);
            mainTabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            mainTabs.TabPages.Add(mainPage);
            mainTabs.TabPages.Add(pathsPage);
            mainTabs.TabPages.Add(logPage);
            mainTabs.TabPages.Add(reportsPage);
            mainTabs.TabPages.Add(restorePage);
            mainTabs.TabPages.Add(advancedPage);
            Controls.Add(mainTabs);

            BuildMainTab();
            BuildPathsTab();
            BuildLogTab();
            BuildReportsTab();
            BuildRestoreTab();
            BuildAdvancedTab();

            progress.Location = new Point(20, 574);
            progress.Size = new Size(920, 24);
            progress.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(progress);

            statusLabel.Location = new Point(20, 604);
            statusLabel.Size = new Size(920, 28);
            statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            statusLabel.Text = "Ready.";
            Controls.Add(statusLabel);

            stabilizeButton.Text = "Stabilize CK3";
            stabilizeButton.Location = new Point(20, 642);
            stabilizeButton.Size = new Size(150, 34);
            stabilizeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            stabilizeButton.Click += delegate { RunStabilize(); };
            Controls.Add(stabilizeButton);

            checkButton.Text = "Check only";
            checkButton.Location = new Point(184, 642);
            checkButton.Size = new Size(130, 34);
            checkButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            checkButton.Click += delegate { RunCheckOnly(); };
            Controls.Add(checkButton);

            openFolderButton.Text = "Open quarantine";
            openFolderButton.Location = new Point(328, 642);
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
        }

        private void BuildMainTab()
        {
            var presetLabel = new Label();
            presetLabel.Text = "Preset:";
            presetLabel.AutoSize = true;
            presetLabel.Location = new Point(12, 18);
            mainPage.Controls.Add(presetLabel);

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
            presetBox.Location = new Point(68, 14);
            presetBox.Size = new Size(180, 24);
            presetBox.SelectedIndexChanged += delegate
            {
                if (presetBox.SelectedItem != null)
                    ApplyPreset(presetBox.SelectedItem.ToString());
            };
            mainPage.Controls.Add(presetBox);

            selectAllButton.Text = "All";
            selectAllButton.Location = new Point(262, 12);
            selectAllButton.Size = new Size(58, 28);
            selectAllButton.Click += delegate
            {
                if (String.Equals(Convert.ToString(presetBox.SelectedItem), "Maximum", StringComparison.Ordinal))
                    ApplyPreset("Maximum");
                else
                    presetBox.SelectedItem = "Maximum";
            };
            mainPage.Controls.Add(selectAllButton);

            selectNoneButton.Text = "None";
            selectNoneButton.Location = new Point(328, 12);
            selectNoneButton.Size = new Size(70, 28);
            selectNoneButton.Click += delegate
            {
                SetAllSteps(false);
                presetBox.SelectedIndex = -1;
                statusLabel.Text = "No steps selected. Choose a preset or tick steps manually.";
            };
            mainPage.Controls.Add(selectNoneButton);

            previewButton.Text = "Preview";
            previewButton.Location = new Point(406, 12);
            previewButton.Size = new Size(84, 28);
            previewButton.Click += delegate { ShowStabilizationPreview(false); };
            mainPage.Controls.Add(previewButton);

            var graphicsLabel = new Label();
            graphicsLabel.Text = "Graphics:";
            graphicsLabel.AutoSize = true;
            graphicsLabel.Location = new Point(512, 18);
            mainPage.Controls.Add(graphicsLabel);

            graphicsProfileBox.DropDownStyle = ComboBoxStyle.DropDownList;
            graphicsProfileBox.Items.AddRange(new object[]
            {
                "Stability Low",
                "Balanced",
                "Quality",
                "Keep current"
            });
            graphicsProfileBox.Location = new Point(578, 14);
            graphicsProfileBox.Size = new Size(140, 24);
            mainPage.Controls.Add(graphicsProfileBox);

            liveLogLabel.Text = "Live log:";
            liveLogLabel.AutoSize = true;
            mainPage.Controls.Add(liveLogLabel);

            checklistPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            checklistPanel.BorderStyle = BorderStyle.FixedSingle;
            checklistPanel.TabStop = true;
            checklistPanel.MouseWheel += delegate(object sender, MouseEventArgs e) { ScrollChecklistWheel(e.Delta); };

            checklistContentPanel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            checklistPanel.Controls.Add(checklistContentPanel);

            checklistScrollBar.Width = SystemInformation.VerticalScrollBarWidth;
            checklistScrollBar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            checklistScrollBar.Scroll += delegate { UpdateChecklistScrollPosition(); };
            checklistPanel.Controls.Add(checklistScrollBar);

            checklistPanel.Resize += delegate
            {
                ResizeChecklistRows();
                LayoutChecklistViewport();
            };
            mainPage.Resize += delegate { LayoutMainTabControls(); };
            mainPage.Controls.Add(checklistPanel);

            ConfigureLogView(logBox);
            mainPage.Controls.Add(logBox);

            LayoutMainTabControls();
        }

        private void LayoutMainTabControls()
        {
            int leftMargin = 12;
            int top = 52;
            int bottomMargin = 12;
            int gap = 12;
            int checklistWidth = 446;
            int labelY = 56;
            int logTop = 78;
            int availableHeight = Math.Max(240, mainPage.ClientSize.Height - top - bottomMargin);

            checklistPanel.Location = new Point(leftMargin, top);
            checklistPanel.Size = new Size(checklistWidth, availableHeight);

            liveLogLabel.Location = new Point(checklistPanel.Right + gap, labelY);

            logBox.Location = new Point(checklistPanel.Right + gap, logTop);
            logBox.Size = new Size(Math.Max(260, mainPage.ClientSize.Width - logBox.Left - leftMargin), Math.Max(220, mainPage.ClientSize.Height - logTop - bottomMargin));
        }

        private void BuildPathsTab()
        {
            var gamePathLabel = new Label();
            gamePathLabel.Text = "Game folder:";
            gamePathLabel.AutoSize = true;
            gamePathLabel.Location = new Point(16, 28);
            pathsPage.Controls.Add(gamePathLabel);

            gamePathBox.Location = new Point(124, 24);
            gamePathBox.Size = new Size(630, 24);
            gamePathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            gamePathBox.ReadOnly = true;
            pathsPage.Controls.Add(gamePathBox);

            gamePathBrowseButton.Text = "Browse...";
            gamePathBrowseButton.Location = new Point(766, 22);
            gamePathBrowseButton.Size = new Size(84, 28);
            gamePathBrowseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            gamePathBrowseButton.Click += delegate { BrowseForGameFolder(); };
            pathsPage.Controls.Add(gamePathBrowseButton);

            gamePathStatusLabel.Location = new Point(858, 27);
            gamePathStatusLabel.Size = new Size(88, 20);
            gamePathStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pathsPage.Controls.Add(gamePathStatusLabel);

            var settingsPathLabel = new Label();
            settingsPathLabel.Text = "Settings/saves:";
            settingsPathLabel.AutoSize = true;
            settingsPathLabel.Location = new Point(16, 64);
            pathsPage.Controls.Add(settingsPathLabel);

            settingsPathBox.Location = new Point(124, 60);
            settingsPathBox.Size = new Size(630, 24);
            settingsPathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            settingsPathBox.ReadOnly = true;
            pathsPage.Controls.Add(settingsPathBox);

            settingsPathBrowseButton.Text = "Browse...";
            settingsPathBrowseButton.Location = new Point(766, 58);
            settingsPathBrowseButton.Size = new Size(84, 28);
            settingsPathBrowseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            settingsPathBrowseButton.Click += delegate { BrowseForSettingsFolder(); };
            pathsPage.Controls.Add(settingsPathBrowseButton);

            settingsPathStatusLabel.Location = new Point(858, 63);
            settingsPathStatusLabel.Size = new Size(88, 20);
            settingsPathStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pathsPage.Controls.Add(settingsPathStatusLabel);

            resetPathsButton.Text = "Auto-detect paths";
            resetPathsButton.Location = new Point(124, 100);
            resetPathsButton.Size = new Size(140, 32);
            resetPathsButton.Click += delegate { ResetPathsToAutoDetect(); };
            pathsPage.Controls.Add(resetPathsButton);

            openGamePathButton.Text = "Open game";
            openGamePathButton.Location = new Point(278, 100);
            openGamePathButton.Size = new Size(100, 32);
            openGamePathButton.Click += delegate { OpenPathIfExists(ck3Install); };
            pathsPage.Controls.Add(openGamePathButton);

            openSettingsPathButton.Text = "Open settings";
            openSettingsPathButton.Location = new Point(390, 100);
            openSettingsPathButton.Size = new Size(112, 32);
            openSettingsPathButton.Click += delegate { OpenPathIfExists(ck3Docs); };
            pathsPage.Controls.Add(openSettingsPathButton);

            resetGamePathButton.Text = "Reset game";
            resetGamePathButton.Location = new Point(514, 100);
            resetGamePathButton.Size = new Size(100, 32);
            resetGamePathButton.Click += delegate { ResetGamePathToAutoDetect(); };
            pathsPage.Controls.Add(resetGamePathButton);

            resetSettingsPathButton.Text = "Reset settings";
            resetSettingsPathButton.Location = new Point(626, 100);
            resetSettingsPathButton.Size = new Size(112, 32);
            resetSettingsPathButton.Click += delegate { ResetSettingsPathToDefault(); };
            pathsPage.Controls.Add(resetSettingsPathButton);

            var pathsHint = new Label();
            pathsHint.Text = "Game folder must contain binaries\\ck3.exe. Settings/saves should be the Crusader Kings III folder under Documents.";
            pathsHint.Location = new Point(124, 148);
            pathsHint.Size = new Size(720, 44);
            pathsHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            pathsPage.Controls.Add(pathsHint);

            pathDetailsLabel.Location = new Point(124, 198);
            pathDetailsLabel.Size = new Size(760, 120);
            pathDetailsLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            pathsPage.Controls.Add(pathDetailsLabel);
        }

        private void BuildLogTab()
        {
            ConfigureLogView(logTabBox);
            logTabBox.Location = new Point(8, 8);
            logTabBox.Size = new Size(896, 428);
            logTabBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            logPage.Controls.Add(logTabBox);
        }

        private static void ConfigureLogView(RichTextBox box)
        {
            box.Multiline = true;
            box.ScrollBars = RichTextBoxScrollBars.Both;
            box.WordWrap = false;
            box.ReadOnly = true;
            box.Font = new Font("Consolas", 9F);
            box.BackColor = Color.White;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.DetectUrls = false;
            box.HideSelection = false;
        }

        private void BuildReportsTab()
        {
            openReportsButton.Text = "Open reports";
            openReportsButton.Location = new Point(16, 18);
            openReportsButton.Size = new Size(130, 34);
            openReportsButton.Click += delegate { OpenReportsLocation(); };
            reportsPage.Controls.Add(openReportsButton);

            exportSupportButton.Text = "Export support package";
            exportSupportButton.Location = new Point(160, 18);
            exportSupportButton.Size = new Size(170, 34);
            exportSupportButton.Click += delegate { ExportSupportPackage(); };
            reportsPage.Controls.Add(exportSupportButton);

            refreshHistoryButton.Text = "Refresh history";
            refreshHistoryButton.Location = new Point(344, 18);
            refreshHistoryButton.Size = new Size(130, 34);
            refreshHistoryButton.Click += delegate { RefreshHistoryView(); };
            reportsPage.Controls.Add(refreshHistoryButton);

            historyBox.Location = new Point(16, 66);
            historyBox.Size = new Size(888, 370);
            historyBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            historyBox.Multiline = true;
            historyBox.ReadOnly = true;
            historyBox.ScrollBars = ScrollBars.Both;
            historyBox.WordWrap = false;
            historyBox.Font = new Font("Consolas", 9F);
            reportsPage.Controls.Add(historyBox);
        }

        private void BuildRestoreTab()
        {
            refreshRestoreButton.Text = "Refresh";
            refreshRestoreButton.Location = new Point(16, 18);
            refreshRestoreButton.Size = new Size(100, 34);
            refreshRestoreButton.Click += delegate { RefreshRestoreList(); };
            restorePage.Controls.Add(refreshRestoreButton);

            restoreSelectedButton.Text = "Restore selected";
            restoreSelectedButton.Location = new Point(130, 18);
            restoreSelectedButton.Size = new Size(140, 34);
            restoreSelectedButton.Click += delegate { RestoreSelectedItem(); };
            restorePage.Controls.Add(restoreSelectedButton);

            restoreDefaultButton.Text = "Restore default";
            restoreDefaultButton.Location = new Point(284, 18);
            restoreDefaultButton.Size = new Size(140, 34);
            restoreDefaultButton.Click += delegate { RestoreSelectedItemToDefault(); };
            restorePage.Controls.Add(restoreDefaultButton);

            openQuarantineButton.Text = "Open quarantine";
            openQuarantineButton.Location = new Point(438, 18);
            openQuarantineButton.Size = new Size(140, 34);
            openQuarantineButton.Click += delegate
            {
                string dir = GetKnownQuarantine();
                if (!String.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Process.Start("explorer.exe", dir);
            };
            restorePage.Controls.Add(openQuarantineButton);

            var restoreRunLabel = new Label();
            restoreRunLabel.Text = "Run:";
            restoreRunLabel.AutoSize = true;
            restoreRunLabel.Location = new Point(594, 27);
            restorePage.Controls.Add(restoreRunLabel);

            restoreRunBox.DropDownStyle = ComboBoxStyle.DropDownList;
            restoreRunBox.Location = new Point(632, 22);
            restoreRunBox.Size = new Size(252, 24);
            restoreRunBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            restoreRunBox.SelectedIndexChanged += delegate
            {
                if (!updatingRestoreUi)
                    RefreshRestoreListOnly();
            };
            restorePage.Controls.Add(restoreRunBox);

            restoreListBox.Location = new Point(16, 66);
            restoreListBox.Size = new Size(410, 370);
            restoreListBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            restoreListBox.SelectedIndexChanged += delegate { ShowSelectedRestoreDetails(); };
            restorePage.Controls.Add(restoreListBox);

            restoreDetailsBox.Location = new Point(442, 66);
            restoreDetailsBox.Size = new Size(462, 370);
            restoreDetailsBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            restoreDetailsBox.Multiline = true;
            restoreDetailsBox.ReadOnly = true;
            restoreDetailsBox.ScrollBars = ScrollBars.Both;
            restoreDetailsBox.WordWrap = false;
            restoreDetailsBox.Font = new Font("Consolas", 9F);
            restorePage.Controls.Add(restoreDetailsBox);
        }

        private void BuildAdvancedTab()
        {
            updateOnStartupBox.Text = "Check for updates on startup";
            updateOnStartupBox.Location = new Point(18, 22);
            updateOnStartupBox.Size = new Size(260, 24);
            updateOnStartupBox.CheckedChanged += delegate
            {
                updateCheckOnStartup = updateOnStartupBox.Checked;
                SaveAppConfig();
            };
            advancedPage.Controls.Add(updateOnStartupBox);

            portableModeBox.Text = "Portable mode";
            portableModeBox.Location = new Point(18, 56);
            portableModeBox.Size = new Size(180, 24);
            portableModeBox.CheckedChanged += delegate
            {
                portableMode = portableModeBox.Checked;
                SaveAppConfig();
            };
            advancedPage.Controls.Add(portableModeBox);

            var logVerbosityLabel = new Label();
            logVerbosityLabel.Text = "Log verbosity:";
            logVerbosityLabel.AutoSize = true;
            logVerbosityLabel.Location = new Point(18, 96);
            advancedPage.Controls.Add(logVerbosityLabel);

            logVerbosityBox.DropDownStyle = ComboBoxStyle.DropDownList;
            logVerbosityBox.Items.AddRange(new object[] { "Quiet", "Normal", "Verbose" });
            logVerbosityBox.Location = new Point(124, 92);
            logVerbosityBox.Size = new Size(130, 24);
            logVerbosityBox.SelectedIndexChanged += delegate
            {
                if (logVerbosityBox.SelectedItem != null)
                {
                    logVerbosity = Convert.ToString(logVerbosityBox.SelectedItem);
                    SaveAppConfig();
                }
            };
            advancedPage.Controls.Add(logVerbosityBox);

            updateButton.Text = "Check updates";
            updateButton.Location = new Point(18, 136);
            updateButton.Size = new Size(130, 34);
            updateButton.Click += delegate { CheckForUpdatesManual(); };
            advancedPage.Controls.Add(updateButton);

            updateDownloadProgress.Location = new Point(164, 142);
            updateDownloadProgress.Size = new Size(280, 22);
            updateDownloadProgress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            advancedPage.Controls.Add(updateDownloadProgress);
        }

        private void FillSteps()
        {
            steps.Items.Clear();
            steps.Items.Add("Create Windows restore point");
            steps.Items.Add("Check CK3 folders and running processes");
            steps.Items.Add("Create timestamped quarantine backup");
            steps.Items.Add("Flush DNS cache");
            steps.Items.Add("Diagnose adapters, routes, DNS, MTU and TCP/IP");
            steps.Items.Add("Add CK3 allow rules when elevated");
            steps.Items.Add("Apply game/network stability profile");
            steps.Items.Add("Tune power and adapter stability profile");
            steps.Items.Add("Check overlays, VPNs and competing background apps");
            steps.Items.Add("Check Paradox and Steam online reachability");
            steps.Items.Add("Back up Steam and Paradox Launcher settings");
            steps.Items.Add("Stabilize CK3 launch/cloud/overlay settings");
            steps.Items.Add("Rebuild CK3 launcher database");
            steps.Items.Add("Check runtime hygiene");
            steps.Items.Add("Force no-mod dlc_load.json");
            steps.Items.Add("Stabilize pdx_settings.txt");
            steps.Items.Add("Confirm launched profile");
            steps.Items.Add("Write stable new-campaign profile");
            steps.Items.Add("Clear player UI state");
            steps.Items.Add("Archive OOS and crash reports");
            steps.Items.Add("Clear CK3 and launcher caches");
            steps.Items.Add("Quarantine local .mod descriptors");
            steps.Items.Add("Inspect non-vanilla loader files");
            steps.Items.Add("Check active save and save-folder hygiene");
            steps.Items.Add("Remove nonessential files, keep saves");
            steps.Items.Add("Analyze latest OOS metadata");
            steps.Items.Add("Write support package index");
            steps.Items.Add("Write prevention rules");
            steps.Items.Add("Write player comparison manifest");
            progress.Maximum = steps.Items.Count;
            BuildChecklistGroups();
        }

        private void ValidateStepConfiguration()
        {
            if (steps.Items.Count != ExpectedStepCount)
                Log("WARN Step configuration mismatch: expected " + ExpectedStepCount + ", actual " + steps.Items.Count);

            if (steps.Items.Count > 0 && !steps.Items[0].ToString().StartsWith("Create Windows restore point", StringComparison.Ordinal))
                Log("WARN First checklist item is not the expected Safety block.");

            if (steps.Items.Count > 0 && !steps.Items[steps.Items.Count - 1].ToString().StartsWith("Write player comparison manifest", StringComparison.Ordinal))
                Log("WARN Last checklist item is not the expected MP parity block.");
        }

        private void RunStabilize()
        {
            SetBusy(true);
            ClearLogViews();
            progress.Value = 0;
            progress.Maximum = Math.Max(1, CountSelectedSteps());

            try
            {
                LogSection("Run started");
                Log("Preset: " + NullText(Convert.ToString(presetBox.SelectedItem)));
                Log("Selected steps: " + CountSelectedSteps());

                if (!ValidateBeforeRun())
                {
                    statusLabel.Text = "Stopped: fix folder paths before running Stabilize.";
                    AppendRunHistory("stabilize", "stopped_path_validation");
                    return;
                }

                if (CountSelectedSteps() == 0)
                {
                    statusLabel.Text = "No steps selected.";
                    Log("No steps selected. Choose a preset or tick steps manually.");
                    AppendRunHistory("stabilize", "stopped_no_steps");
                    return;
                }

                if (IsGameRunning())
                {
                    MessageBox.Show("Close CK3 and Paradox Launcher first. Steam may stay open.", "CK3 is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Log("Stopped: CK3 or Paradox Launcher is running.");
                    AppendRunHistory("stabilize", "stopped_game_running");
                    return;
                }

                if (!ConfirmStabilizationPreview())
                {
                    statusLabel.Text = "Stopped: preview was not confirmed.";
                    Log("INFO Stabilize stopped before changes: preview was not confirmed.");
                    AppendRunHistory("stabilize", "stopped_preview_not_confirmed");
                    return;
                }

                RunOptionalStep(0, "Safety: creating Windows restore point", CreateWindowsRestorePoint, false);
                RunOptionalStep(1, "Safety: checking paths", CheckBasePaths, true);
                RunOptionalStep(2, "Safety: creating quarantine", CreateQuarantine, true);
                RunOptionalStep(3, "Windows network: flushing DNS cache", FlushDnsCache, false);
                RunOptionalStep(4, "Windows network: diagnosing adapters and routes", RunNetworkDiagnostics, false);
                RunOptionalStep(5, "Windows firewall: adding CK3 rules", EnsureFirewallRules, false);
                RunOptionalStep(6, "Windows registry: applying game/network profile", ApplyWindowsGameNetworkProfile, false);
                RunOptionalStep(7, "Windows adapters: tuning power profile", ApplyPowerAdapterProfile, false);
                RunOptionalStep(8, "Windows apps: checking overlays and VPNs", CheckOverlaysAndVpn, false);
                RunOptionalStep(9, "Windows network: checking online services", CheckOnlineServices, false);
                RunOptionalStep(10, "Launchers: backing up settings", BackupSteamAndLauncherSettings, false);
                RunOptionalStep(11, "Steam: stabilizing CK3 settings", StabilizeSteamSettings, false);
                RunOptionalStep(12, "Paradox Launcher: rebuilding database", RebuildParadoxLauncherDatabase, false);
                RunOptionalStep(13, "Launchers: checking runtime hygiene", CheckLauncherRuntimeHygiene, false);
                RunOptionalStep(14, "CK3 external profile: writing no-mod profile", ForceNoMods, false);
                RunOptionalStep(15, "CK3 external settings: stabilizing settings", StabilizePdxSettings, false);
                RunOptionalStep(16, "CK3 runtime verification: writing launch report", WriteRuntimeVerificationReport, false);
                RunOptionalStep(17, "CK3 in-game rules: writing game-rule profile", WriteStableGameRuleProfile, false);
                RunOptionalStep(18, "CK3 user state: clearing player UI state", ClearPlayerState, false);
                RunOptionalStep(19, "CK3 reports: archiving OOS and crashes", ArchiveReports, false);
                RunOptionalStep(20, "CK3 cache: clearing CK3 and launcher caches", ClearCaches, false);
                RunOptionalStep(21, "CK3 mods: quarantining .mod descriptors", QuarantineModDescriptors, false);
                RunOptionalStep(22, "CK3 binaries: inspecting non-vanilla files", QuarantineLoaderFiles, false);
                RunOptionalStep(23, "CK3 saves: stabilizing save launch hygiene", StabilizeSaveHygiene, false);
                RunOptionalStep(24, "CK3 folder cleanup: removing nonessential files", CleanCk3DocumentsFolder, false);
                RunOptionalStep(25, "OOS reports: analyzing latest metadata", AnalyzeLatestOosReport, false);
                RunOptionalStep(26, "OOS evidence: writing support package index", WriteOosEvidencePack, false);
                RunOptionalStep(27, "OOS protocol: writing prevention rules", WriteOosPreventionProtocol, false);
                RunOptionalStep(28, "MP parity: writing comparison manifest", WriteMultiplayerParityManifest, false);
                if (IsStepChecked(14) || IsStepChecked(15) || IsStepChecked(16))
                    StartSettingsGuard();
                LogSection("Final readiness summary");
                RunReadinessChecks(true);
                LogSection("Automatic report");
                WriteStabilityReport();

                if (lastReadinessFailures == 0)
                {
                    statusLabel.Text = "Done. CK3 profile is prepared for stable vanilla multiplayer.";
                    Log("Done. Use host local save, no hotjoin, speed 1-2 after load.");
                    AppendRunHistory("stabilize", "ready");
                }
                else
                {
                    statusLabel.Text = "Completed with blockers. Fix failed readiness checks before serious MP.";
                    Log("RESULT Completed with blockers. Fix failed readiness checks before serious MP.");
                    AppendRunHistory("stabilize", "completed_with_blockers");
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Failed: " + ex.Message;
                Log("ERROR: " + ex);
                AppendRunHistory("stabilize", "failed");
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
            ClearLogViews();
            progress.Value = 0;
            progress.Maximum = steps.Items.Count;
            try
            {
                LogSection("Check only started");
                Log("Mode: read-only scan of every checklist item. No files or settings will be changed.");
                ShowStabilizationPreview(true);
                if (!ValidateBeforeRun())
                {
                    statusLabel.Text = "Check stopped: fix folder paths first.";
                    AppendRunHistory("check_only", "stopped_path_validation");
                    return;
                }

                for (int i = 0; i < steps.Items.Count; i++)
                    RunCheckStep(i);

                LogSection("Final readiness summary");
                RunReadinessChecks(false);
                WriteCheckOnlyReport();
                statusLabel.Text = "Check complete. Every checklist item was checked in read-only mode.";
                AppendRunHistory("check_only", lastReadinessFailures == 0 ? "ready" : "completed_with_blockers");
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
                AppendRunHistory("check_only", "failed");
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



