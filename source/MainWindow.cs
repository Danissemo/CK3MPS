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
using System.Threading.Tasks;
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
            mainTabs.TabPages.Add(workflowPage);
            mainTabs.TabPages.Add(reportsPage);
            mainTabs.TabPages.Add(restorePage);
            mainTabs.TabPages.Add(advancedPage);
            Controls.Add(mainTabs);

            BuildMainTab();
            BuildPathsTab();
            BuildWorkflowTab();
            BuildReportsTab();
            BuildRestoreTab();
            BuildAdvancedTab();

            statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            statusLabel.Text = "Ready.";
            Controls.Add(statusLabel);

            Resize += delegate { LayoutRootControls(); };
            LayoutRootControls();

            stabilizeButton.Text = "Apply Settings";
            stabilizeButton.Size = new Size(150, 34);
            stabilizeButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            stabilizeButton.Click += delegate { RunStabilize(); };
            mainPage.Controls.Add(stabilizeButton);

            checkButton.Text = "Scan";
            checkButton.Size = new Size(130, 34);
            checkButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            checkButton.Click += delegate { RunCheckOnly(); };
            mainPage.Controls.Add(checkButton);

            exportScanReportButton.Text = "Export Scan Report";
            exportScanReportButton.Size = new Size(150, 34);
            exportScanReportButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            exportScanReportButton.Enabled = false;
            exportScanReportButton.Click += delegate { ExportLastScanReport(); };
            mainPage.Controls.Add(exportScanReportButton);

            mainPage.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!stabilizeButton.Enabled && stabilizeButton.Bounds.Contains(e.Location))
                    ShowApplyButtonHint();
                else
                    HideApplyButtonHint();
            };
            mainPage.MouseLeave += delegate { HideApplyButtonHint(); };
            UpdateApplyButtonState();
        }

        private void LayoutRootControls()
        {
            int width = Math.Max(760, ClientSize.Width - 32);
            int bottomMargin = 14;
            int statusHeight = 24;
            statusLabel.Location = new Point(16, ClientSize.Height - bottomMargin - statusHeight);
            statusLabel.Size = new Size(width, statusHeight);

            int height = Math.Max(420, statusLabel.Top - mainTabs.Top - 14);
            mainTabs.Size = new Size(width, height);
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

            previewButton.Text = "Review";
            previewButton.Size = new Size(110, 34);
            previewButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            previewButton.Click += delegate { RunReview(); };
            mainPage.Controls.Add(previewButton);

            graphicsSectionLabel.Text = "In-game graphics profile";
            graphicsSectionLabel.AutoSize = true;
            graphicsSectionLabel.Font = new Font(Font, FontStyle.Bold);
            mainPage.Controls.Add(graphicsSectionLabel);

            graphicsHintLabel.Text = "Changes CK3 graphics settings in `pdx_settings.txt`. Choose a safer profile for multiplayer stability or keep the current game graphics.";
            graphicsHintLabel.AutoSize = false;
            graphicsHintLabel.ForeColor = Color.FromArgb(90, 90, 90);
            mainPage.Controls.Add(graphicsHintLabel);

            graphicsProfileBox.DropDownStyle = ComboBoxStyle.DropDownList;
            graphicsProfileBox.Items.AddRange(new object[]
            {
                "Stability Low",
                "Balanced",
                "Quality",
                "Keep current"
            });
            graphicsProfileBox.Size = new Size(200, 24);
            graphicsProfileBox.SelectedIndexChanged += delegate
            {
                InvalidatePlanningSnapshot();
                UpdateApplyButtonState();
            };
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

            progress.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            mainPage.Controls.Add(progress);

            LayoutMainTabControls();
        }

        private void LayoutMainTabControls()
        {
            int leftMargin = 12;
            int top = 52;
            int bottomMargin = 12;
            int gap = 12;
            int checklistWidth = 446;
            int rightColumnLeft = checklistWidth + leftMargin + gap;
            int rightColumnWidth = Math.Max(260, mainPage.ClientSize.Width - rightColumnLeft - leftMargin);
            int graphicsTop = top;
            int graphicsWidth = checklistWidth;
            int graphicsLabelTop = graphicsTop;
            int graphicsComboTop = graphicsLabelTop + 22;
            int graphicsHintTop = graphicsComboTop + 30;
            int graphicsHintHeight = 36;
            int checklistTop = graphicsHintTop + graphicsHintHeight + 14;
            int logLabelTop = 16;
            int logTop = 34;
            int actionButtonTop = mainPage.ClientSize.Height - 46;
            int progressTop = actionButtonTop - 34;
            int checklistHeight = Math.Max(150, progressTop - checklistTop - bottomMargin);
            int logHeight = Math.Max(170, progressTop - logTop - 4);

            graphicsSectionLabel.Location = new Point(leftMargin, graphicsLabelTop);
            graphicsProfileBox.Location = new Point(leftMargin, graphicsComboTop);
            graphicsProfileBox.Size = new Size(Math.Min(220, graphicsWidth), 24);
            graphicsHintLabel.Location = new Point(leftMargin, graphicsHintTop);
            graphicsHintLabel.Size = new Size(graphicsWidth, graphicsHintHeight);

            checklistPanel.Location = new Point(leftMargin, checklistTop);
            checklistPanel.Size = new Size(checklistWidth, checklistHeight);

            liveLogLabel.Location = new Point(rightColumnLeft, logLabelTop);

            logBox.Location = new Point(rightColumnLeft, logTop);
            logBox.Size = new Size(rightColumnWidth, logHeight);

            progress.Location = new Point(leftMargin, progressTop);
            progress.Size = new Size(mainPage.ClientSize.Width - (leftMargin * 2), 22);

            checkButton.Location = new Point(leftMargin, actionButtonTop);
            exportScanReportButton.Location = new Point(checkButton.Right + gap, actionButtonTop);
            previewButton.Location = new Point(exportScanReportButton.Right + gap, actionButtonTop);
            stabilizeButton.Location = new Point(previewButton.Right + gap, actionButtonTop);
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
            gamePathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            gamePathBox.ReadOnly = true;
            pathsPage.Controls.Add(gamePathBox);

            gamePathBrowseButton.Text = "Browse...";
            gamePathBrowseButton.Location = new Point(766, 22);
            gamePathBrowseButton.Size = new Size(84, 28);
            gamePathBrowseButton.Click += delegate { BrowseForGameFolder(); };
            pathsPage.Controls.Add(gamePathBrowseButton);

            gamePathStatusLabel.Location = new Point(858, 27);
            gamePathStatusLabel.Size = new Size(88, 20);
            pathsPage.Controls.Add(gamePathStatusLabel);

            var settingsPathLabel = new Label();
            settingsPathLabel.Text = "Settings/saves:";
            settingsPathLabel.AutoSize = true;
            settingsPathLabel.Location = new Point(16, 64);
            pathsPage.Controls.Add(settingsPathLabel);

            settingsPathBox.Location = new Point(124, 60);
            settingsPathBox.Size = new Size(630, 24);
            settingsPathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            settingsPathBox.ReadOnly = true;
            pathsPage.Controls.Add(settingsPathBox);

            settingsPathBrowseButton.Text = "Browse...";
            settingsPathBrowseButton.Location = new Point(766, 58);
            settingsPathBrowseButton.Size = new Size(84, 28);
            settingsPathBrowseButton.Click += delegate { BrowseForSettingsFolder(); };
            pathsPage.Controls.Add(settingsPathBrowseButton);

            settingsPathStatusLabel.Location = new Point(858, 63);
            settingsPathStatusLabel.Size = new Size(88, 20);
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

            var pathsHint = new Label();
            pathsHint.Text = "Game folder must contain binaries\\ck3.exe. Settings/saves should be the Crusader Kings III folder under Documents.";
            pathsHint.Location = new Point(124, 148);
            pathsHint.Size = new Size(720, 44);
            pathsHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            pathsPage.Controls.Add(pathsHint);

            pathDetailsLabel.Location = new Point(124, 198);
            pathDetailsLabel.Size = new Size(760, 120);
            pathsPage.Controls.Add(pathDetailsLabel);

            pathsPage.Resize += delegate { LayoutPathsTabControls(); };
            LayoutPathsTabControls();
        }

        private void LayoutPathsTabControls()
        {
            const int leftMargin = 124;
            const int topGame = 24;
            const int topSettings = 60;
            const int buttonWidth = 84;
            const int statusWidth = 88;
            const int gap = 8;
            const int rightMargin = 18;

            int statusLeft = Math.Max(640, pathsPage.ClientSize.Width - rightMargin - statusWidth);
            int browseLeft = Math.Max(leftMargin + 220, statusLeft - gap - buttonWidth);
            int textWidth = Math.Max(280, browseLeft - leftMargin - gap);

            gamePathBox.Location = new Point(leftMargin, topGame);
            gamePathBox.Size = new Size(textWidth, 24);
            gamePathBrowseButton.Location = new Point(browseLeft, 22);
            gamePathStatusLabel.Location = new Point(statusLeft, 27);

            settingsPathBox.Location = new Point(leftMargin, topSettings);
            settingsPathBox.Size = new Size(textWidth, 24);
            settingsPathBrowseButton.Location = new Point(browseLeft, 58);
            settingsPathStatusLabel.Location = new Point(statusLeft, 63);

            pathDetailsLabel.Size = new Size(Math.Max(520, pathsPage.ClientSize.Width - leftMargin - rightMargin), 120);
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

            clearReportsButton.Text = "Clear reports";
            clearReportsButton.Location = new Point(488, 18);
            clearReportsButton.Size = new Size(130, 34);
            clearReportsButton.Click += delegate { ClearAllReports(); };
            reportsPage.Controls.Add(clearReportsButton);

            ConfigureLogView(historyBox);
            reportsPage.Controls.Add(historyBox);

            reportsPage.Resize += delegate { LayoutReportsTabControls(); };
            LayoutReportsTabControls();
        }

        private void LayoutReportsTabControls()
        {
            int left = 16;
            int top = 18;
            int gap = 14;
            int buttonHeight = 34;
            int rightPadding = 16;
            int bottomPadding = 16;

            openReportsButton.Location = new Point(left, top);
            openReportsButton.Size = new Size(128, buttonHeight);

            exportSupportButton.Location = new Point(openReportsButton.Right + gap, top);
            exportSupportButton.Size = new Size(168, buttonHeight);

            refreshHistoryButton.Location = new Point(exportSupportButton.Right + gap, top);
            refreshHistoryButton.Size = new Size(138, buttonHeight);

            clearReportsButton.Location = new Point(refreshHistoryButton.Right + gap, top);
            clearReportsButton.Size = new Size(128, buttonHeight);

            historyBox.Location = new Point(left, top + buttonHeight + 14);
            historyBox.Size = new Size(Math.Max(320, reportsPage.ClientSize.Width - left - rightPadding), Math.Max(220, reportsPage.ClientSize.Height - historyBox.Top - bottomPadding));
            historyBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        }

        private void BuildRestoreTab()
        {
            refreshRestoreButton.Text = "Refresh";
            refreshRestoreButton.Click += delegate { RefreshRestoreList(); };
            restorePage.Controls.Add(refreshRestoreButton);

            restoreSelectedButton.Text = "Restore selected";
            restoreSelectedButton.Click += delegate { RestoreSelectedItem(); };
            restorePage.Controls.Add(restoreSelectedButton);

            restoreDefaultButton.Text = "Restore default";
            restoreDefaultButton.Click += delegate { RestoreSelectedItemToDefault(); };
            restorePage.Controls.Add(restoreDefaultButton);

            deleteRestoreButton.Text = "Delete selected";
            deleteRestoreButton.Click += delegate { DeleteSelectedRestoreEntries(); };
            restorePage.Controls.Add(deleteRestoreButton);

            openQuarantineButton.Text = "Open quarantine";
            openQuarantineButton.Click += delegate
            {
                string dir = GetKnownQuarantine();
                if (!String.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Process.Start("explorer.exe", dir);
            };
            restorePage.Controls.Add(openQuarantineButton);

            restoreRunLabel.Text = "Run:";
            restoreRunLabel.AutoSize = true;
            restorePage.Controls.Add(restoreRunLabel);

            restoreRunBox.DropDownStyle = ComboBoxStyle.DropDownList;
            restoreRunBox.SelectedIndexChanged += delegate
            {
                if (!updatingRestoreUi)
                    RefreshRestoreListOnly();
            };
            restorePage.Controls.Add(restoreRunBox);

            restoreSortLabel.Text = "Sort:";
            restoreSortLabel.AutoSize = true;
            restorePage.Controls.Add(restoreSortLabel);

            restoreSortBox.DropDownStyle = ComboBoxStyle.DropDownList;
            restoreSortBox.Items.AddRange(new object[] { "Created", "Run", "Status", "Type", "Description", "Original path", "Backup path" });
            restoreSortBox.SelectedIndexChanged += delegate
            {
                if (!updatingRestoreUi)
                    RefreshRestoreListOnly();
            };
            restorePage.Controls.Add(restoreSortBox);

            restoreSortDirectionBox.DropDownStyle = ComboBoxStyle.DropDownList;
            restoreSortDirectionBox.Items.AddRange(new object[] { "Newest first", "Oldest first" });
            restoreSortDirectionBox.SelectedIndexChanged += delegate
            {
                if (!updatingRestoreUi)
                    RefreshRestoreListOnly();
            };
            restorePage.Controls.Add(restoreSortDirectionBox);

            restoreSelectAllBox.Text = "Select all visible";
            restoreSelectAllBox.AutoSize = true;
            restoreSelectAllBox.CheckedChanged += delegate
            {
                if (updatingRestoreSelectionUi)
                    return;
                SetAllVisibleRestoreEntriesChecked(restoreSelectAllBox.Checked);
            };
            restorePage.Controls.Add(restoreSelectAllBox);

            restoreListBox.HorizontalScrollbar = true;
            restoreListBox.CheckOnClick = true;
            restoreListBox.IntegralHeight = false;
            restoreListBox.SelectedIndexChanged += delegate { ShowSelectedRestoreDetails(); };
            restoreListBox.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                BeginInvoke((MethodInvoker)delegate { SyncCheckedRestoreEntryIds(); UpdateRestoreSelectAllState(); ShowSelectedRestoreDetails(); });
            };
            restorePage.Controls.Add(restoreListBox);

            restoreDetailsBox.Multiline = true;
            restoreDetailsBox.ReadOnly = true;
            restoreDetailsBox.ScrollBars = ScrollBars.Both;
            restoreDetailsBox.WordWrap = false;
            restoreDetailsBox.Font = new Font("Consolas", 9F);
            restorePage.Controls.Add(restoreDetailsBox);

            restorePage.Resize += delegate { LayoutRestoreTabControls(); };
            LayoutRestoreTabControls();
        }

        private void LayoutRestoreTabControls()
        {
            int left = 16;
            int top = 18;
            int buttonHeight = 34;
            int gap = 10;
            int smallGap = 8;
            int rightPadding = 16;
            int detailsMinWidth = 360;
            int filterTop = top + buttonHeight + 12;
            int listTop = filterTop + 58;
            int bottomPadding = 16;

            refreshRestoreButton.Location = new Point(left, top);
            refreshRestoreButton.Size = new Size(96, buttonHeight);

            restoreSelectedButton.Location = new Point(refreshRestoreButton.Right + gap, top);
            restoreSelectedButton.Size = new Size(136, buttonHeight);

            restoreDefaultButton.Location = new Point(restoreSelectedButton.Right + gap, top);
            restoreDefaultButton.Size = new Size(136, buttonHeight);

            deleteRestoreButton.Location = new Point(restoreDefaultButton.Right + gap, top);
            deleteRestoreButton.Size = new Size(126, buttonHeight);

            openQuarantineButton.Location = new Point(deleteRestoreButton.Right + gap, top);
            openQuarantineButton.Size = new Size(138, buttonHeight);

            restoreRunLabel.Location = new Point(left, filterTop + 5);

            int runBoxLeft = restoreRunLabel.Right + smallGap;
            int runBoxWidth = 160;
            restoreRunBox.Location = new Point(runBoxLeft, filterTop);
            restoreRunBox.Size = new Size(runBoxWidth, 24);

            restoreSortLabel.Location = new Point(restoreRunBox.Right + 18, filterTop + 5);

            int sortBoxLeft = restoreSortLabel.Right + smallGap;
            restoreSortBox.Location = new Point(sortBoxLeft, filterTop);
            restoreSortBox.Size = new Size(150, 24);

            int directionLeft = restoreSortBox.Right + smallGap;
            int directionWidth = Math.Max(140, restorePage.ClientSize.Width - directionLeft - rightPadding);
            restoreSortDirectionBox.Location = new Point(directionLeft, filterTop);
            restoreSortDirectionBox.Size = new Size(directionWidth, 24);

            restoreSelectAllBox.Location = new Point(left, filterTop + 30);

            int listWidth = Math.Max(300, restorePage.ClientSize.Width - left - rightPadding - gap - detailsMinWidth);
            restoreListBox.Location = new Point(left, listTop);
            restoreListBox.Size = new Size(listWidth, Math.Max(220, restorePage.ClientSize.Height - listTop - bottomPadding));
            restoreListBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;

            restoreDetailsBox.Location = new Point(restoreListBox.Right + gap, listTop);
            restoreDetailsBox.Size = new Size(Math.Max(detailsMinWidth, restorePage.ClientSize.Width - restoreDetailsBox.Left - rightPadding), restoreListBox.Height);
            restoreDetailsBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        }

        private void BuildAdvancedTab()
        {
            advancedGeneralGroup.Text = "General";
            advancedGeneralGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            advancedPage.Controls.Add(advancedGeneralGroup);

            updateOnStartupBox.Text = "Check for updates on startup";
            updateOnStartupBox.Size = new Size(260, 24);
            updateOnStartupBox.CheckedChanged += delegate
            {
                if (updatingSettingsUi)
                    return;
                updateCheckOnStartup = updateOnStartupBox.Checked;
                SaveAppConfig();
            };
            advancedGeneralGroup.Controls.Add(updateOnStartupBox);

            portableModeBox.Text = "Portable mode";
            portableModeBox.Size = new Size(180, 24);
            portableModeBox.CheckedChanged += async delegate
            {
                if (updatingSettingsUi || portableModeChangeInProgress)
                    return;
                try
                {
                    await SetPortableModeAsync(portableModeBox.Checked);
                }
                catch (Exception ex)
                {
                    Log("ERROR Portable mode change failed: " + ex.Message);
                    MessageBox.Show(ex.Message, "CK3MPS", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            advancedGeneralGroup.Controls.Add(portableModeBox);

            settingsGuardAutoRepairBox.Text = "Allow guard auto-repair";
            settingsGuardAutoRepairBox.Size = new Size(220, 24);
            settingsGuardAutoRepairBox.CheckedChanged += delegate
            {
                if (updatingSettingsUi)
                    return;

                if (settingsGuardAutoRepairBox.Checked && !settingsGuardAutoRepairEnabled)
                {
                    DialogResult consent = MessageBox.Show(
                        "Automatic settings guard repair can rewrite CK3, launcher, and Steam configuration files after drift is detected.\r\n\r\nSave quarantine still stays manual-only.\r\n\r\nEnable auto-repair?",
                        "Settings guard auto-repair",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (consent != DialogResult.Yes)
                    {
                        updatingSettingsUi = true;
                        try { settingsGuardAutoRepairBox.Checked = false; } finally { updatingSettingsUi = false; }
                        return;
                    }
                }

                settingsGuardAutoRepairEnabled = settingsGuardAutoRepairBox.Checked;
                SaveAppConfig();
            };
            advancedGeneralGroup.Controls.Add(settingsGuardAutoRepairBox);

            advancedLogVerbosityLabel.Text = "Log verbosity:";
            advancedLogVerbosityLabel.AutoSize = true;
            advancedGeneralGroup.Controls.Add(advancedLogVerbosityLabel);

            logVerbosityBox.DropDownStyle = ComboBoxStyle.DropDownList;
            logVerbosityBox.Items.AddRange(new object[] { "Quiet", "Normal", "Verbose" });
            logVerbosityBox.Size = new Size(130, 24);
            logVerbosityBox.SelectedIndexChanged += delegate
            {
                if (updatingSettingsUi)
                    return;
                if (logVerbosityBox.SelectedItem != null)
                {
                    logVerbosity = Convert.ToString(logVerbosityBox.SelectedItem);
                    SaveAppConfig();
                }
            };
            advancedGeneralGroup.Controls.Add(logVerbosityBox);

            advancedHintLabel.Text = "Use this page for update behavior, portable mode, settings guard mode and cleanup tasks. Restore point deletion affects only CK3MPS-created system restore points on this PC.";
            advancedHintLabel.AutoSize = false;
            advancedHintLabel.ForeColor = Color.FromArgb(90, 90, 90);
            advancedGeneralGroup.Controls.Add(advancedHintLabel);

            advancedMaintenanceGroup.Text = "Maintenance";
            advancedMaintenanceGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            advancedPage.Controls.Add(advancedMaintenanceGroup);

            updateButton.Text = "Check updates";
            updateButton.Size = new Size(130, 34);
            updateButton.Click += delegate { CheckForUpdatesManual(); };
            advancedMaintenanceGroup.Controls.Add(updateButton);

            clearOtherLogsButton.Text = "Delete other logs";
            clearOtherLogsButton.Size = new Size(180, 34);
            clearOtherLogsButton.Click += delegate { ClearOtherLogs(); };

            clearQuarantineButton.Text = "Delete quarantine files";
            clearQuarantineButton.Size = new Size(180, 34);
            clearQuarantineButton.Click += delegate { ClearQuarantineFiles(); };

            updateDownloadProgress.Size = new Size(280, 22);
            advancedMaintenanceGroup.Controls.Add(updateDownloadProgress);

            advancedRestoreGroup.Text = "Delete Data";
            advancedRestoreGroup.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            advancedPage.Controls.Add(advancedRestoreGroup);

            restorePointsLabel.Text = "Restore Points";
            restorePointsLabel.AutoSize = true;
            advancedRestoreGroup.Controls.Add(restorePointsLabel);

            restorePointsListBox.CheckOnClick = true;
            restorePointsListBox.HorizontalScrollbar = true;
            restorePointsListBox.IntegralHeight = false;
            restorePointsListBox.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                if (e.Index < 0 || e.Index >= restorePointsListBox.Items.Count)
                    return;

                RestorePointListItem item = restorePointsListBox.Items[e.Index] as RestorePointListItem;
                if (!ShouldAllowRestorePointItemCheck(item, e.NewValue))
                {
                    e.NewValue = CheckState.Unchecked;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        SetStatusText("Only CK3MPS-created restore points can be deleted.");
                        UpdateRestorePointDeleteButtonState();
                    });
                    return;
                }

                BeginInvoke((MethodInvoker)delegate { UpdateRestorePointDeleteButtonState(); });
            };
            advancedRestoreGroup.Controls.Add(restorePointsListBox);

            deleteSelectedRestorePointsButton.Text = "Delete selected restore points";
            deleteSelectedRestorePointsButton.Size = new Size(240, 34);
            deleteSelectedRestorePointsButton.Enabled = false;
            deleteSelectedRestorePointsButton.Click += delegate { DeleteSelectedRestorePoints(); };
            advancedRestoreGroup.Controls.Add(deleteSelectedRestorePointsButton);
            advancedRestoreGroup.Controls.Add(clearOtherLogsButton);
            advancedRestoreGroup.Controls.Add(clearQuarantineButton);

            mainTabs.SelectedIndexChanged += async delegate
            {
                if (mainTabs.SelectedTab == workflowPage)
                    ShowWorkflowSnapshot();
                if (mainTabs.SelectedTab == advancedPage)
                    await RefreshRestorePointsListAsync();
            };
            advancedPage.Resize += delegate { LayoutAdvancedTabControls(); };
            LayoutAdvancedTabControls();
        }

        private void LayoutAdvancedTabControls()
        {
            const int left = 18;
            const int top = 18;
            const int gap = 12;
            const int buttonWidth = 130;
            const int rightPadding = 18;
            const int progressMinWidth = 180;
            int contentWidth = Math.Max(520, advancedPage.ClientSize.Width - left - rightPadding);
            int generalHeight = 164;
            int maintenanceHeight = 82;
            int restoreTop = top + generalHeight + gap + maintenanceHeight + gap;

            advancedGeneralGroup.Location = new Point(left, top);
            advancedGeneralGroup.Size = new Size(contentWidth, generalHeight);
            updateOnStartupBox.Location = new Point(14, 28);
            updateOnStartupBox.Size = new Size(250, 24);
            portableModeBox.Location = new Point(14, 56);
            portableModeBox.Size = new Size(180, 24);
            settingsGuardAutoRepairBox.Location = new Point(14, 84);
            settingsGuardAutoRepairBox.Size = new Size(220, 24);
            advancedLogVerbosityLabel.Location = new Point(300, 31);
            logVerbosityBox.Location = new Point(404, 27);
            logVerbosityBox.Size = new Size(Math.Min(150, Math.Max(130, advancedGeneralGroup.ClientSize.Width - 418)), 24);
            advancedHintLabel.Location = new Point(14, 116);
            advancedHintLabel.Size = new Size(advancedGeneralGroup.ClientSize.Width - 28, 40);

            advancedMaintenanceGroup.Location = new Point(left, advancedGeneralGroup.Bottom + gap);
            advancedMaintenanceGroup.Size = new Size(contentWidth, maintenanceHeight);
            updateButton.Location = new Point(14, 30);
            updateButton.Size = new Size(buttonWidth, 34);
            int progressLeft = updateButton.Right + gap;
            int progressWidth = Math.Max(progressMinWidth, advancedMaintenanceGroup.ClientSize.Width - progressLeft - 14);
            updateDownloadProgress.Location = new Point(progressLeft, 36);
            updateDownloadProgress.Size = new Size(progressWidth, 22);

            advancedRestoreGroup.Location = new Point(left, restoreTop);
            advancedRestoreGroup.Size = new Size(contentWidth, Math.Max(220, advancedPage.ClientSize.Height - restoreTop - 18));
            deleteSelectedRestorePointsButton.Location = new Point(14, 24);
            deleteSelectedRestorePointsButton.Size = new Size(240, 34);
            int cleanupButtonWidth = Math.Max(180, Math.Min(210, (advancedRestoreGroup.ClientSize.Width - 28 - gap * 2 - deleteSelectedRestorePointsButton.Width) / 2));
            clearOtherLogsButton.Location = new Point(deleteSelectedRestorePointsButton.Right + gap, 24);
            clearOtherLogsButton.Size = new Size(cleanupButtonWidth, 34);
            clearQuarantineButton.Location = new Point(clearOtherLogsButton.Right + gap, 24);
            clearQuarantineButton.Size = new Size(cleanupButtonWidth, 34);
            restorePointsLabel.Location = new Point(14, deleteSelectedRestorePointsButton.Bottom + 14);
            int listWidth = Math.Max(320, advancedRestoreGroup.ClientSize.Width - 28);
            restorePointsListBox.Location = new Point(14, restorePointsLabel.Bottom + 6);
            restorePointsListBox.Size = new Size(listWidth, Math.Max(180, advancedRestoreGroup.ClientSize.Height - restorePointsListBox.Top - 18));
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

        private async void RunStabilize()
        {
            int finalizeGeneration = ++deferredFinalizeGeneration;
            SetBusy(true);
            ClearLogViews();
            SetProgressValueSafe(0);
            SetProgressMaximumSafe(1);

            try
            {
                LogSection("Run started");
                Log("Preset: " + NullText(Convert.ToString(presetBox.SelectedItem)));
                Log("Selected steps: " + CountSelectedSteps());

                if (!ValidateBeforeRun())
                {
                    SetStatusText("Stopped: fix folder paths before running Stabilize.");
                    AppendRunHistory("stabilize", "stopped_path_validation");
                    return;
                }

                if (CountSelectedSteps() == 0)
                {
                    SetStatusText("No steps selected.");
                    Log("No steps selected. Choose a preset or tick steps manually.");
                    AppendRunHistory("stabilize", "stopped_no_steps");
                    return;
                }

                if (!HasReusableFreshCheckOnlyScan())
                {
                    SetStatusText("Run Scan first to activate Apply Settings.");
                    Log("INFO Apply Settings is locked until a fresh Scan is completed in this session.");
                    return;
                }

                Log("INFO Reusing the fresh Scan from this session.");
                LogSection("Stabilize plan");
                await EnsurePlanningSnapshotPreparedAsync("Preparing apply plan...");

                int plannedSteps = CountPlannedStabilizeSteps();
                SetProgressMaximumSafe(plannedSteps);
                Log("Planned steps after current-state filtering: " + plannedSteps);

                if (plannedSteps == 0)
                {
                    SetStatusText("All selected items are already applied.");
                    Log("INFO All selected items are already in the target state. No changes were needed.");
                    AppendRunHistory("stabilize", "stopped_no_changes_needed");
                    return;
                }

                if (IsGameRunning())
                {
                    MessageBox.Show("Close CK3 and Paradox Launcher first. Steam may stay open.", "CK3 is running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Log("Stopped: CK3 or Paradox Launcher is running.");
                    AppendRunHistory("stabilize", "stopped_game_running");
                    return;
                }

                bool shouldStartGuard = IsStepChecked(14) || IsStepChecked(15) || IsStepChecked(16);
                CaptureExecutionSnapshot();
                await Task.Run(delegate
                {
                    RunCoreStabilizeStep(1, "Safety: checking paths", CheckBasePaths, ShouldRunPathValidationCoreStep());
                    RunCoreStabilizeStep(2, "Safety: creating quarantine", CreateQuarantine, ShouldRunQuarantineCoreStep());
                    RunPlannedStabilizeStep(0, "Safety: creating Windows restore point", CreateWindowsRestorePoint);
                    RunPlannedStabilizeStep(3, "Windows network: flushing DNS cache", FlushDnsCache);
                    RunPlannedStabilizeStep(4, "Windows network: diagnosing adapters and routes", RunNetworkDiagnostics);
                    RunPlannedStabilizeStep(5, "Windows firewall: adding CK3 rules", EnsureFirewallRules);
                    RunPlannedStabilizeStep(6, "Windows registry: applying game/network profile", ApplyWindowsGameNetworkProfile);
                    RunPlannedStabilizeStep(7, "Windows adapters: tuning power profile", ApplyPowerAdapterProfile);
                    RunPlannedStabilizeStep(8, "Windows apps: checking overlays and VPNs", CheckOverlaysAndVpn);
                    RunPlannedStabilizeStep(9, "Windows network: checking online services", CheckOnlineServices);
                    RunPlannedStabilizeStep(10, "Launchers: backing up settings", BackupSteamAndLauncherSettings);
                    RunPlannedStabilizeStep(11, "Steam: stabilizing CK3 settings", StabilizeSteamSettings);
                    RunPlannedStabilizeStep(12, "Paradox Launcher: rebuilding database", RebuildParadoxLauncherDatabase);
                    RunPlannedStabilizeStep(13, "Launchers: checking runtime hygiene", CheckLauncherRuntimeHygiene);
                    RunPlannedStabilizeStep(14, "CK3 external profile: writing no-mod profile", ForceNoMods);
                    RunPlannedStabilizeStep(15, "CK3 external settings: stabilizing settings", StabilizePdxSettings);
                    RunPlannedStabilizeStep(16, "CK3 runtime verification: writing launch report", WriteRuntimeVerificationReport);
                    RunPlannedStabilizeStep(17, "CK3 in-game rules: writing game-rule profile", WriteStableGameRuleProfile);
                    RunPlannedStabilizeStep(18, "CK3 user state: clearing player UI state", ClearPlayerState);
                    RunPlannedStabilizeStep(19, "CK3 reports: archiving OOS and crashes", ArchiveReports);
                    RunPlannedStabilizeStep(20, "CK3 cache: clearing CK3 and launcher caches", ClearCaches);
                    RunPlannedStabilizeStep(21, "CK3 mods: quarantining .mod descriptors", QuarantineModDescriptors);
                    RunPlannedStabilizeStep(22, "CK3 binaries: inspecting non-vanilla files", QuarantineLoaderFiles);
                    RunPlannedStabilizeStep(23, "CK3 saves: stabilizing save launch hygiene", StabilizeSaveHygiene);
                    RunPlannedStabilizeStep(24, "CK3 folder cleanup: removing nonessential files", CleanCk3DocumentsFolder);
                    RunPlannedStabilizeStep(25, "OOS reports: analyzing latest metadata", AnalyzeLatestOosReport);
                    RunPlannedStabilizeStep(26, "OOS evidence: writing support package index", WriteOosEvidencePack);
                    RunPlannedStabilizeStep(27, "OOS protocol: writing prevention rules", WriteOosPreventionProtocol);
                    RunPlannedStabilizeStep(28, "MP parity: writing comparison manifest", WriteMultiplayerParityManifest);
                    LogSection("Final readiness summary");
                    RunReadinessChecks(true);
                });
                string[] runLogLines = SnapshotRunLogLines();
                int readinessFailures = lastReadinessFailures;
                lastCheckOnlyReportText = BuildCheckOnlyReportText(readinessFailures, runLogLines);
                exportScanReportButton.Enabled = true;
                string historyResult;

                if (readinessFailures == 0)
                {
                    SetStatusText("Done. CK3 profile is prepared for stable vanilla multiplayer.");
                    Log("Done. Use host local save, no hotjoin, speed 1-2 after load.");
                    historyResult = "ready";
                }
                else
                {
                    SetStatusText("Completed with blockers. Fix failed readiness checks before serious MP.");
                    Log("RESULT Completed with blockers. Fix failed readiness checks before serious MP.");
                    historyResult = "completed_with_blockers";
                }

                string historyLine = BuildRunHistoryLine(
                    "stabilize",
                    historyResult,
                    Convert.ToString(presetBox.SelectedItem),
                    ck3Install,
                    ck3Docs,
                    readinessFailures);
                BeginDeferredStabilizeFinalize(finalizeGeneration, readinessFailures, runLogLines, historyLine, shouldStartGuard);

                InvalidateFreshCheckOnlyScan();
            }
            catch (Exception ex)
            {
                SetStatusText("Failed: " + ex.Message);
                Log("ERROR: " + ex);
                AppendRunHistory("stabilize", "failed");
                MessageBox.Show(ex.Message, "CK3MPS", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ClearExecutionSnapshot();
                SetBusy(false);
            }
        }

        private async void RunReview()
        {
            if (!HasReusableFreshCheckOnlyScan())
            {
                SetStatusText("Run Scan first to unlock Review.");
                return;
            }

            string previousStatus = statusLabel.Text;
            previewButton.Enabled = false;
            stabilizeButton.Enabled = false;
            checkButton.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                await EnsurePlanningSnapshotPreparedAsync("Preparing review...");
                SetStatusText("Review ready.");
                ShowStabilizationPreview(false);
            }
            finally
            {
                Cursor = Cursors.Default;
                checkButton.Enabled = !busyUi;
                UpdateApplyButtonState();
                if (String.Equals(statusLabel.Text, "Preparing review...", StringComparison.Ordinal)
                    || String.Equals(statusLabel.Text, "Review ready.", StringComparison.Ordinal))
                    statusLabel.Text = previousStatus;
            }
        }

        private async Task EnsurePlanningSnapshotPreparedAsync(string statusText)
        {
            string key;
            CaptureExecutionSnapshot();
            key = BuildPlanningSnapshotKey();
            try
            {
                if (sessionScanSnapshot != null && String.Equals(sessionScanSnapshot.ScanKey, key, StringComparison.Ordinal))
                    return;

                SetStatusText(statusText);
                await Task.Run(delegate { sessionScanSnapshot = BuildSessionScanSnapshot(key); });
            }
            finally
            {
                ClearExecutionSnapshot();
            }
        }

        private async void RunCheckOnly()
        {
            int finalizeGeneration = ++deferredFinalizeGeneration;
            SetBusy(true);
            ClearLogViews();
            SetProgressValueSafe(0);
            SetProgressMaximumSafe(steps.Items.Count);
            try
            {
                LogSection("Scan started");
                Log("Mode: read-only scan of every checklist item. No files or settings will be changed.");
                if (!ValidateBeforeRun())
                {
                    SetStatusText("Check stopped: fix folder paths first.");
                    return;
                }

                CaptureExecutionSnapshot();
                await Task.Run(delegate { RunCheckOnlyScanCore(false, true); });
                string[] runLogLines = SnapshotRunLogLines();
                int readinessFailures = lastReadinessFailures;
                SetStatusText("Scan complete. Apply Settings is now available for this session.");
                string historyResult = readinessFailures == 0 ? "ready" : "completed_with_blockers";
                string historyLine = BuildRunHistoryLine(
                    "check_only",
                    historyResult,
                    Convert.ToString(presetBox.SelectedItem),
                    ck3Install,
                    ck3Docs,
                    readinessFailures);
                BeginDeferredCheckOnlyFinalize(finalizeGeneration, readinessFailures, runLogLines, historyLine);
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
            }
            finally
            {
                ClearExecutionSnapshot();
                SetBusy(false);
            }
        }

        private void RunCheckOnlyScanCore(bool writeReport, bool advanceProgress)
        {
            bool resumeOosWatcher = oosWatcherCancelSource != null && !oosWatcherCancelSource.IsCancellationRequested;
            if (resumeOosWatcher)
                StopOosWatcherServices(3000);
            readOnlyScanMode = true;
            MutationAudit.BeginReadOnlyScope();
            try
            {
                for (int i = 0; i < steps.Items.Count; i++)
                    RunCheckStep(i, advanceProgress);

                LogSection("Final readiness summary");
                RunReadinessChecks(false);
                if (writeReport)
                    WriteCheckOnlyReport();
                StoreFreshCheckOnlySessionSnapshot();
            }
            finally
            {
                lastReadOnlyMutationAttempts = MutationAudit.EndReadOnlyScope();
                readOnlyScanMode = false;
                if (resumeOosWatcher)
                    StartOosWatcherServices();
                if (lastReadOnlyMutationAttempts.Length > 0)
                    throw new InvalidOperationException("Read-only Scan attempted " + lastReadOnlyMutationAttempts.Length + " mutation(s): " + String.Join(", ", lastReadOnlyMutationAttempts));
            }
        }

        private void RunStep(int index, string label, Action action)
        {
            Stopwatch sw = Stopwatch.StartNew();
            SetStatusText(label + "...");
            LogSection(label);
            SetProgressStyleSafe(ProgressBarStyle.Marquee);
            FlushPendingUiLogLines();
            if (!InvokeRequired)
                Application.DoEvents();
            action();
            sw.Stop();
            SetProgressStyleSafe(ProgressBarStyle.Blocks);
            Log("INFO Step finished in " + FormatDurationMs(sw.ElapsedMilliseconds) + ": " + label);
            FlushPendingUiLogLines();
            IncrementProgressValueSafe();
            if (!InvokeRequired)
                Application.DoEvents();
        }

        private void BeginDeferredCheckOnlyFinalize(int finalizeGeneration, int readinessFailures, string[] runLogLines, string historyLine)
        {
            Task.Run(delegate
            {
                if (finalizeGeneration != deferredFinalizeGeneration)
                    return;
            });
        }

        private void ExportLastScanReport()
        {
            if (String.IsNullOrWhiteSpace(lastCheckOnlyReportText))
            {
                MessageBox.Show("Run Scan before exporting its report.", "Export Scan Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Export Scan Report";
                dialog.Filter = "Text report (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.FileName = "CK3MPS_scan_report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                SafeAtomicFile.WriteAllText(dialog.FileName, lastCheckOnlyReportText, Encoding.UTF8);
                SetStatusText("Scan report exported: " + dialog.FileName);
            }
        }

        private void BeginDeferredStabilizeFinalize(int finalizeGeneration, int readinessFailures, string[] runLogLines, string historyLine, bool shouldStartGuard)
        {
            Task.Run(delegate
            {
                if (finalizeGeneration != deferredFinalizeGeneration)
                    return;

                WriteStabilityReportSnapshot(readinessFailures, runLogLines);
                AppendRunHistoryLineAsync(historyLine);
                if (shouldStartGuard && finalizeGeneration == deferredFinalizeGeneration)
                    StartSettingsGuardDeferred();
            });
        }

    }
}



