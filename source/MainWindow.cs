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
            mainTabs.TabPages.Add(reportsPage);
            mainTabs.TabPages.Add(restorePage);
            mainTabs.TabPages.Add(advancedPage);
            Controls.Add(mainTabs);

            BuildMainTab();
            BuildPathsTab();
            BuildReportsTab();
            BuildRestoreTab();
            BuildAdvancedTab();

            statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            statusLabel.Text = "Ready.";
            Controls.Add(statusLabel);

            Resize += delegate { LayoutRootControls(); };
            LayoutRootControls();

            stabilizeButton.Text = "Stabilize CK3";
            stabilizeButton.Size = new Size(150, 34);
            stabilizeButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            stabilizeButton.Click += delegate { RunStabilize(); };
            mainPage.Controls.Add(stabilizeButton);

            checkButton.Text = "Check only";
            checkButton.Size = new Size(130, 34);
            checkButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            checkButton.Click += delegate { RunCheckOnly(); };
            mainPage.Controls.Add(checkButton);

            openFolderButton.Text = "Open quarantine";
            openFolderButton.Size = new Size(150, 34);
            openFolderButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            openFolderButton.Click += delegate
            {
                if (!String.IsNullOrEmpty(lastQuarantine) && Directory.Exists(lastQuarantine))
                    Process.Start("explorer.exe", lastQuarantine);
                else if (Directory.Exists(ck3Docs))
                    Process.Start("explorer.exe", ck3Docs);
            };
            mainPage.Controls.Add(openFolderButton);
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
            graphicsProfileBox.SelectedIndexChanged += delegate { InvalidateFreshCheckOnlyScan(); };
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
            int labelY = 56;
            int logTop = 78;
            int actionButtonTop = mainPage.ClientSize.Height - 46;
            int progressTop = actionButtonTop - 34;
            int availableHeight = Math.Max(170, progressTop - top - bottomMargin);

            checklistPanel.Location = new Point(leftMargin, top);
            checklistPanel.Size = new Size(checklistWidth, availableHeight);

            liveLogLabel.Location = new Point(checklistPanel.Right + gap, labelY);

            logBox.Location = new Point(checklistPanel.Right + gap, logTop);
            logBox.Size = new Size(Math.Max(260, mainPage.ClientSize.Width - logBox.Left - leftMargin), Math.Max(150, progressTop - logTop - bottomMargin));

            progress.Location = new Point(leftMargin, progressTop);
            progress.Size = new Size(mainPage.ClientSize.Width - (leftMargin * 2), 22);

            stabilizeButton.Location = new Point(leftMargin, actionButtonTop);
            checkButton.Location = new Point(stabilizeButton.Right + gap, actionButtonTop);
            openFolderButton.Location = new Point(checkButton.Right + gap, actionButtonTop);
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
            updateOnStartupBox.Text = "Check for updates on startup";
            updateOnStartupBox.Location = new Point(18, 22);
            updateOnStartupBox.Size = new Size(260, 24);
            updateOnStartupBox.CheckedChanged += delegate
            {
                if (updatingSettingsUi)
                    return;
                updateCheckOnStartup = updateOnStartupBox.Checked;
                SaveAppConfig();
            };
            advancedPage.Controls.Add(updateOnStartupBox);

            portableModeBox.Text = "Portable mode";
            portableModeBox.Location = new Point(18, 56);
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
                if (updatingSettingsUi)
                    return;
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

            restorePointsLabel.Text = "Restore Points";
            restorePointsLabel.AutoSize = true;
            restorePointsLabel.Location = new Point(18, 184);
            advancedPage.Controls.Add(restorePointsLabel);

            restorePointsListBox.CheckOnClick = true;
            restorePointsListBox.HorizontalScrollbar = true;
            restorePointsListBox.IntegralHeight = false;
            advancedPage.Controls.Add(restorePointsListBox);

            deleteSelectedRestorePointsButton.Text = "Delete selected restore points";
            deleteSelectedRestorePointsButton.Location = new Point(18, 418);
            deleteSelectedRestorePointsButton.Size = new Size(240, 34);
            deleteSelectedRestorePointsButton.Click += delegate { DeleteSelectedRestorePoints(); };
            advancedPage.Controls.Add(deleteSelectedRestorePointsButton);

            clearOtherLogsButton.Text = "Delete other logs";
            clearOtherLogsButton.Location = new Point(18, 316);
            clearOtherLogsButton.Size = new Size(180, 34);
            clearOtherLogsButton.Click += delegate { ClearOtherLogs(); };
            advancedPage.Controls.Add(clearOtherLogsButton);

            clearQuarantineButton.Text = "Delete quarantine files";
            clearQuarantineButton.Location = new Point(18, 356);
            clearQuarantineButton.Size = new Size(180, 34);
            clearQuarantineButton.Click += delegate { ClearQuarantineFiles(); };
            advancedPage.Controls.Add(clearQuarantineButton);

            updateDownloadProgress.Location = new Point(164, 142);
            updateDownloadProgress.Size = new Size(280, 22);
            advancedPage.Controls.Add(updateDownloadProgress);

            mainTabs.SelectedIndexChanged += async delegate
            {
                if (mainTabs.SelectedTab == advancedPage)
                    await RefreshRestorePointsListAsync();
            };
            advancedPage.Resize += delegate { LayoutAdvancedTabControls(); };
            LayoutAdvancedTabControls();
        }

        private void LayoutAdvancedTabControls()
        {
            const int left = 18;
            const int topButton = 136;
            const int buttonWidth = 130;
            const int gap = 16;
            const int rightPadding = 18;
            const int progressMinWidth = 180;

            updateButton.Location = new Point(left, topButton);
            updateButton.Size = new Size(buttonWidth, 34);

            int progressLeft = updateButton.Right + gap;
            int progressWidth = Math.Max(progressMinWidth, advancedPage.ClientSize.Width - progressLeft - rightPadding);
            updateDownloadProgress.Location = new Point(progressLeft, topButton + 6);
            updateDownloadProgress.Size = new Size(progressWidth, 22);

            int rightColumnWidth = Math.Max(200, Math.Min(260, advancedPage.ClientSize.Width - left - rightPadding));
            clearOtherLogsButton.Location = new Point(advancedPage.ClientSize.Width - rightPadding - rightColumnWidth, 18);
            clearOtherLogsButton.Size = new Size(rightColumnWidth, 34);

            clearQuarantineButton.Location = new Point(clearOtherLogsButton.Left, clearOtherLogsButton.Bottom + 10);
            clearQuarantineButton.Size = new Size(rightColumnWidth, 34);

            int listRight = clearOtherLogsButton.Left - gap;
            int listWidth = Math.Max(320, listRight - left);
            restorePointsLabel.Location = new Point(left, topButton + 48);
            restorePointsListBox.Location = new Point(left, restorePointsLabel.Bottom + 6);
            int deleteButtonBottomPadding = 18;
            int deleteButtonHeight = 34;
            int listBottomGap = 10;
            restorePointsListBox.Size = new Size(listWidth, Math.Max(180, advancedPage.ClientSize.Height - restorePointsListBox.Top - deleteButtonHeight - listBottomGap - deleteButtonBottomPadding));
            deleteSelectedRestorePointsButton.Location = new Point(left, advancedPage.ClientSize.Height - deleteButtonHeight - deleteButtonBottomPadding);
            deleteSelectedRestorePointsButton.Size = new Size(Math.Min(260, listWidth), 34);
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

                if (HasReusableFreshCheckOnlyScan())
                {
                    Log("INFO Reusing the fresh Check Only scan from this session.");
                }
                else
                {
                    LogSection("Preflight check");
                    Log("INFO No fresh Check Only scan is available. Running the read-only checklist first.");
                    await Task.Run(delegate { RunCheckOnlyScanCore(false, false); });
                    LogSection("Stabilize plan");
                }

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

                if (!ConfirmStabilizationPreview())
                {
                    SetStatusText("Stopped: preview was not confirmed.");
                    Log("INFO Stabilize stopped before changes: preview was not confirmed.");
                    AppendRunHistory("stabilize", "stopped_preview_not_confirmed");
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
                    LogSection("Automatic report");
                    WriteStabilityReport();
                });
                if (shouldStartGuard)
                    StartSettingsGuard();

                if (lastReadinessFailures == 0)
                {
                    SetStatusText("Done. CK3 profile is prepared for stable vanilla multiplayer.");
                    Log("Done. Use host local save, no hotjoin, speed 1-2 after load.");
                    AppendRunHistory("stabilize", "ready");
                }
                else
                {
                    SetStatusText("Completed with blockers. Fix failed readiness checks before serious MP.");
                    Log("RESULT Completed with blockers. Fix failed readiness checks before serious MP.");
                    AppendRunHistory("stabilize", "completed_with_blockers");
                }

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

        private async void RunCheckOnly()
        {
            SetBusy(true);
            ClearLogViews();
            SetProgressValueSafe(0);
            SetProgressMaximumSafe(steps.Items.Count);
            try
            {
                LogSection("Check only started");
                Log("Mode: read-only scan of every checklist item. No files or settings will be changed.");
                if (!ValidateBeforeRun())
                {
                    SetStatusText("Check stopped: fix folder paths first.");
                    AppendRunHistory("check_only", "stopped_path_validation");
                    return;
                }

                CaptureExecutionSnapshot();
                await Task.Run(delegate { RunCheckOnlyScanCore(true, true); });
                SetStatusText("Check complete. Every checklist item was checked in read-only mode.");
                AppendRunHistory("check_only", lastReadinessFailures == 0 ? "ready" : "completed_with_blockers");
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
                AppendRunHistory("check_only", "failed");
            }
            finally
            {
                ClearExecutionSnapshot();
                SetBusy(false);
            }
        }

        private void RunCheckOnlyScanCore(bool writeReport, bool advanceProgress)
        {
            for (int i = 0; i < steps.Items.Count; i++)
                RunCheckStep(i, advanceProgress);

            LogSection("Final readiness summary");
            RunReadinessChecks(false);
            if (writeReport)
                WriteCheckOnlyReport();
            MarkFreshCheckOnlyScan();
        }

        private void RunStep(int index, string label, Action action)
        {
            SetStatusText(label + "...");
            LogSection(label);
            SetProgressStyleSafe(ProgressBarStyle.Marquee);
            FlushPendingUiLogLines();
            if (!InvokeRequired)
                Application.DoEvents();
            action();
            SetProgressStyleSafe(ProgressBarStyle.Blocks);
            FlushPendingUiLogLines();
            IncrementProgressValueSafe();
            if (!InvokeRequired)
                Application.DoEvents();
        }

    }
}



