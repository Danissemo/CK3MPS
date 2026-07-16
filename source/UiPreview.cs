using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed class UiPreviewForm : Form
    {
        private static readonly Color PageBack = Color.FromArgb(240, 240, 240);
        private static readonly Color Surface = Color.FromArgb(252, 252, 252);
        private static readonly Color TextColor = Color.FromArgb(46, 56, 72);
        private static readonly Color Muted = Color.FromArgb(90, 90, 90);
        private static readonly Color Border = Color.FromArgb(205, 212, 222);
        private static readonly Color Blue = Color.FromArgb(34, 110, 190);
        private static readonly Color Green = Color.FromArgb(38, 138, 91);
        private static readonly Color Amber = Color.FromArgb(188, 125, 24);
        private static readonly Color Red = Color.FromArgb(183, 60, 60);

        private readonly TabControl mainTabs = new TabControl();
        private readonly TabPage mainPage = new TabPage("Main");
        private readonly TabPage pathsPage = new TabPage("Paths");
        private readonly TabPage workflowPage = new TabPage("Workflow");
        private readonly TabPage reportsPage = new TabPage("Reports");
        private readonly TabPage restorePage = new TabPage("Restore");
        private readonly TabPage advancedPage = new TabPage("Advanced");
        private readonly Label statusLabel = new Label();

        private readonly ComboBox presetBox = new ComboBox();
        private readonly ComboBox graphicsProfileBox = new ComboBox();
        private readonly Button selectAllButton = new Button();
        private readonly Button selectNoneButton = new Button();
        private readonly Button stabilizeButton = new Button();
        private readonly Button checkButton = new Button();
        private readonly Button exportScanReportButton = new Button();
        private readonly Button previewButton = new Button();
        private readonly Label graphicsSectionLabel = new Label();
        private readonly Label graphicsHintLabel = new Label();
        private readonly Label liveLogLabel = new Label();
        private readonly Panel checklistPanel = new Panel();
        private readonly Panel checklistContentPanel = new Panel();
        private readonly VScrollBar checklistScrollBar = new VScrollBar();
        private readonly RichTextBox logBox = new RichTextBox();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly List<PreviewStepGroup> checklistGroups = new List<PreviewStepGroup>();

        private readonly TextBox gamePathBox = new TextBox();
        private readonly TextBox settingsPathBox = new TextBox();
        private readonly Button gamePathBrowseButton = new Button();
        private readonly Button settingsPathBrowseButton = new Button();
        private readonly Button openGamePathButton = new Button();
        private readonly Button openSettingsPathButton = new Button();
        private readonly Button resetPathsButton = new Button();
        private readonly Label gamePathStatusLabel = new Label();
        private readonly Label settingsPathStatusLabel = new Label();
        private readonly Label pathDetailsLabel = new Label();

        private readonly Button openReportsButton = new Button();
        private readonly Button exportSupportButton = new Button();
        private readonly Button refreshHistoryButton = new Button();
        private readonly Button clearReportsButton = new Button();
        private readonly RichTextBox historyBox = new RichTextBox();

        private readonly Button refreshRestoreButton = new Button();
        private readonly Button restoreSelectedButton = new Button();
        private readonly Button restoreDefaultButton = new Button();
        private readonly Button deleteRestoreButton = new Button();
        private readonly Button openQuarantineButton = new Button();
        private readonly Label restoreRunLabel = new Label();
        private readonly ComboBox restoreRunBox = new ComboBox();
        private readonly Label restoreSortLabel = new Label();
        private readonly ComboBox restoreSortBox = new ComboBox();
        private readonly ComboBox restoreSortDirectionBox = new ComboBox();
        private readonly CheckBox restoreSelectAllBox = new CheckBox();
        private readonly CheckedListBox restoreListBox = new CheckedListBox();
        private readonly TextBox restoreDetailsBox = new TextBox();

        private readonly GroupBox advancedGeneralGroup = new GroupBox();
        private readonly GroupBox advancedMaintenanceGroup = new GroupBox();
        private readonly GroupBox advancedRestoreGroup = new GroupBox();
        private readonly CheckBox updateOnStartupBox = new CheckBox();
        private readonly CheckBox portableModeBox = new CheckBox();
        private readonly CheckBox settingsGuardAutoRepairBox = new CheckBox();
        private readonly Label advancedLogVerbosityLabel = new Label();
        private readonly ComboBox logVerbosityBox = new ComboBox();
        private readonly Label advancedHintLabel = new Label();
        private readonly Button updateButton = new Button();
        private readonly ProgressBar updateDownloadProgress = new ProgressBar();
        private readonly Button deleteSelectedRestorePointsButton = new Button();
        private readonly Button clearOtherLogsButton = new Button();
        private readonly Button clearQuarantineButton = new Button();
        private readonly Label restorePointsLabel = new Label();
        private readonly CheckedListBox restorePointsListBox = new CheckedListBox();

        private readonly Panel workflowHeaderPanel = new Panel();
        private readonly Panel workflowStatusPanel = new Panel();
        private readonly Panel workflowStatusAccentPanel = new Panel();
        private readonly Panel workflowStepsPanel = new Panel();
        private readonly Panel workflowSummaryPanel = new Panel();
        private readonly Label workflowModeLabel = new Label();
        private readonly ComboBox workflowModeBox = new ComboBox();
        private readonly Label workflowSaveLabel = new Label();
        private readonly ComboBox workflowSaveBox = new ComboBox();
        private readonly Button workflowSaveBrowseButton = new Button();
        private readonly Button workflowApplySafeStartButton = new Button();
        private readonly Button workflowRepairSaveButton = new Button();
        private readonly Button workflowCompareParityButton = new Button();
        private readonly Button workflowParityRoomButton = new Button();
        private readonly Button workflowMoreButton = new Button();
        private readonly Label workflowVerdictLabel = new Label();
        private readonly Label workflowHintLabel = new Label();
        private readonly Label workflowStepsLabel = new Label();
        private readonly ListBox workflowStepsListBox = new ListBox();
        private readonly Label workflowSummaryLabel = new Label();
        private readonly RichTextBox workflowSummaryBox = new RichTextBox();

        public UiPreviewForm()
        {
            Text = "CK3MPS v0.3 - style preview";
            Width = 980;
            Height = 760;
            MinimumSize = new Size(900, 700);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BackColor = PageBack;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            BuildUi();
            ApplyWorkflowTheme();
            PopulatePreviewData();
        }

        private void BuildUi()
        {
            Label title = new Label();
            title.Text = "CK3MPS";
            title.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(16, 14);
            Controls.Add(title);

            Label subtitle = new Label();
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
            statusLabel.Text = "Preview mode. Controls are visual only.";
            Controls.Add(statusLabel);

            Resize += delegate
            {
                LayoutRootControls();
                LayoutMainTabControls();
                LayoutPathsTabControls();
                LayoutWorkflowTabControls();
                LayoutReportsTabControls();
                LayoutRestoreTabControls();
                LayoutAdvancedTabControls();
            };
            LayoutRootControls();
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
            Label presetLabel = new Label();
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
            mainPage.Controls.Add(presetBox);

            selectAllButton.Text = "All";
            selectAllButton.Location = new Point(262, 12);
            selectAllButton.Size = new Size(58, 28);
            mainPage.Controls.Add(selectAllButton);

            selectNoneButton.Text = "None";
            selectNoneButton.Location = new Point(328, 12);
            selectNoneButton.Size = new Size(70, 28);
            mainPage.Controls.Add(selectNoneButton);

            graphicsSectionLabel.Text = "In-game graphics profile";
            graphicsSectionLabel.AutoSize = true;
            graphicsSectionLabel.Font = new Font(Font, FontStyle.Bold);
            mainPage.Controls.Add(graphicsSectionLabel);

            graphicsHintLabel.Text = "Changes CK3 graphics settings in `pdx_settings.txt`. Choose a safer profile for multiplayer stability or keep the current game graphics.";
            graphicsHintLabel.AutoSize = false;
            graphicsHintLabel.ForeColor = Muted;
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
            mainPage.Controls.Add(graphicsProfileBox);

            liveLogLabel.Text = "Live log:";
            liveLogLabel.AutoSize = true;
            mainPage.Controls.Add(liveLogLabel);

            checklistPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            checklistPanel.BorderStyle = BorderStyle.FixedSingle;
            checklistPanel.TabStop = true;
            checklistContentPanel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            checklistPanel.Controls.Add(checklistContentPanel);
            checklistScrollBar.Width = SystemInformation.VerticalScrollBarWidth;
            checklistScrollBar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            checklistScrollBar.Scroll += delegate { UpdateChecklistScrollPosition(); };
            checklistPanel.Controls.Add(checklistScrollBar);
            checklistPanel.MouseWheel += delegate(object sender, MouseEventArgs e) { ScrollChecklistWheel(e.Delta); };
            checklistPanel.Resize += delegate { ResizeChecklistRows(); };
            mainPage.Controls.Add(checklistPanel);

            BuildChecklistGroups();

            ConfigureLogView(logBox);
            mainPage.Controls.Add(logBox);

            progress.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            progress.Maximum = 29;
            progress.Value = 0;
            mainPage.Controls.Add(progress);

            checkButton.Text = "Scan";
            checkButton.Size = new Size(130, 34);
            checkButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            mainPage.Controls.Add(checkButton);

            exportScanReportButton.Text = "Export Scan Report";
            exportScanReportButton.Size = new Size(150, 34);
            exportScanReportButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            exportScanReportButton.Enabled = false;
            mainPage.Controls.Add(exportScanReportButton);

            previewButton.Text = "Review";
            previewButton.Size = new Size(110, 34);
            previewButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            mainPage.Controls.Add(previewButton);

            stabilizeButton.Text = "Apply Settings";
            stabilizeButton.Size = new Size(150, 34);
            stabilizeButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            stabilizeButton.Enabled = false;
            mainPage.Controls.Add(stabilizeButton);

            mainPage.Resize += delegate { LayoutMainTabControls(); };
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

            ResizeChecklistRows();
        }

        private void BuildChecklistGroups()
        {
            checklistContentPanel.Controls.Clear();
            checklistGroups.Clear();

            AddChecklistGroup("Safety Options", new[]
            {
                "Create Windows restore point",
                "Check CK3 folders and running processes",
                "Create timestamped quarantine backup"
            });

            AddChecklistGroup("Windows and Network Settings", new[]
            {
                "Flush DNS cache",
                "Diagnose adapters, routes, DNS, MTU and TCP/IP",
                "Add CK3 allow rules when elevated",
                "Apply game/network stability profile",
                "Tune power and adapter stability profile",
                "Check overlays, VPNs and competing background apps",
                "Check Paradox and Steam online reachability"
            });

            AddChecklistGroup("Launch Settings", new[]
            {
                "Back up Steam and Paradox Launcher settings",
                "Stabilize CK3 launch/cloud/overlay settings",
                "Rebuild CK3 launcher database",
                "Check runtime hygiene"
            });

            AddChecklistGroup("Game Settings", new[]
            {
                "Force no-mod dlc_load.json",
                "Stabilize pdx_settings.txt",
                "Confirm launched profile",
                "Write stable new-campaign profile"
            });

            AddChecklistGroup("Files and Cache", new[]
            {
                "Clear player UI state",
                "Archive OOS and crash reports",
                "Clear CK3 and launcher caches",
                "Quarantine local .mod descriptors",
                "Inspect non-vanilla loader files",
                "Check active save and save-folder hygiene",
                "Remove nonessential files, keep saves"
            });

            AddChecklistGroup("Diagnostics", new[]
            {
                "Analyze latest OOS metadata",
                "Write support package index",
                "Write prevention rules",
                "Write player comparison manifest"
            });

            ResizeChecklistRows();
            RelayoutChecklistGroups();
        }

        private void AddChecklistGroup(string title, string[] items)
        {
            PreviewStepGroup group = new PreviewStepGroup();
            group.Title = title;
            group.Items = items;
            group.Expanded = false;

            group.Header.Height = 30;
            group.Header.BackColor = Color.FromArgb(242, 244, 247);

            group.ToggleButton.Text = "+";
            group.ToggleButton.Size = new Size(28, 24);
            group.ToggleButton.Location = new Point(4, 3);
            group.ToggleButton.Click += delegate
            {
                group.Expanded = !group.Expanded;
                group.ToggleButton.Text = group.Expanded ? "-" : "+";
                RelayoutChecklistGroups();
            };
            group.Header.Controls.Add(group.ToggleButton);

            group.CheckBox.ThreeState = true;
            group.CheckBox.CheckState = CheckState.Checked;
            group.CheckBox.Size = new Size(22, 24);
            group.CheckBox.Location = new Point(36, 3);
            group.Header.Controls.Add(group.CheckBox);

            group.TitleLabel.Text = title;
            group.TitleLabel.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            group.TitleLabel.Location = new Point(62, 6);
            group.TitleLabel.Height = 18;
            group.Header.Controls.Add(group.TitleLabel);

            checklistContentPanel.Controls.Add(group.Header);

            foreach (string item in items)
            {
                Panel row = new Panel();
                row.Height = 28;

                CheckBox box = new CheckBox();
                box.Checked = true;
                box.Size = new Size(22, 22);
                box.Location = new Point(38, 3);
                row.Controls.Add(box);

                Label help = new Label();
                help.Text = "?";
                help.Size = new Size(24, 22);
                help.Location = new Point(66, 3);
                help.BorderStyle = BorderStyle.FixedSingle;
                help.TextAlign = ContentAlignment.MiddleCenter;
                help.BackColor = Color.FromArgb(240, 242, 245);
                help.ForeColor = Color.FromArgb(50, 65, 85);
                row.Controls.Add(help);

                Label text = new Label();
                text.Text = item;
                text.Location = new Point(98, 6);
                text.Height = 18;
                row.Controls.Add(text);

                group.Rows.Add(row);
                checklistContentPanel.Controls.Add(row);
            }

            checklistGroups.Add(group);
        }

        private int ChecklistContentWidth()
        {
            int width = checklistPanel.ClientSize.Width - checklistScrollBar.Width - 6;
            if (width < 300)
                width = checklistPanel.ClientSize.Width - 8;
            return Math.Max(300, width);
        }

        private void ResizeChecklistRows()
        {
            int width = ChecklistContentWidth();
            foreach (PreviewStepGroup group in checklistGroups)
            {
                group.Header.Size = new Size(width, 30);
                group.TitleLabel.Width = Math.Max(100, width - group.TitleLabel.Left - 8);
                foreach (Panel row in group.Rows)
                {
                    row.Size = new Size(width, 28);
                    foreach (Control control in row.Controls)
                    {
                        Label label = control as Label;
                        if (label != null && label.Text != "?")
                            label.Width = Math.Max(100, width - label.Left - 8);
                    }
                }
            }

            checklistScrollBar.Location = new Point(Math.Max(0, checklistPanel.ClientSize.Width - checklistScrollBar.Width - 1), 1);
            checklistScrollBar.Height = Math.Max(0, checklistPanel.ClientSize.Height - 2);
            RelayoutChecklistGroups();
        }

        private void RelayoutChecklistGroups()
        {
            int y = 4;
            foreach (PreviewStepGroup group in checklistGroups)
            {
                group.Header.Location = new Point(0, y);
                y += group.Header.Height;

                foreach (Panel row in group.Rows)
                {
                    row.Visible = group.Expanded;
                    row.Location = new Point(0, y);
                    if (group.Expanded)
                        y += row.Height;
                }

                y += 4;
            }

            int contentHeight = Math.Max(0, y + 4);
            checklistContentPanel.Size = new Size(ChecklistContentWidth(), Math.Max(checklistPanel.ClientSize.Height, contentHeight));

            int viewportHeight = Math.Max(1, checklistPanel.ClientSize.Height - 2);
            int maxValue = Math.Max(0, contentHeight - viewportHeight);
            checklistScrollBar.Minimum = 0;
            checklistScrollBar.SmallChange = 24;
            checklistScrollBar.LargeChange = viewportHeight;
            checklistScrollBar.Maximum = maxValue + checklistScrollBar.LargeChange - 1;
            checklistScrollBar.Enabled = maxValue > 0;
            if (!checklistScrollBar.Enabled)
                checklistScrollBar.Value = 0;
            else if (checklistScrollBar.Value > maxValue)
                checklistScrollBar.Value = maxValue;

            UpdateChecklistScrollPosition();
        }

        private void ScrollChecklistWheel(int delta)
        {
            if (!checklistScrollBar.Enabled || delta == 0)
                return;

            int step = 84;
            int next = checklistScrollBar.Value - Math.Sign(delta) * step;
            int maxValue = Math.Max(checklistScrollBar.Minimum, checklistScrollBar.Maximum - checklistScrollBar.LargeChange + 1);
            next = Math.Max(checklistScrollBar.Minimum, Math.Min(maxValue, next));
            checklistScrollBar.Value = next;
            UpdateChecklistScrollPosition();
        }

        private void UpdateChecklistScrollPosition()
        {
            checklistContentPanel.Location = new Point(0, -checklistScrollBar.Value);
        }

        private void BuildPathsTab()
        {
            Label gamePathLabel = new Label();
            gamePathLabel.Text = "Game folder:";
            gamePathLabel.AutoSize = true;
            gamePathLabel.Location = new Point(16, 28);
            pathsPage.Controls.Add(gamePathLabel);

            gamePathBox.Location = new Point(124, 24);
            gamePathBox.Size = new Size(630, 24);
            gamePathBox.ReadOnly = true;
            pathsPage.Controls.Add(gamePathBox);

            gamePathBrowseButton.Text = "Browse...";
            gamePathBrowseButton.Location = new Point(766, 22);
            gamePathBrowseButton.Size = new Size(84, 28);
            pathsPage.Controls.Add(gamePathBrowseButton);

            gamePathStatusLabel.Location = new Point(858, 27);
            gamePathStatusLabel.Size = new Size(88, 20);
            pathsPage.Controls.Add(gamePathStatusLabel);

            Label settingsPathLabel = new Label();
            settingsPathLabel.Text = "Settings/saves:";
            settingsPathLabel.AutoSize = true;
            settingsPathLabel.Location = new Point(16, 64);
            pathsPage.Controls.Add(settingsPathLabel);

            settingsPathBox.Location = new Point(124, 60);
            settingsPathBox.Size = new Size(630, 24);
            settingsPathBox.ReadOnly = true;
            pathsPage.Controls.Add(settingsPathBox);

            settingsPathBrowseButton.Text = "Browse...";
            settingsPathBrowseButton.Location = new Point(766, 58);
            settingsPathBrowseButton.Size = new Size(84, 28);
            pathsPage.Controls.Add(settingsPathBrowseButton);

            settingsPathStatusLabel.Location = new Point(858, 63);
            settingsPathStatusLabel.Size = new Size(88, 20);
            pathsPage.Controls.Add(settingsPathStatusLabel);

            resetPathsButton.Text = "Auto-detect paths";
            resetPathsButton.Location = new Point(124, 100);
            resetPathsButton.Size = new Size(140, 32);
            pathsPage.Controls.Add(resetPathsButton);

            openGamePathButton.Text = "Open game";
            openGamePathButton.Location = new Point(278, 100);
            openGamePathButton.Size = new Size(100, 32);
            pathsPage.Controls.Add(openGamePathButton);

            openSettingsPathButton.Text = "Open settings";
            openSettingsPathButton.Location = new Point(390, 100);
            openSettingsPathButton.Size = new Size(112, 32);
            pathsPage.Controls.Add(openSettingsPathButton);

            Label pathsHint = new Label();
            pathsHint.Text = "Game folder must contain binaries\\ck3.exe. Settings/saves should be the Crusader Kings III folder under Documents.";
            pathsHint.Location = new Point(124, 148);
            pathsHint.Size = new Size(720, 44);
            pathsHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            pathsHint.ForeColor = Muted;
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

        private void BuildWorkflowTab()
        {
            ConfigureSurfacePanel(workflowHeaderPanel);
            ConfigureSurfacePanel(workflowStatusPanel);
            ConfigureSurfacePanel(workflowStepsPanel);
            ConfigureSurfacePanel(workflowSummaryPanel);

            workflowPage.Controls.Add(workflowHeaderPanel);
            workflowPage.Controls.Add(workflowStatusPanel);
            workflowPage.Controls.Add(workflowStepsPanel);
            workflowPage.Controls.Add(workflowSummaryPanel);

            workflowModeLabel.Text = "Scenario:";
            workflowModeLabel.AutoSize = true;
            workflowModeLabel.ForeColor = Color.FromArgb(70, 78, 92);
            workflowHeaderPanel.Controls.Add(workflowModeLabel);

            workflowModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            workflowModeBox.Items.AddRange(new object[] { "Start Session", "After OOS", "Rehost", "Hotjoin" });
            workflowHeaderPanel.Controls.Add(workflowModeBox);

            workflowSaveLabel.Text = "Host save:";
            workflowSaveLabel.AutoSize = true;
            workflowSaveLabel.ForeColor = Color.FromArgb(70, 78, 92);
            workflowHeaderPanel.Controls.Add(workflowSaveLabel);

            workflowSaveBox.DropDownStyle = ComboBoxStyle.DropDownList;
            workflowSaveBox.Items.AddRange(new object[]
            {
                "autosave.ck3 | 2026-07-16 12:20",
                "campaign.ck3 | 2026-07-15 22:44"
            });
            workflowHeaderPanel.Controls.Add(workflowSaveBox);

            workflowSaveBrowseButton.Text = "Browse...";
            workflowHeaderPanel.Controls.Add(workflowSaveBrowseButton);

            workflowApplySafeStartButton.Text = "Fix host";
            workflowHeaderPanel.Controls.Add(workflowApplySafeStartButton);

            workflowRepairSaveButton.Text = "Fix save";
            workflowHeaderPanel.Controls.Add(workflowRepairSaveButton);

            workflowCompareParityButton.Text = "Compare parity";
            workflowHeaderPanel.Controls.Add(workflowCompareParityButton);

            workflowParityRoomButton.Text = "Parity room";
            workflowHeaderPanel.Controls.Add(workflowParityRoomButton);

            workflowMoreButton.Text = "More";
            workflowHeaderPanel.Controls.Add(workflowMoreButton);

            workflowVerdictLabel.Text = "Status: ready for a clean start.";
            workflowVerdictLabel.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            workflowVerdictLabel.AutoSize = false;
            workflowStatusPanel.Controls.Add(workflowVerdictLabel);

            workflowHintLabel.Text = "Everything refreshes automatically. Fix red items first, then follow the manual steps below.";
            workflowHintLabel.AutoSize = false;
            workflowHintLabel.ForeColor = Muted;
            workflowStatusPanel.Controls.Add(workflowHintLabel);

            workflowStatusAccentPanel.Enabled = false;
            workflowStatusAccentPanel.BackColor = Green;
            workflowStatusPanel.Controls.Add(workflowStatusAccentPanel);

            workflowStepsLabel.Text = "What to do";
            workflowStepsLabel.AutoSize = true;
            workflowStepsLabel.Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold);
            workflowStepsLabel.ForeColor = TextColor;
            workflowStepsPanel.Controls.Add(workflowStepsLabel);

            workflowStepsListBox.IntegralHeight = false;
            workflowStepsListBox.BorderStyle = BorderStyle.None;
            workflowStepsListBox.BackColor = Color.White;
            workflowStepsListBox.Items.AddRange(new object[]
            {
                "✓ Confirm every player uses the same CK3 version.",
                "✓ Share and compare parity manifests.",
                "• Select the clean host save.",
                "• Start the session and wait one in-game day."
            });
            workflowStepsPanel.Controls.Add(workflowStepsListBox);

            workflowSummaryLabel.Text = "Details";
            workflowSummaryLabel.AutoSize = true;
            workflowSummaryLabel.Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold);
            workflowSummaryLabel.ForeColor = TextColor;
            workflowSummaryPanel.Controls.Add(workflowSummaryLabel);

            ConfigureLogView(workflowSummaryBox);
            workflowSummaryBox.BorderStyle = BorderStyle.None;
            workflowSummaryBox.WordWrap = true;
            workflowSummaryBox.ScrollBars = RichTextBoxScrollBars.Vertical;
            workflowSummaryPanel.Controls.Add(workflowSummaryBox);

            StyleButton(workflowApplySafeStartButton, ButtonKind.Primary);
            StyleButton(workflowRepairSaveButton, ButtonKind.Secondary);
            StyleButton(workflowCompareParityButton, ButtonKind.Secondary);
            StyleButton(workflowParityRoomButton, ButtonKind.Secondary);
            StyleButton(workflowMoreButton, ButtonKind.Secondary);
            StyleButton(workflowSaveBrowseButton, ButtonKind.Secondary);

            workflowPage.Resize += delegate { LayoutWorkflowTabControls(); };
            LayoutWorkflowTabControls();
        }

        private void LayoutWorkflowTabControls()
        {
            int left = 18;
            int top = 16;
            int gap = 14;
            int rightPadding = 18;
            int contentWidth = Math.Max(760, workflowPage.ClientSize.Width - left - rightPadding);
            int headerHeight = 96;
            int statusHeight = 86;
            int cardsTop = top + headerHeight + gap + statusHeight + gap;
            int cardsHeight = Math.Max(260, workflowPage.ClientSize.Height - cardsTop - 16);
            int leftColumnWidth = Math.Max(320, (int)(contentWidth * 0.43));
            int rightColumnWidth = contentWidth - leftColumnWidth - gap;
            int buttonHeight = 34;

            workflowHeaderPanel.Location = new Point(left, top);
            workflowHeaderPanel.Size = new Size(contentWidth, headerHeight);

            workflowStatusPanel.Location = new Point(left, workflowHeaderPanel.Bottom + gap);
            workflowStatusPanel.Size = new Size(contentWidth, statusHeight);
            workflowStatusAccentPanel.Location = new Point(0, 0);
            workflowStatusAccentPanel.Size = new Size(6, workflowStatusPanel.ClientSize.Height);

            workflowStepsPanel.Location = new Point(left, cardsTop);
            workflowStepsPanel.Size = new Size(leftColumnWidth, cardsHeight);

            workflowSummaryPanel.Location = new Point(workflowStepsPanel.Right + gap, cardsTop);
            workflowSummaryPanel.Size = new Size(rightColumnWidth, cardsHeight);

            workflowModeLabel.Location = new Point(16, 16);
            workflowModeBox.Location = new Point(88, 12);
            workflowModeBox.Size = new Size(186, 28);

            int actionsLeft = workflowModeBox.Right + 16;
            int actionGap = 8;
            workflowApplySafeStartButton.Location = new Point(actionsLeft, 11);
            workflowApplySafeStartButton.Size = new Size(104, buttonHeight);
            workflowRepairSaveButton.Location = new Point(workflowApplySafeStartButton.Right + actionGap, 11);
            workflowRepairSaveButton.Size = new Size(96, buttonHeight);
            workflowCompareParityButton.Location = new Point(workflowRepairSaveButton.Right + actionGap, 11);
            workflowCompareParityButton.Size = new Size(116, buttonHeight);
            workflowParityRoomButton.Location = new Point(workflowCompareParityButton.Right + actionGap, 11);
            workflowParityRoomButton.Size = new Size(96, buttonHeight);
            workflowMoreButton.Location = new Point(workflowParityRoomButton.Right + actionGap, 11);
            workflowMoreButton.Size = new Size(78, buttonHeight);

            int secondRowTop = 54;
            workflowSaveLabel.Location = new Point(16, secondRowTop + 5);
            int saveLeft = 88;
            int saveButtonsWidth = 88;
            int saveAvailableWidth = workflowHeaderPanel.ClientSize.Width - saveLeft - saveButtonsWidth - 28;
            int saveBoxWidth = Math.Max(200, saveAvailableWidth);
            workflowSaveBox.Location = new Point(saveLeft, secondRowTop + 1);
            workflowSaveBox.Size = new Size(saveBoxWidth, 24);
            workflowSaveBrowseButton.Location = new Point(workflowSaveBox.Right + 10, secondRowTop - 1);
            workflowSaveBrowseButton.Size = new Size(88, 28);

            workflowVerdictLabel.Location = new Point(20, 13);
            workflowVerdictLabel.Size = new Size(workflowStatusPanel.ClientSize.Width - 40, 24);
            workflowHintLabel.Location = new Point(20, 42);
            workflowHintLabel.Size = new Size(workflowStatusPanel.ClientSize.Width - 40, 18);

            workflowStepsLabel.Location = new Point(16, 14);
            workflowStepsListBox.Location = new Point(12, 40);
            workflowStepsListBox.Size = new Size(workflowStepsPanel.ClientSize.Width - 24, workflowStepsPanel.ClientSize.Height - 52);

            workflowSummaryLabel.Location = new Point(16, 14);
            workflowSummaryBox.Location = new Point(12, 40);
            workflowSummaryBox.Size = new Size(workflowSummaryPanel.ClientSize.Width - 24, workflowSummaryPanel.ClientSize.Height - 52);
        }

        private void BuildReportsTab()
        {
            openReportsButton.Text = "Open reports";
            openReportsButton.Location = new Point(16, 18);
            openReportsButton.Size = new Size(130, 34);
            reportsPage.Controls.Add(openReportsButton);

            exportSupportButton.Text = "Export support package";
            exportSupportButton.Location = new Point(160, 18);
            exportSupportButton.Size = new Size(170, 34);
            reportsPage.Controls.Add(exportSupportButton);

            refreshHistoryButton.Text = "Refresh history";
            refreshHistoryButton.Location = new Point(344, 18);
            refreshHistoryButton.Size = new Size(130, 34);
            reportsPage.Controls.Add(refreshHistoryButton);

            clearReportsButton.Text = "Clear reports";
            clearReportsButton.Location = new Point(488, 18);
            clearReportsButton.Size = new Size(130, 34);
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
            restorePage.Controls.Add(refreshRestoreButton);

            restoreSelectedButton.Text = "Restore selected";
            restorePage.Controls.Add(restoreSelectedButton);

            restoreDefaultButton.Text = "Restore default";
            restorePage.Controls.Add(restoreDefaultButton);

            deleteRestoreButton.Text = "Delete selected";
            restorePage.Controls.Add(deleteRestoreButton);

            openQuarantineButton.Text = "Open quarantine";
            restorePage.Controls.Add(openQuarantineButton);

            restoreRunLabel.Text = "Run:";
            restoreRunLabel.AutoSize = true;
            restorePage.Controls.Add(restoreRunLabel);

            restoreRunBox.DropDownStyle = ComboBoxStyle.DropDownList;
            restoreRunBox.Items.AddRange(new object[] { "Latest run", "2026-07-15 22:41", "All runs" });
            restorePage.Controls.Add(restoreRunBox);

            restoreSortLabel.Text = "Sort:";
            restoreSortLabel.AutoSize = true;
            restorePage.Controls.Add(restoreSortLabel);

            restoreSortBox.DropDownStyle = ComboBoxStyle.DropDownList;
            restoreSortBox.Items.AddRange(new object[] { "Created", "Run", "Status", "Type", "Description", "Original path", "Backup path" });
            restorePage.Controls.Add(restoreSortBox);

            restoreSortDirectionBox.DropDownStyle = ComboBoxStyle.DropDownList;
            restoreSortDirectionBox.Items.AddRange(new object[] { "Newest first", "Oldest first" });
            restorePage.Controls.Add(restoreSortDirectionBox);

            restoreSelectAllBox.Text = "Select all visible";
            restoreSelectAllBox.AutoSize = true;
            restorePage.Controls.Add(restoreSelectAllBox);

            restoreListBox.HorizontalScrollbar = true;
            restoreListBox.CheckOnClick = true;
            restoreListBox.IntegralHeight = false;
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
            restoreRunBox.Location = new Point(runBoxLeft, filterTop);
            restoreRunBox.Size = new Size(160, 24);

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
            advancedGeneralGroup.Controls.Add(updateOnStartupBox);

            portableModeBox.Text = "Portable mode";
            portableModeBox.Size = new Size(180, 24);
            advancedGeneralGroup.Controls.Add(portableModeBox);

            settingsGuardAutoRepairBox.Text = "Allow guard auto-repair";
            settingsGuardAutoRepairBox.Size = new Size(220, 24);
            advancedGeneralGroup.Controls.Add(settingsGuardAutoRepairBox);

            advancedLogVerbosityLabel.Text = "Log verbosity:";
            advancedLogVerbosityLabel.AutoSize = true;
            advancedGeneralGroup.Controls.Add(advancedLogVerbosityLabel);

            logVerbosityBox.DropDownStyle = ComboBoxStyle.DropDownList;
            logVerbosityBox.Items.AddRange(new object[] { "Quiet", "Normal", "Verbose" });
            logVerbosityBox.Size = new Size(130, 24);
            advancedGeneralGroup.Controls.Add(logVerbosityBox);

            advancedHintLabel.Text = "Use this page for update behavior, portable mode, settings guard mode and cleanup tasks. Restore point deletion affects only CK3MPS-created system restore points on this PC.";
            advancedHintLabel.AutoSize = false;
            advancedHintLabel.ForeColor = Muted;
            advancedGeneralGroup.Controls.Add(advancedHintLabel);

            advancedMaintenanceGroup.Text = "Maintenance";
            advancedMaintenanceGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            advancedPage.Controls.Add(advancedMaintenanceGroup);

            updateButton.Text = "Check updates";
            updateButton.Size = new Size(130, 34);
            advancedMaintenanceGroup.Controls.Add(updateButton);

            updateDownloadProgress.Size = new Size(280, 22);
            advancedMaintenanceGroup.Controls.Add(updateDownloadProgress);

            advancedRestoreGroup.Text = "Delete Data";
            advancedRestoreGroup.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            advancedPage.Controls.Add(advancedRestoreGroup);

            deleteSelectedRestorePointsButton.Text = "Delete selected restore points";
            deleteSelectedRestorePointsButton.Size = new Size(240, 34);
            deleteSelectedRestorePointsButton.Enabled = false;
            advancedRestoreGroup.Controls.Add(deleteSelectedRestorePointsButton);

            clearOtherLogsButton.Text = "Delete other logs";
            clearOtherLogsButton.Size = new Size(180, 34);
            advancedRestoreGroup.Controls.Add(clearOtherLogsButton);

            clearQuarantineButton.Text = "Delete quarantine files";
            clearQuarantineButton.Size = new Size(180, 34);
            advancedRestoreGroup.Controls.Add(clearQuarantineButton);

            restorePointsLabel.Text = "Restore Points";
            restorePointsLabel.AutoSize = true;
            advancedRestoreGroup.Controls.Add(restorePointsLabel);

            restorePointsListBox.CheckOnClick = true;
            restorePointsListBox.HorizontalScrollbar = true;
            restorePointsListBox.IntegralHeight = false;
            advancedRestoreGroup.Controls.Add(restorePointsListBox);

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

        private void PopulatePreviewData()
        {
            presetBox.SelectedItem = "Recommended";
            graphicsProfileBox.SelectedItem = "Stability Low";

            logBox.Text =
                "READY  CK3MPS style preview loaded.\r\n" +
                "INFO   This window mirrors the production layout.\r\n" +
                "INFO   Controls are disabled from performing system actions.\r\n" +
                "NEXT   Review the visual styling across each tab.";

            gamePathBox.Text = @"C:\Program Files (x86)\Steam\steamapps\common\Crusader Kings III";
            settingsPathBox.Text = @"C:\Users\Player\Documents\Paradox Interactive\Crusader Kings III";
            gamePathStatusLabel.Text = "OK";
            settingsPathStatusLabel.Text = "OK";
            pathDetailsLabel.Text =
                "Game executable: binaries\\ck3.exe found\r\n" +
                "Settings folder: valid CK3 Documents root\r\n" +
                "Saves folder: save games\r\n" +
                "Preview: no folders are read or changed.";

            workflowModeBox.SelectedItem = "Start Session";
            workflowSaveBox.SelectedIndex = 0;
            workflowSummaryBox.Text =
                "VERDICT\r\nReady for Start Session.\r\n\r\n" +
                "HOST\r\nNo blocking launcher or settings drift.\r\n\r\n" +
                "SAVE\r\nSelected save matches the installed version.\r\n\r\n" +
                "PARITY\r\nLocal fingerprint is ready to compare.";

            historyBox.Text =
                "2026-07-16 12:28  Scan             ready_no_blockers\r\n" +
                "2026-07-16 12:12  Workflow         start_session_safe\r\n" +
                "2026-07-16 11:47  Support package  exported\r\n" +
                "2026-07-15 22:41  Apply Settings   completed";

            restoreRunBox.SelectedIndex = 0;
            restoreSortBox.SelectedItem = "Created";
            restoreSortDirectionBox.SelectedItem = "Newest first";
            restoreListBox.Items.Add("Committed | launcher-v2.sqlite", true);
            restoreListBox.Items.Add("Committed | pdx_settings.txt", false);
            restoreListBox.Items.Add("Committed | dlc_load.json", false);
            restoreListBox.Items.Add("Prepared  | autosave.ck3", false);
            restoreListBox.Items.Add("Rolled back | error.log", false);
            restoreListBox.SelectedIndex = 0;
            restoreDetailsBox.Text =
                "Status: committed\r\n" +
                "Type: moved_file\r\n" +
                "Description: Launcher database moved to quarantine.\r\n\r\n" +
                "Original path:\r\nC:\\Users\\Player\\Documents\\Paradox Interactive\\Crusader Kings III\\launcher-v2.sqlite\r\n\r\n" +
                "Backup path:\r\nC:\\Users\\Player\\Documents\\Paradox Interactive\\CK3MPS\\quarantine\\launcher-v2.sqlite";

            updateOnStartupBox.Checked = true;
            logVerbosityBox.SelectedItem = "Normal";
            updateDownloadProgress.Value = 0;
            restorePointsListBox.Items.Add("CK3MPS restore point - 2026-07-16 12:12", false);
            restorePointsListBox.Items.Add("CK3MPS restore point - 2026-07-15 22:41", false);
            restorePointsListBox.Items.Add("Windows Update restore point - protected", false);
        }

        private void ApplyWorkflowTheme()
        {
            BackColor = PageBack;
            ForeColor = TextColor;
            mainTabs.BackColor = PageBack;

            foreach (TabPage page in mainTabs.TabPages)
            {
                page.BackColor = PageBack;
                page.ForeColor = TextColor;
            }

            StyleAllControls(this);

            StyleButton(stabilizeButton, ButtonKind.Primary);
            StyleButton(checkButton, ButtonKind.Secondary);
            StyleButton(exportScanReportButton, ButtonKind.Secondary);
            StyleButton(previewButton, ButtonKind.Secondary);
            StyleButton(selectAllButton, ButtonKind.Secondary);
            StyleButton(selectNoneButton, ButtonKind.Secondary);

            StyleButton(resetPathsButton, ButtonKind.Primary);
            StyleButton(gamePathBrowseButton, ButtonKind.Secondary);
            StyleButton(settingsPathBrowseButton, ButtonKind.Secondary);
            StyleButton(openGamePathButton, ButtonKind.Secondary);
            StyleButton(openSettingsPathButton, ButtonKind.Secondary);

            StyleButton(openReportsButton, ButtonKind.Primary);
            StyleButton(exportSupportButton, ButtonKind.Secondary);
            StyleButton(refreshHistoryButton, ButtonKind.Secondary);
            StyleButton(clearReportsButton, ButtonKind.Danger);

            StyleButton(refreshRestoreButton, ButtonKind.Secondary);
            StyleButton(restoreSelectedButton, ButtonKind.Primary);
            StyleButton(restoreDefaultButton, ButtonKind.Secondary);
            StyleButton(deleteRestoreButton, ButtonKind.Danger);
            StyleButton(openQuarantineButton, ButtonKind.Secondary);

            StyleButton(updateButton, ButtonKind.Primary);
            StyleButton(deleteSelectedRestorePointsButton, ButtonKind.Danger);
            StyleButton(clearOtherLogsButton, ButtonKind.Danger);
            StyleButton(clearQuarantineButton, ButtonKind.Danger);

            checklistPanel.BackColor = Surface;
            checklistPanel.BorderStyle = BorderStyle.FixedSingle;
            checklistContentPanel.BackColor = Surface;
            logBox.BackColor = Color.White;
            historyBox.BackColor = Color.White;
            restoreListBox.BackColor = Color.White;
            restoreDetailsBox.BackColor = Color.White;
            restorePointsListBox.BackColor = Color.White;

            gamePathStatusLabel.ForeColor = Green;
            gamePathStatusLabel.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            settingsPathStatusLabel.ForeColor = Green;
            settingsPathStatusLabel.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);

            graphicsSectionLabel.ForeColor = TextColor;
            liveLogLabel.ForeColor = TextColor;
            statusLabel.ForeColor = Muted;

            StyleGroupBox(advancedGeneralGroup);
            StyleGroupBox(advancedMaintenanceGroup);
            StyleGroupBox(advancedRestoreGroup);

            foreach (PreviewStepGroup group in checklistGroups)
            {
                group.Header.BackColor = Color.FromArgb(247, 248, 250);
                group.TitleLabel.ForeColor = TextColor;
                StyleButton(group.ToggleButton, ButtonKind.Secondary);
                group.ToggleButton.Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold);

                foreach (Panel row in group.Rows)
                {
                    row.BackColor = Surface;
                    foreach (Control control in row.Controls)
                    {
                        Label label = control as Label;
                        if (label != null && label.Text == "?")
                        {
                            label.BackColor = Color.White;
                            label.ForeColor = TextColor;
                        }
                    }
                }
            }
        }

        private void StyleAllControls(Control root)
        {
            foreach (Control control in root.Controls)
            {
                if (!(control is RichTextBox) && !(control is TextBox) && !(control is ListBox) && !(control is CheckedListBox))
                    control.ForeColor = TextColor;

                ComboBox combo = control as ComboBox;
                if (combo != null)
                {
                    combo.BackColor = Color.White;
                    combo.ForeColor = TextColor;
                    combo.FlatStyle = FlatStyle.Flat;
                }

                TextBox textBox = control as TextBox;
                if (textBox != null)
                {
                    textBox.BackColor = Color.White;
                    textBox.ForeColor = TextColor;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                }

                RichTextBox richTextBox = control as RichTextBox;
                if (richTextBox != null)
                {
                    richTextBox.BackColor = Color.White;
                    richTextBox.ForeColor = TextColor;
                    richTextBox.BorderStyle = BorderStyle.FixedSingle;
                }

                if (control.HasChildren)
                    StyleAllControls(control);
            }
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

        private static void ConfigureSurfacePanel(Panel panel)
        {
            panel.BackColor = Surface;
            panel.BorderStyle = BorderStyle.FixedSingle;
        }

        private void StyleGroupBox(GroupBox group)
        {
            group.BackColor = Surface;
            group.ForeColor = TextColor;
            group.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            foreach (Control control in group.Controls)
            {
                if (!(control is Label))
                    control.Font = new Font(Font.FontFamily, 9F, FontStyle.Regular);
            }
        }

        private void StyleButton(Button button, ButtonKind kind)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.Font = new Font(Font.FontFamily, 8.8F, kind == ButtonKind.Primary ? FontStyle.Bold : FontStyle.Regular);

            if (kind == ButtonKind.Primary)
            {
                button.BackColor = Blue;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(28, 93, 160);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(32, 111, 201);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(22, 90, 170);
            }
            else if (kind == ButtonKind.Danger)
            {
                button.BackColor = Color.White;
                button.ForeColor = Red;
                button.FlatAppearance.BorderColor = Color.FromArgb(210, 150, 150);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(252, 240, 240);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(248, 230, 230);
            }
            else
            {
                button.BackColor = Color.White;
                button.ForeColor = TextColor;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 243, 248);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(233, 237, 244);
            }
        }

        private sealed class PreviewStepGroup
        {
            public string Title;
            public string[] Items;
            public bool Expanded;
            public readonly Panel Header = new Panel();
            public readonly Button ToggleButton = new Button();
            public readonly CheckBox CheckBox = new CheckBox();
            public readonly Label TitleLabel = new Label();
            public readonly List<Panel> Rows = new List<Panel>();
        }

        private enum ButtonKind
        {
            Primary,
            Secondary,
            Danger
        }
    }
}
