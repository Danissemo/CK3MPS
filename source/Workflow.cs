using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private void BuildWorkflowTab()
        {
            workflowPage.BackColor = Color.FromArgb(240, 240, 240);

            ConfigureWorkflowSurfacePanel(workflowHeaderPanel, 1);
            ConfigureWorkflowSurfacePanel(workflowStatusPanel, 1);
            ConfigureWorkflowSurfacePanel(workflowStepsPanel, 1);
            ConfigureWorkflowSurfacePanel(workflowSummaryPanel, 1);

            workflowPage.Controls.Add(workflowHeaderPanel);
            workflowPage.Controls.Add(workflowStatusPanel);
            workflowPage.Controls.Add(workflowStepsPanel);
            workflowPage.Controls.Add(workflowSummaryPanel);

            workflowModeLabel.Text = "Scenario:";
            workflowModeLabel.AutoSize = true;
            workflowModeLabel.ForeColor = Color.FromArgb(70, 78, 92);
            workflowHeaderPanel.Controls.Add(workflowModeLabel);

            workflowModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            workflowModeBox.Items.AddRange(new object[]
            {
                "Start Session",
                "After OOS",
                "Rehost",
                "Hotjoin"
            });
            workflowModeBox.SelectedIndexChanged += delegate
            {
                if (workflowModeBox.SelectedItem != null)
                {
                    CancelWorkflowScenarioRefresh();
                    currentWorkflowScenario = Convert.ToString(workflowModeBox.SelectedItem);
                    LayoutWorkflowTabControls();
                    if (workflowUiInitialized)
                        EnsureWorkflowScenarioLoaded(currentWorkflowScenario);
                }
            };
            workflowHeaderPanel.Controls.Add(workflowModeBox);

            workflowSaveLabel.Text = "Host save:";
            workflowSaveLabel.AutoSize = true;
            workflowSaveLabel.ForeColor = Color.FromArgb(70, 78, 92);
            workflowHeaderPanel.Controls.Add(workflowSaveLabel);

            workflowSaveBox.DropDownStyle = ComboBoxStyle.DropDownList;
            workflowSaveBox.SelectedIndexChanged += delegate
            {
                if (updatingWorkflowUi)
                    return;

                WorkflowSaveOption selected = workflowSaveBox.SelectedItem as WorkflowSaveOption;
                workflowSelectedSavePath = selected == null ? "" : selected.Path;
                SaveAppConfig();
                InvalidateWorkflowSaveSelectionState();
            };
            workflowHeaderPanel.Controls.Add(workflowSaveBox);

            workflowSaveBrowseButton.Text = "Browse...";
            workflowSaveBrowseButton.Click += delegate { BrowseWorkflowSavePath(); };
            workflowHeaderPanel.Controls.Add(workflowSaveBrowseButton);

            workflowSaveDeleteButton.Text = "Delete";
            workflowSaveDeleteButton.Click += delegate { DeleteSelectedWorkflowSave(); };
            workflowHeaderPanel.Controls.Add(workflowSaveDeleteButton);

            workflowApplySafeStartButton.Text = "Fix host";
            workflowApplySafeStartButton.Click += delegate { ApplyWorkflowSafeStartProfile(); };
            workflowHeaderPanel.Controls.Add(workflowApplySafeStartButton);
            stepToolTip.SetToolTip(workflowApplySafeStartButton, BuildWorkflowFixHostHintText());

            workflowRepairSaveButton.Text = "Fix save";
            workflowRepairSaveButton.Click += delegate { RepairSelectedWorkflowSave(); };
            workflowHeaderPanel.Controls.Add(workflowRepairSaveButton);
            stepToolTip.SetToolTip(workflowRepairSaveButton, BuildWorkflowFixSaveHintText());

            workflowCompareParityButton.Text = "Compare parity";
            workflowCompareParityButton.Click += delegate { CompareWorkflowParity(); };
            workflowHeaderPanel.Controls.Add(workflowCompareParityButton);
            stepToolTip.SetToolTip(workflowCompareParityButton, "Compare parity files from other players against this PC and selected save.");

            workflowParityRoomButton.Text = "Parity room";
            workflowParityRoomButton.Click += delegate { OpenParityRoom(); };
            workflowHeaderPanel.Controls.Add(workflowParityRoomButton);
            stepToolTip.SetToolTip(workflowParityRoomButton, "Live local room for parity and OOS exchange. Best when several players need one shared diagnosis.");

            workflowMoreButton.Text = "More";
            workflowMoreButton.Click += delegate
            {
                workflowMoreMenu.Show(workflowMoreButton, new Point(0, workflowMoreButton.Height));
            };
            workflowHeaderPanel.Controls.Add(workflowMoreButton);

            workflowOpenSaveFolderButton.Text = "Open save folder";
            workflowOpenSaveFolderButton.Click += delegate { OpenSelectedWorkflowSaveFolder(); };

            workflowOpenOosFolderButton.Text = "Open OOS folder";
            workflowOpenOosFolderButton.Click += delegate { OpenWorkflowOosFolder(); };

            workflowDuplicateSaveButton.Text = "Duplicate save";
            workflowDuplicateSaveButton.Click += delegate { DuplicateSelectedWorkflowSave(); };

            workflowArchiveIncidentButton.Text = "Archive incident";
            workflowArchiveIncidentButton.Click += delegate { ArchiveWorkflowIncident(); };

            workflowMoreMenu.Items.Add("Open save folder", null, delegate { OpenSelectedWorkflowSaveFolder(); });
            workflowMoreMenu.Items.Add("Open OOS folder", null, delegate { OpenWorkflowOosFolder(); });
            workflowMoreMenu.Items.Add("Duplicate save", null, delegate { DuplicateSelectedWorkflowSave(); });
            workflowMoreMenu.Items.Add("Delete save", null, delegate { DeleteSelectedWorkflowSave(); });

            workflowVerdictLabel.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            workflowVerdictLabel.AutoSize = false;
            workflowVerdictLabel.BackColor = Color.Transparent;
            workflowStatusPanel.Controls.Add(workflowVerdictLabel);

            workflowHintLabel.Text = "Everything refreshes automatically. Fix red items first, then follow the manual steps below.";
            workflowHintLabel.AutoSize = false;
            workflowHintLabel.ForeColor = Color.FromArgb(90, 90, 90);
            workflowHintLabel.BackColor = Color.Transparent;
            workflowStatusPanel.Controls.Add(workflowHintLabel);

            workflowProgressBar.Style = ProgressBarStyle.Blocks;
            workflowProgressBar.Minimum = 0;
            workflowProgressBar.Visible = false;
            workflowStatusPanel.Controls.Add(workflowProgressBar);
            workflowStatusAccentPanel.Enabled = false;
            workflowStatusPanel.Controls.Add(workflowStatusAccentPanel);

            workflowStepsLabel.Text = "What to do";
            workflowStepsLabel.AutoSize = true;
            workflowStepsLabel.Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold);
            workflowStepsLabel.ForeColor = Color.FromArgb(46, 56, 72);
            workflowStepsPanel.Controls.Add(workflowStepsLabel);

            workflowStepsListBox.IntegralHeight = false;
            workflowStepsListBox.BorderStyle = BorderStyle.None;
            workflowStepsListBox.DrawMode = DrawMode.OwnerDrawFixed;
            workflowStepsListBox.ItemHeight = 24;
            workflowStepsListBox.BackColor = Color.White;
            workflowStepsListBox.DrawItem += WorkflowStepsListBox_DrawItem;
            workflowStepsPanel.Controls.Add(workflowStepsListBox);

            workflowSummaryLabel.Text = "Details";
            workflowSummaryLabel.AutoSize = true;
            workflowSummaryLabel.Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold);
            workflowSummaryLabel.ForeColor = Color.FromArgb(46, 56, 72);
            workflowSummaryPanel.Controls.Add(workflowSummaryLabel);

            ConfigureLogView(workflowSummaryBox);
            workflowSummaryBox.BorderStyle = BorderStyle.None;
            workflowSummaryBox.BackColor = Color.White;
            workflowSummaryBox.ScrollBars = RichTextBoxScrollBars.Vertical;
            workflowSummaryBox.WordWrap = true;
            workflowSummaryPanel.Controls.Add(workflowSummaryBox);

            StyleWorkflowActionButton(workflowApplySafeStartButton, true);
            StyleWorkflowActionButton(workflowRepairSaveButton, false);
            StyleWorkflowActionButton(workflowCompareParityButton, false);
            StyleWorkflowActionButton(workflowParityRoomButton, false);
            StyleWorkflowActionButton(workflowMoreButton, false);
            StyleWorkflowActionButton(workflowSaveBrowseButton, false);

            workflowPage.Resize += delegate { LayoutWorkflowTabControls(); };
            workflowRenderTimer.Interval = 35;
            workflowRenderTimer.Tick += WorkflowRenderTimer_Tick;
            updatingWorkflowUi = true;
            try
            {
                workflowModeBox.SelectedItem = currentWorkflowScenario;
                if (workflowModeBox.SelectedItem == null && workflowModeBox.Items.Count > 0)
                    workflowModeBox.SelectedIndex = 0;
            }
            finally
            {
                updatingWorkflowUi = false;
            }
            RefreshWorkflowSaveSelectionList();
            LayoutWorkflowTabControls();
            ShowWorkflowScenarioSnapshot(currentWorkflowScenario, false);
            workflowUiInitialized = true;
        }

        private void LayoutWorkflowTabControls()
        {
            int left = 18;
            int top = 16;
            int gap = 14;
            int rightPadding = 18;
            int contentWidth = Math.Max(760, workflowPage.ClientSize.Width - left - rightPadding);
            int contentHeight = Math.Max(420, workflowPage.ClientSize.Height - top - 16);
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

            workflowEvaluateButton.Visible = false;

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

            workflowRecoveryPathLabel.Visible = false;
            workflowRecoveryPathBox.Visible = false;

            workflowVerdictLabel.Location = new Point(20, 13);
            workflowVerdictLabel.Size = new Size(workflowStatusPanel.ClientSize.Width - 40, 24);

            workflowHintLabel.Location = new Point(20, 42);
            workflowHintLabel.Size = new Size(workflowStatusPanel.ClientSize.Width - 40, 18);

            workflowProgressBar.Location = new Point(20, 64);
            workflowProgressBar.Size = new Size(workflowStatusPanel.ClientSize.Width - 40, 10);

            workflowStepsLabel.Location = new Point(16, 14);
            workflowStepsListBox.Location = new Point(12, 40);
            workflowStepsListBox.Size = new Size(workflowStepsPanel.ClientSize.Width - 24, workflowStepsPanel.ClientSize.Height - 52);

            workflowSummaryLabel.Location = new Point(16, 14);
            workflowSummaryBox.Location = new Point(12, 40);
            workflowSummaryBox.Size = new Size(workflowSummaryPanel.ClientSize.Width - 24, workflowSummaryPanel.ClientSize.Height - 52);
            workflowEvaluateButton.Visible = false;
            workflowActionsLabel.Visible = false;
            workflowOpenSaveFolderButton.Visible = false;
            workflowOpenOosFolderButton.Visible = false;
            workflowDuplicateSaveButton.Visible = false;
            workflowArchiveIncidentButton.Visible = false;
            workflowSaveDeleteButton.Visible = false;
            workflowNotesLabel.Visible = false;
            workflowNotesBox.Visible = false;
            workflowCreateRehostPackButton.Visible = false;
            workflowResetButton.Visible = false;
            workflowIncidentLabel.Visible = false;
            workflowRecoveryPathLabel.Visible = false;
            workflowRecoveryPathBox.Visible = false;
        }

        private void ConfigureWorkflowSurfacePanel(Panel panel, int borderWidth)
        {
            panel.BackColor = Color.FromArgb(252, 252, 252);
            panel.BorderStyle = borderWidth > 0 ? BorderStyle.FixedSingle : BorderStyle.None;
        }

        private void StyleWorkflowActionButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(22, 90, 170) : Color.FromArgb(233, 237, 244);
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(32, 111, 201) : Color.FromArgb(239, 243, 248);
            button.Font = new Font(Font.FontFamily, 8.8F, primary ? FontStyle.Bold : FontStyle.Regular);
            if (primary)
            {
                button.BackColor = Color.FromArgb(34, 110, 190);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(28, 93, 160);
            }
            else
            {
                button.BackColor = Color.White;
                button.ForeColor = Color.FromArgb(52, 61, 75);
                button.FlatAppearance.BorderColor = Color.FromArgb(205, 212, 222);
            }
        }

        private void RefreshWorkflowSaveSelectionList()
        {
            List<WorkflowSaveOption> options = new List<WorkflowSaveOption>();
            foreach (string path in EnumerateHostSaveCandidates(64))
            {
                FileInfo info = new FileInfo(path);
                string display = info.Name + " | " + info.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                options.Add(new WorkflowSaveOption
                {
                    Path = path,
                    Display = display
                });
            }

            if (!String.IsNullOrWhiteSpace(workflowSelectedSavePath)
                && File.Exists(workflowSelectedSavePath)
                && !options.Exists(delegate (WorkflowSaveOption option) { return String.Equals(option.Path, workflowSelectedSavePath, StringComparison.OrdinalIgnoreCase); }))
            {
                FileInfo info = new FileInfo(workflowSelectedSavePath);
                options.Insert(0, new WorkflowSaveOption
                {
                    Path = workflowSelectedSavePath,
                    Display = info.Name + " | external/manual"
                });
            }

            updatingWorkflowUi = true;
            try
            {
                workflowSaveBox.Items.Clear();
                foreach (WorkflowSaveOption option in options)
                    workflowSaveBox.Items.Add(option);

                WorkflowSaveOption selected = options.Find(delegate (WorkflowSaveOption option)
                {
                    return String.Equals(option.Path, workflowSelectedSavePath, StringComparison.OrdinalIgnoreCase);
                });
                if (selected != null)
                    workflowSaveBox.SelectedItem = selected;
                else if (workflowSaveBox.Items.Count > 0)
                    workflowSaveBox.SelectedIndex = 0;
            }
            finally
            {
                updatingWorkflowUi = false;
            }

            WorkflowSaveOption current = workflowSaveBox.SelectedItem as WorkflowSaveOption;
            workflowSelectedSavePath = current == null ? "" : current.Path;
        }

        private bool WorkflowUsesManualSaveSelection()
        {
            return true;
        }

        private void InvalidateWorkflowSaveSelectionState()
        {
            RefreshWorkflowSaveSelectionList();
            SaveAppConfig();
            ClearWorkflowScenarioSnapshots();
            InvalidateHostSaveAnalysisCache();
            if (workflowUiInitialized)
                BeginWorkflowScenarioRefresh();
        }

        private void ClearWorkflowScenarioSnapshots()
        {
            lock (workflowScenarioSnapshots)
                workflowScenarioSnapshots.Clear();
        }

        private void BrowseWorkflowSavePath()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                string saveDir = Path.Combine(ck3Docs, "save games");
                dialog.Filter = "CK3 saves (*.ck3)|*.ck3";
                dialog.Title = "Select host save for workflow";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (Directory.Exists(saveDir))
                    dialog.InitialDirectory = saveDir;
                if (!String.IsNullOrWhiteSpace(workflowSelectedSavePath) && File.Exists(workflowSelectedSavePath))
                    dialog.FileName = workflowSelectedSavePath;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                workflowSelectedSavePath = dialog.FileName;
                InvalidateWorkflowSaveSelectionState();
            }
        }

        private string WorkflowManagedSaveRoot()
        {
            return Path.Combine(ck3Docs ?? "", "save games");
        }

        private bool TryGetManagedWorkflowSavePath(string path, out string normalizedPath, out string reason)
        {
            normalizedPath = "";
            reason = "";
            if (String.IsNullOrWhiteSpace(path))
            {
                reason = "No save is selected.";
                return false;
            }

            if (!PathContainmentUtilities.TryNormalizeAbsolutePath(path, out normalizedPath))
            {
                reason = "The selected save path is malformed or unsupported.";
                return false;
            }

            string saveRoot = WorkflowManagedSaveRoot();
            if (!PathContainmentUtilities.IsManagedSaveFilePath(saveRoot, normalizedPath))
            {
                reason = "Managed workflow actions are only allowed for regular .ck3 files inside the CK3 save-games folder.";
                return false;
            }

            if (!File.Exists(normalizedPath))
            {
                reason = "The selected save file does not exist.";
                return false;
            }

            return true;
        }

        private void DeleteSelectedWorkflowSave()
        {
            WorkflowSaveOption selected = workflowSaveBox.SelectedItem as WorkflowSaveOption;
            string path = selected == null ? workflowSelectedSavePath : selected.Path;
            string normalizedPath;
            string reason;
            if (!TryGetManagedWorkflowSavePath(path, out normalizedPath, out reason))
            {
                MessageBox.Show(reason, "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string fileName = Path.GetFileName(normalizedPath);
            DialogResult confirm = MessageBox.Show(
                "Move this save to CK3MPS quarantine?\r\n\r\n" + fileName + "\r\n" + normalizedPath,
                "CK3MPS workflow",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            try
            {
                if (String.IsNullOrEmpty(lastQuarantine) || !Directory.Exists(lastQuarantine))
                    CreateQuarantine();
                string quarantinedPath;
                QuarantineWorkflowSaveTransactional(normalizedPath, out quarantinedPath);

                Log("OK   Quarantined workflow host save: " + normalizedPath);
                if (String.Equals(workflowSelectedSavePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                    workflowSelectedSavePath = "";
                SaveAppConfig();
                InvalidateWorkflowSaveSelectionState();
                SetStatusText("Quarantined workflow host save: " + fileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not quarantine the selected save.\r\n\r\n" + ex.Message, "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("WARN Could not quarantine workflow host save: " + normalizedPath + " | " + ex.Message);
            }
        }

        private void QuarantineWorkflowSaveTransactional(string normalizedPath, out string quarantinedPath)
        {
            MutationAudit.RecordMutation("file-quarantine", normalizedPath);
            quarantinedPath = "";
            string revalidatedPath;
            string validationReason;
            if (!TryGetManagedWorkflowSavePath(normalizedPath, out revalidatedPath, out validationReason)
                || !String.Equals(revalidatedPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Workflow save changed after confirmation. " + validationReason);
            FileInfo sourceInfo = new FileInfo(normalizedPath);
            if (!sourceInfo.Exists)
                throw new FileNotFoundException("Workflow save is missing.", normalizedPath);
            string sourceHash = FileHashOrMissing(normalizedPath);
            if (String.IsNullOrWhiteSpace(sourceHash) || String.Equals(sourceHash, "(missing)", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Workflow save hash could not be captured before quarantine.");

            string operationId = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N");
            string targetDir = Path.Combine(RestoreBackupRoot(), "workflow_saves", operationId);
            Directory.CreateDirectory(targetDir);
            string destinationPath = Path.Combine(targetDir, sourceInfo.Name);
            ThrowIfWorkflowFaultInjected("prepared");
            string entryId = RecordRestoreEntry(
                "moved_file",
                normalizedPath,
                destinationPath,
                "Workflow save moved to quarantine: " + normalizedPath,
                "sha256:" + sourceHash,
                "Prepared workflow quarantine move.",
                "prepared");

            bool moved = false;
            try
            {
                ThrowIfWorkflowFaultInjected("move");
                File.Move(normalizedPath, destinationPath);
                moved = true;

                FileInfo destinationInfo = new FileInfo(destinationPath);
                if (!destinationInfo.Exists)
                    throw new IOException("Workflow save quarantine destination was not created.");
                if (File.Exists(normalizedPath))
                    throw new IOException("Workflow save still exists at the original path after quarantine move.");
                if (sourceInfo.Length != destinationInfo.Length)
                    throw new IOException("Workflow save size changed during quarantine move.");
                if (!String.Equals(sourceHash, FileHashOrMissing(destinationPath), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Workflow save hash changed during quarantine move.");

                ThrowIfWorkflowFaultInjected("commit");
                UpdateRestoreEntryStatus(entryId, "committed");
                quarantinedPath = destinationPath;
            }
            catch
            {
                if (moved && File.Exists(destinationPath) && !File.Exists(normalizedPath))
                {
                    try
                    {
                        File.Move(destinationPath, normalizedPath);
                        UpdateRestoreEntryStatus(entryId, "rolled_back");
                    }
                    catch
                    {
                        try { UpdateRestoreEntryStatus(entryId, "failed"); } catch { }
                    }
                }
                else
                {
                    try { UpdateRestoreEntryStatus(entryId, "failed"); } catch { }
                }

                throw;
            }
        }

        private void ThrowIfWorkflowFaultInjected(string stage)
        {
            string requested = Environment.GetEnvironmentVariable("CK3MPS_TEST_WORKFLOW_FAULT");
            if (!String.Equals(requested, stage, StringComparison.OrdinalIgnoreCase))
                return;

            throw new IOException("Injected workflow fault at stage: " + stage);
        }

        private void WorkflowRenderTimer_Tick(object sender, EventArgs e)
        {
            if (workflowRenderIndex < workflowRenderStates.Count)
            {
                WorkflowStepState state = workflowRenderStates[workflowRenderIndex];
                if (workflowRenderIndex < workflowStepStates.Count)
                    workflowStepStates[workflowRenderIndex] = state;
                updatingWorkflowUi = true;
                try
                {
                    workflowStepsListBox.Items.Add(FormatWorkflowStepText(state));
                }
                finally
                {
                    updatingWorkflowUi = false;
                }

                workflowRenderIndex++;
                workflowProgressBar.Value = Math.Min(workflowProgressBar.Maximum, workflowRenderIndex);
                return;
            }

            workflowRenderTimer.Stop();
            workflowSummaryBox.Text = workflowRenderSummary;
            workflowVerdictLabel.Text = workflowRenderVerdict;
            ApplyWorkflowVerdictStyle(workflowRenderVerdict);
            ApplyWorkflowSummaryStyling();
            workflowProgressBar.Value = workflowProgressBar.Maximum;
            workflowProgressBar.Visible = false;
            workflowRefreshPending = false;
            WriteWorkflowStatusReport();
        }

        private void ShowWorkflowSnapshot()
        {
            RefreshWorkflowSaveSelectionList();
            EnsureWorkflowScenarioLoaded(currentWorkflowScenario);
        }

        private void ShowWorkflowScenarioSnapshot(string scenario, bool allowRefreshFallback)
        {
            WorkflowScenarioSnapshot snapshot;
            if (TryGetWorkflowScenarioSnapshot(scenario, out snapshot))
            {
                ApplyWorkflowScenarioSnapshot(snapshot);
                return;
            }

            if (allowRefreshFallback)
            {
                BeginWorkflowScenarioRefresh();
                return;
            }

            string text = "";
            try
            {
                string path = StabilizerFile("ck3_stabilizer_workflow_status.txt");
                if (File.Exists(path))
                    text = File.ReadAllText(path);
            }
            catch
            {
                text = "";
            }

            if (String.IsNullOrWhiteSpace(text))
            {
                workflowVerdictLabel.Text = "Status: waiting for automatic checks.";
                ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
                workflowSummaryBox.Text = "Choose a scenario and save. Workflow will refresh automatically.";
                ApplyWorkflowSummaryStyling();
                return;
            }

            workflowVerdictLabel.Text = "Status: last saved workflow snapshot loaded.";
            ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
            workflowSummaryBox.Text = text;
            ApplyWorkflowSummaryStyling();
        }

        private void EnsureWorkflowScenarioLoaded(string scenario)
        {
            ShowWorkflowScenarioSnapshot(scenario, false);

            WorkflowScenarioSnapshot snapshot;
            if (!TryGetWorkflowScenarioSnapshot(scenario, out snapshot))
                BeginInvoke((MethodInvoker)delegate { BeginWorkflowScenarioRefresh(); });
        }

        private bool TryGetWorkflowScenarioSnapshot(string scenario, out WorkflowScenarioSnapshot snapshot)
        {
            lock (workflowScenarioSnapshots)
                return workflowScenarioSnapshots.TryGetValue(scenario, out snapshot);
        }

        private void StoreWorkflowScenarioSnapshot(WorkflowScenarioSnapshot snapshot)
        {
            if (snapshot == null || String.IsNullOrEmpty(snapshot.Scenario))
                return;

            lock (workflowScenarioSnapshots)
                workflowScenarioSnapshots[snapshot.Scenario] = snapshot;
        }

        private void ApplyWorkflowScenarioSnapshot(WorkflowScenarioSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            workflowRenderTimer.Stop();
            workflowRenderStates = snapshot.States ?? new List<WorkflowStepState>();
            workflowRenderVerdict = snapshot.Verdict ?? "";
            workflowRenderSummary = snapshot.Summary ?? "";
            workflowRenderIndex = 0;
            workflowStepStates.Clear();
            workflowStepStates.AddRange(workflowRenderStates);

            updatingWorkflowUi = true;
            try
            {
                workflowStepsListBox.Items.Clear();
            }
            finally
            {
                updatingWorkflowUi = false;
            }

            workflowProgressBar.Visible = false;
            workflowVerdictLabel.Text = String.IsNullOrEmpty(workflowRenderVerdict)
                ? "Status: waiting for automatic checks."
                : workflowRenderVerdict;
            ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
            workflowSummaryBox.Text = "Scenario snapshot loaded.";
            ApplyWorkflowSummaryStyling();
            UpdateWorkflowActionAvailability();
            workflowProgressBar.Maximum = Math.Max(1, workflowRenderStates.Count);
            workflowProgressBar.Value = 0;
            workflowRenderTimer.Start();
        }

        private void BeginWorkflowScenarioRefresh()
        {
            if (!workflowUiInitialized)
                return;

            workflowRefreshPending = true;
            workflowLoadGeneration++;
            int generation = workflowLoadGeneration;
            string scenario = currentWorkflowScenario = NullText(Convert.ToString(workflowModeBox.SelectedItem)) == "(none)"
                ? currentWorkflowScenario
                : Convert.ToString(workflowModeBox.SelectedItem);
            CancellationToken cancellationToken = BeginWorkflowRefreshCancellation();

            workflowRenderTimer.Stop();
            updatingWorkflowUi = true;
            try
            {
                workflowStepsListBox.Items.Clear();
            }
            finally
            {
                updatingWorkflowUi = false;
            }

            workflowVerdictLabel.Text = "Status: checking your setup...";
            ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
            workflowSummaryBox.Text = "Loading workflow checks for " + scenario + "...";
            ApplyWorkflowSummaryStyling();
            workflowProgressBar.Value = 0;
            workflowProgressBar.Maximum = 1;
            workflowProgressBar.Visible = true;

            Task.Run(delegate
            {
                try
                {
                    WorkflowScenarioSnapshot snapshot = BuildWorkflowScenarioSnapshotCore(scenario, cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!WorkflowRefreshStillCurrent(generation, scenario, cancellationToken))
                            return;

                        workflowRenderStates = snapshot.States ?? new List<WorkflowStepState>();
                        workflowRenderVerdict = snapshot.Verdict;
                        workflowRenderSummary = snapshot.Summary;
                        workflowRenderIndex = 0;
                        workflowStepStates.Clear();
                        workflowStepStates.AddRange(workflowRenderStates);
                        StoreWorkflowScenarioSnapshot(snapshot);
                        workflowProgressBar.Maximum = Math.Max(1, workflowRenderStates.Count);
                        workflowProgressBar.Value = 0;
                        workflowProgressBar.Visible = true;
                        workflowRenderTimer.Start();
                    });
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!WorkflowRefreshStillCurrent(generation, scenario, cancellationToken))
                            return;

                        workflowRenderTimer.Stop();
                        workflowProgressBar.Visible = false;
                        workflowVerdictLabel.Text = "Status: workflow refresh failed.";
                        ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
                        workflowSummaryBox.Text = "Workflow checks could not be loaded for the selected scenario.";
                        ApplyWorkflowSummaryStyling();
                        workflowRefreshPending = false;
                    });
                }
            }, cancellationToken);
        }

        private void RebuildWorkflowScenarioUi()
        {
            currentWorkflowScenario = NullText(Convert.ToString(workflowModeBox.SelectedItem)) == "(none)"
                ? currentWorkflowScenario
                : Convert.ToString(workflowModeBox.SelectedItem);

            workflowStepStates.Clear();
            BuildWorkflowScenarioSteps(currentWorkflowScenario, workflowStepStates);

            updatingWorkflowUi = true;
            try
            {
                workflowStepsListBox.Items.Clear();
                foreach (WorkflowStepState state in workflowStepStates)
                {
                    workflowStepsListBox.Items.Add(FormatWorkflowStepText(state));
                }
            }
            finally
            {
                updatingWorkflowUi = false;
            }

            workflowSummaryBox.Text = BuildWorkflowScenarioSummaryText(currentWorkflowScenario, workflowStepStates);
            workflowVerdictLabel.Text = BuildWorkflowVerdictLine(currentWorkflowScenario, workflowStepStates);
            ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
            ApplyWorkflowSummaryStyling();
            UpdateWorkflowActionAvailability();
            WriteWorkflowStatusReport();
        }

        private WorkflowScenarioSnapshot BuildWorkflowScenarioSnapshot(string scenario)
        {
            return BuildWorkflowScenarioSnapshotCore(scenario, CancellationToken.None);
        }

        private void BuildWorkflowScenarioSteps(string scenario, List<WorkflowStepState> states)
        {
            WorkflowAnalysisSnapshot analysis = CurrentWorkflowAnalysis();
            HostSuitabilityResult host = analysis.Host;
            HostSaveCandidateResult save = analysis.Save;
            OosDeepInsight oos = analysis.Oos;
            OosIncidentState incident = analysis.Incident;
            bool saveRulesSafe = AllCriticalRulesSafe(save.Save.Rules);
            string latestOosPath = FindLatestOosMetadataFile();
            bool latestOosExists = !String.IsNullOrEmpty(latestOosPath);
            bool activeOosFolderClean = CountDirectories(Path.Combine(ck3Docs, "oos")) == 0;
            bool stableProfile = StableCriticalSettingsOk() && NoActiveMods() && NoDisabledDlcs() && HasNoAsync() && !HasRiskyLaunchOptions();
            bool parityReady = ParityManifestComplete() && VersionParityBaselineOk() && String.Equals(DetectSteamBranch(), "public", StringComparison.OrdinalIgnoreCase);
            bool selectedSaveExists = !String.IsNullOrWhiteSpace(workflowSelectedSavePath) && File.Exists(workflowSelectedSavePath);

            if (String.Equals(scenario, "Start Session", StringComparison.OrdinalIgnoreCase))
            {
                AddWorkflowStep(states, scenario, "start_profile", "Automatic: host profile is stable.", "Checks -noasync, no risky flags, stable pdx_settings, no mods, no disabled DLCs.", "Before start", true, false, stableProfile, !stableProfile);
                AddWorkflowStep(states, scenario, "start_host_save", "Automatic: selected save is ready.", "Requires a readable local manual save with acceptable score.", "Before start", true, false, save.Score >= 70, save.Score < 70);
                AddWorkflowStep(states, scenario, "start_rules", "Automatic: critical save rules are safe.", "Checks murder schemes, landless adventurers, Great Steppe, earthquakes, floods.", "Before start", true, false, saveRulesSafe, !saveRulesSafe);
                AddWorkflowStep(states, scenario, "start_host_suitability", "Automatic: host PC and internet are ready.", "Uses route, jitter, packet loss, branch, mod/profile and service checks.", "Before start", true, false, host.Suitable, !host.Suitable);
                AddWorkflowStep(states, scenario, "start_load_game", "Manual: use Load Game, not Continue.", "Prevents starting from autosave/recovery/desync-like baselines.", "Before start", true, true, false, false);
                AddWorkflowStep(states, scenario, "start_compare_parity", "Manual: run Compare parity or Parity room.", "Do this before the host creates the lobby so every player matches host files, options and save.", "Before start", true, true, false, false);
                AddWorkflowStep(states, scenario, "start_wait_lobby", "Manual: everyone joins before unpause.", "No hotjoin into a running simulation.", "In lobby", true, true, false, false);
                AddWorkflowStep(states, scenario, "start_speed1", "Manual: keep speed 1-2 at start.", "The first month after loading is the riskiest phase.", "In game", true, true, false, false);
                return;
            }

            if (String.Equals(scenario, "After OOS", StringComparison.OrdinalIgnoreCase))
            {
                AddWorkflowStep(states, scenario, "oos_metadata", "Automatic: latest OOS data is available.", "If absent, collect fresh host/client OOS data before retrying.", "Do now", true, false, latestOosExists, !latestOosExists);
                AddWorkflowStep(states, scenario, "oos_parser", "Automatic: deep OOS parser built a recovery path.", "Parses .oos dumps, error log, contamination score and hotjoin safety.", "Do now", true, false, latestOosExists && !String.IsNullOrWhiteSpace(oos.RecoveryPath), !(latestOosExists && !String.IsNullOrWhiteSpace(oos.RecoveryPath)));
                AddWorkflowStep(states, scenario, "oos_incident_state", "Automatic: incident state is consistent.", "Tracks stage, escalation, blocked actions and confidence across repeated OOS.", "Do now", true, false, latestOosExists && !String.IsNullOrWhiteSpace(incident.Stage), !(latestOosExists && !String.IsNullOrWhiteSpace(incident.Stage)));
                AddWorkflowStep(states, scenario, "oos_stop", "Manual: stop the session immediately.", "Do not keep playing through a desynced state.", "Do now", true, true, false, false);
                AddWorkflowStep(states, scenario, "oos_collect", "Manual: collect host and client OOS data.", "Use Send OOS data or incident pack before retrying, so the host sees the same evidence from everyone.", "Do now", true, true, false, false);
                AddWorkflowStep(states, scenario, "oos_compare", "Manual: compare parity and OOS in one place.", "Use Compare parity for files or Parity room for live players, then decide whether the issue is host profile, save, or rollback.", "Do now", true, true, false, false);
                AddWorkflowStep(states, scenario, "oos_decide", "Manual: choose rollback, rehost or controlled hotjoin.", "Follow the parser recommendation before anyone resumes or rejoins.", "Do now", true, true, false, false);
                AddWorkflowStep(states, scenario, "oos_profile", "Automatic: local profile is still stable.", "Checks version parity, branch, launch options and mod/DLC state.", "Before next launch", true, false, stableProfile && parityReady, !(stableProfile && parityReady));
                AddWorkflowStep(states, scenario, "oos_host_save", "Automatic: selected save can still be used.", "Use the selected safe manual save, not the current Continue pointer.", "Before next launch", true, false, save.Score >= 70, save.Score < 70);
                AddWorkflowStep(states, scenario, "oos_selected_save", "Automatic: selected save file still exists.", "The recovery workflow only uses the explicitly selected save.", "Before next launch", true, false, selectedSaveExists, !selectedSaveExists);
                AddWorkflowStep(states, scenario, "oos_fix_host", "Manual: run Fix host if the host profile drifted.", "Use it only after closing CK3. It repairs launcher/profile/network readiness for the next attempt.", "Before next launch", true, true, false, false);
                AddWorkflowStep(states, scenario, "oos_fix_save", "Manual: run Fix save if the selected save is unsafe.", "Use it only after closing CK3. It prepares a safe copy and surgery baseline for the next attempt.", "Before next launch", true, true, false, false);
                return;
            }

            if (String.Equals(scenario, "Rehost", StringComparison.OrdinalIgnoreCase))
            {
                AddWorkflowStep(states, scenario, "rehost_host", "Automatic: host is ready to rehost.", "Uses the same computer/network score as Start Session.", "Before next launch", true, false, host.Suitable, !host.Suitable);
                AddWorkflowStep(states, scenario, "rehost_save", "Automatic: selected save is still usable.", "Prefer rollback to the selected clean manual save with safe rules.", "Before next launch", true, false, save.Score >= 70, save.Score < 70);
                AddWorkflowStep(states, scenario, "rehost_profile", "Automatic: profile is stable before lobby.", "No mods, public branch, no risky launch flags, stable settings.", "Before next launch", true, false, stableProfile && parityReady, !(stableProfile && parityReady));
                AddWorkflowStep(states, scenario, "rehost_fix_host", "Manual: run Fix host if host readiness is weak.", "Use it before launching the new lobby.", "Before next launch", true, true, false, false);
                AddWorkflowStep(states, scenario, "rehost_fix_save", "Manual: run Fix save if the selected save is weak.", "Use it before launching the new lobby.", "Before next launch", true, true, false, false);
                AddWorkflowStep(states, scenario, "rehost_fresh_lobby", "Manual: create a fresh lobby.", "Do not resume the old lobby state.", "In new lobby", true, true, false, false);
                AddWorkflowStep(states, scenario, "rehost_delay_code", "Manual: share code after checks.", "Avoid chaotic joins during recovery setup. Use Rehost pack if players need the exact baseline files.", "In new lobby", true, true, false, false);
                AddWorkflowStep(states, scenario, "rehost_wait_all", "Manual: wait for all players.", "Re-run the start protocol after joins.", "In new lobby", true, true, false, false);
                AddWorkflowStep(states, scenario, "rehost_repeat_start", "Manual: repeat start steps after rehost.", "This includes speed 1-2 and no extra clicks in the first month.", "In game", true, true, false, false);
                return;
            }

            AddWorkflowStep(states, scenario, "hotjoin_allowed", "Automatic: controlled hotjoin is allowed for this OOS.", "AI/LivingCharacters/Modifiers/Armies incidents forbid controlled hotjoin.", "Do now", true, false, !oos.HotjoinForbidden, oos.HotjoinForbidden);
            AddWorkflowStep(states, scenario, "hotjoin_clean_oos", "Automatic: OOS folder is clean now.", "Do not hotjoin while fresh OOS folders still exist.", "Do now", true, false, activeOosFolderClean, !activeOosFolderClean);
            AddWorkflowStep(states, scenario, "hotjoin_profile", "Automatic: host profile is still stable.", "No branch/mod/DLC drift before inviting the player back.", "Do now", true, false, stableProfile && parityReady, !(stableProfile && parityReady));
            AddWorkflowStep(states, scenario, "hotjoin_services", "Automatic: host network is good for hotjoin.", "If host suitability is already poor, prefer a full rehost.", "Do now", true, false, host.Score >= 60, host.Score < 60);
            AddWorkflowStep(states, scenario, "hotjoin_pause", "Manual: keep the game paused.", "Do not unpause during transfer or late initialization.", "Do now", true, true, false, false);
            AddWorkflowStep(states, scenario, "hotjoin_no_clicks", "Manual: avoid clicks during join.", "Keep the simulation and UI calm until the join completes.", "Do now", true, true, false, false);
            AddWorkflowStep(states, scenario, "hotjoin_share_code", "Manual: compare parity, then share code.", "Use Compare parity or Parity room after any client-side fix before sharing the new code.", "Do now", true, true, false, false);
            AddWorkflowStep(states, scenario, "hotjoin_repeat_start", "Manual: repeat start steps after join.", "Stay at speed 1-2 and do not rush the first month.", "In game", true, true, false, false);
            AddWorkflowStep(states, scenario, "hotjoin_rehost_path", "Manual: if hotjoin is blocked, switch to Rehost.", "Do not try to repair the host or save inside the live session. Close CK3 first and continue through Rehost.", "Before next launch", true, true, false, false);
        }

        private void AddWorkflowStep(List<WorkflowStepState> states, string scenario, string id, string label, string detail, string timing, bool required, bool manual, bool passed, bool blocked)
        {
            WorkflowStepState state = new WorkflowStepState();
            state.Id = id;
            state.Label = label;
            state.Detail = detail;
            state.Timing = timing;
            state.Required = required;
            state.Manual = manual;
            state.AutoManaged = !manual;
            state.Passed = passed;
            state.Blocked = blocked;
            state.UserDone = false;
            states.Add(state);
        }

        private string FormatWorkflowStepText(WorkflowStepState state)
        {
            string prefix;
            if (state.Manual)
                prefix = state.UserDone ? "Done" : "You do";
            else if (state.Passed)
                prefix = "OK";
            else if (state.Blocked)
                prefix = "Fix";
            else
                prefix = "Wait";

            return prefix + " - " + BuildWorkflowTimingPrefix(state) + MakeWorkflowStepLabelReadable(state);
        }

        private string BuildWorkflowVerdictLine(string scenario, List<WorkflowStepState> states)
        {
            OosDeepInsight oos = CurrentWorkflowAnalysis().Oos;
            bool blocked = false;
            foreach (WorkflowStepState state in states)
            {
                if (state.Required && state.AutoManaged && !state.Passed)
                    blocked = true;
            }

            if (blocked)
                return "Status: Fix these blockers first";

            if (String.Equals(scenario, "Start Session", StringComparison.OrdinalIgnoreCase))
                return "Status: Ready to start";
            if (String.Equals(scenario, "After OOS", StringComparison.OrdinalIgnoreCase))
                return "Status: Review the OOS and choose next step";
            if (String.Equals(scenario, "Rehost", StringComparison.OrdinalIgnoreCase))
                return "Status: Ready to rehost";
            if (String.Equals(scenario, "Hotjoin", StringComparison.OrdinalIgnoreCase) && oos.HotjoinForbidden)
                return "Status: Hotjoin is forbidden for this OOS";
            return "Status: Hotjoin plan ready";
        }

        private string BuildWorkflowScenarioSummaryText(string scenario, List<WorkflowStepState> states)
        {
            WorkflowAnalysisSnapshot analysis = CurrentWorkflowAnalysis();
            HostSuitabilityResult host = analysis.Host;
            HostSaveCandidateResult save = analysis.Save;
            OosDeepInsight oos = analysis.Oos;
            OosIncidentState incident = analysis.Incident;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Scenario: " + scenario);
            sb.AppendLine(BuildWorkflowVerdictLine(scenario, states));
            sb.AppendLine();
            sb.AppendLine("Quick summary");
            sb.AppendLine("- Host readiness: " + host.Level + " (" + host.Score + "/100)");
            sb.AppendLine("- Save: " + MakeWorkflowSaveVerdictReadable(save));
            sb.AppendLine("- Branch: " + NullText(DetectSteamBranch()));
            if (String.Equals(scenario, "After OOS", StringComparison.OrdinalIgnoreCase)
                || String.Equals(scenario, "Rehost", StringComparison.OrdinalIgnoreCase)
                || String.Equals(scenario, "Hotjoin", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine("- Latest OOS: " + MakeWorkflowFileLabelReadable(FindLatestOosMetadataFile(), "not found"));
            if (String.Equals(scenario, "After OOS", StringComparison.OrdinalIgnoreCase)
                || String.Equals(scenario, "Rehost", StringComparison.OrdinalIgnoreCase)
                || String.Equals(scenario, "Hotjoin", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("- Recovery: " + NullText(oos.RecoveryPath));
                sb.AppendLine("- Contamination: " + oos.SessionContaminationLevel + " (" + oos.SessionContaminationScore + "/100)");
                sb.AppendLine("- Hotjoin: " + (oos.HotjoinForbidden ? "FORBIDDEN" : "ALLOWED"));
                sb.AppendLine("- Incident stage: " + NullText(incident.Stage));
                sb.AppendLine("- Confidence: " + NullText(incident.Confidence));
                sb.AppendLine("- Continue risk: " + incident.ContinuationRiskScore + "/100");
                sb.AppendLine("- Failed attempts: " + incident.PriorAttempts);
            }
            sb.AppendLine();
            sb.AppendLine("Recommendation: " + BuildWorkflowRecommendation(scenario, states, host, save));
            if (ShouldShowWorkflowRepairBoundary(scenario))
            {
                sb.AppendLine();
                sb.AppendLine("Scenario logic");
                sb.AppendLine("- Do now: only live decisions and evidence collection inside the current session.");
                sb.AppendLine("- Before next launch: close CK3 first, then use Fix host or Fix save if needed.");
            }
            if (incident.RequiredActions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Incident actions");
                foreach (string line in incident.RequiredActions)
                    sb.AppendLine("- " + line);
            }
            if (incident.BlockedActions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Blocked now");
                foreach (string line in incident.BlockedActions)
                    sb.AppendLine("- " + line);
            }
            sb.AppendLine();
            AppendWorkflowStepSection(sb, states, "Do now");
            AppendWorkflowStepSection(sb, states, "Before next launch");
            AppendWorkflowStepSection(sb, states, "Before start");
            AppendWorkflowStepSection(sb, states, "In lobby");
            AppendWorkflowStepSection(sb, states, "In new lobby");
            AppendWorkflowStepSection(sb, states, "In game");
            return sb.ToString();
        }

        private bool AllCriticalRulesSafe(IEnumerable<SaveRuleCheckResult> rules)
        {
            bool anyRule = false;
            foreach (SaveRuleCheckResult rule in rules)
            {
                anyRule = true;
                if (!rule.Found || !rule.Safe)
                    return false;
            }
            return anyRule;
        }

        private bool RehostPackPrerequisitesReady()
        {
            return File.Exists(StabilizerFile("ck3_stabilizer_host_suitability.txt"))
                && File.Exists(StabilizerFile("ck3_stabilizer_host_save_preparation.txt"))
                && File.Exists(StabilizerFile("ck3_stabilizer_latest_oos_summary.txt"))
                && File.Exists(StabilizerFile("ck3_stabilizer_workflow_status.txt"));
        }

        private void ApplyWorkflowSafeStartProfile()
        {
            if (IsGameRunning())
            {
                MessageBox.Show("Close CK3 and Paradox Launcher before applying the workflow safe start profile.", "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!ValidateBeforeRun())
            {
                MessageBox.Show("Fix the configured game/settings paths first.", "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            EnsureStabilizerRoot();
            if (String.IsNullOrEmpty(GetKnownQuarantine()))
                CreateQuarantine();

            HostSuitabilityResult beforeHost = AnalyzeHostSuitability();
            HostSaveCandidateResult beforeSave = AnalyzeWorkflowHostSaveCandidate();
            BackupSteamAndLauncherSettings();
            StabilizeSteamSettings();
            ForceNoMods();
            StabilizePdxSettings();
            WriteStableGameRuleProfile();
            WriteMultiplayerParityManifest();
            WriteHostSuitabilityReport();
            WriteHostSavePreparationReport();
            WriteRuntimeVerificationReport();
            InvalidateHostSuitabilityCache();
            BeginWorkflowScenarioRefresh();
            Log("OK   Workflow host profile applied.");
            HostSuitabilityResult afterHost = AnalyzeHostSuitability();
            HostSaveCandidateResult afterSave = AnalyzeWorkflowHostSaveCandidate();
            SetStatusText("Fix host applied. Host " + beforeHost.Score + " -> " + afterHost.Score + ", save " + beforeSave.Score + " -> " + afterSave.Score + ".");
        }

        private void WriteWorkflowStatusReport()
        {
            try
            {
                string path = StabilizerFile("ck3_stabilizer_workflow_status.txt");
                WriteTextFileIfMeaningfullyChanged(
                    path,
                    BuildWorkflowStatusReportText(),
                    "FILE Workflow status report written: ",
                    "INFO Workflow status report already up to date: ",
                    true);
            }
            catch (Exception ex)
            {
                Log("WARN Workflow status report could not be written: " + ex.Message);
            }
        }

        private string BuildWorkflowStatusReportText()
        {
            return BuildWorkflowScenarioSnapshotCore(currentWorkflowScenario, CancellationToken.None).Summary;
        }

        private string BuildWorkflowRecommendation(string scenario, List<WorkflowStepState> states, HostSuitabilityResult host, HostSaveCandidateResult save)
        {
            WorkflowAnalysisSnapshot analysis = CurrentWorkflowAnalysis();
            OosDeepInsight oos = analysis.Oos;
            OosIncidentState incident = analysis.Incident;
            bool gameRunning = IsGameRunning();
            bool blocked = false;
            foreach (WorkflowStepState state in states)
            {
                if (state.Required && state.AutoManaged && !state.Passed)
                    blocked = true;
            }

            if (blocked && save.Score < 70)
                return gameRunning
                    ? "The selected save is weak, but repair comes later. Stop the session now, then close CK3 and run Fix save before the next launch."
                    : "Run Fix save or choose a better selected save before doing anything else.";
            if (blocked && !host.Suitable)
                return gameRunning
                    ? "Host readiness is weak, but live repair is not the next step. Stop or stabilize the session now, then close CK3 and run Fix host before the next launch."
                    : "Run Fix host first. It restores the stable host profile and refreshes host/parity reports before you host again.";
            if (String.Equals(scenario, "After OOS", StringComparison.OrdinalIgnoreCase))
                return "Recommended path: " + incident.RecommendedPath + ". Stage: " + incident.Stage + ". Do the live recovery decision now, then close CK3 and use Fix host/Fix save only if the next attempt needs them.";
            if (String.Equals(scenario, "Rehost", StringComparison.OrdinalIgnoreCase))
                return "This scenario is a between-session rebuild. Prepare host and save first, then create a fresh lobby and repeat the start steps.";
            if (String.Equals(scenario, "Hotjoin", StringComparison.OrdinalIgnoreCase))
                return oos.HotjoinForbidden
                    ? "Controlled hotjoin is forbidden for the current OOS. Do not repair inside the live session; switch to " + oos.RecoveryPath + " after closing CK3 if needed."
                    : "This scenario is mostly live-only. Stay paused, avoid clicks, confirm parity, and let the returning player finish the join cleanly.";
            return "Current path looks acceptable. Proceed carefully and keep the first month slow.";
        }

        private void RepairSelectedWorkflowSave()
        {
            HostSaveCandidateResult before = AnalyzeWorkflowHostSaveCandidate();
            bool repaired = EnsureSafeWorkflowHostSave();
            PrepareWorkflowSaveSurgeryBaseline();
            WriteHostSavePreparationReport();
            WriteMultiplayerParityManifest();
            InvalidateHostSuitabilityCache();
            BeginWorkflowScenarioRefresh();
            HostSaveCandidateResult after = AnalyzeWorkflowHostSaveCandidate();
            if (repaired)
                SetStatusText("Fix save finished. Save " + before.Score + " -> " + after.Score + ". Selected safe copy or baseline is ready.");
            else
                SetStatusText("Fix save could not auto-repair this save. Surgery baseline report was still prepared.");
        }

        private string BuildWorkflowFixHostHintText()
        {
            return "Fix host repairs host-side profile drift only: launch options, no-mod state, pdx settings, parity manifest, host suitability and workflow reports. It does not rewrite the selected save.";
        }

        private string BuildWorkflowFixSaveHintText()
        {
            return "Fix save works on the selected save only: it tries to create a safe copy with repaired critical rules and prepares a surgery baseline report. It does not change Windows, internet, launcher profile or other players.";
        }

        private void UpdateWorkflowActionAvailability()
        {
            bool gameRunning = IsGameRunning();
            bool startScenario = String.Equals(currentWorkflowScenario, "Start Session", StringComparison.OrdinalIgnoreCase);
            bool afterOosScenario = String.Equals(currentWorkflowScenario, "After OOS", StringComparison.OrdinalIgnoreCase);
            bool rehostScenario = String.Equals(currentWorkflowScenario, "Rehost", StringComparison.OrdinalIgnoreCase);
            bool hotjoinScenario = String.Equals(currentWorkflowScenario, "Hotjoin", StringComparison.OrdinalIgnoreCase);

            bool fixHostRelevant = startScenario || afterOosScenario || rehostScenario;
            bool fixSaveRelevant = startScenario || afterOosScenario || rehostScenario;
            bool fixHostAllowedNow = fixHostRelevant && !gameRunning;
            bool fixSaveAllowedNow = fixSaveRelevant && !gameRunning;

            workflowApplySafeStartButton.Enabled = fixHostAllowedNow;
            workflowRepairSaveButton.Enabled = fixSaveAllowedNow;

            if (hotjoinScenario)
            {
                stepToolTip.SetToolTip(workflowApplySafeStartButton, "Fix host is not part of Hotjoin. If hotjoin fails, close CK3 and continue through Rehost or After OOS.");
                stepToolTip.SetToolTip(workflowRepairSaveButton, "Fix save is not part of Hotjoin. If the selected save needs repair, close CK3 and continue through Rehost or After OOS.");
            }
            else if (gameRunning)
            {
                stepToolTip.SetToolTip(workflowApplySafeStartButton, "Fix host belongs to the next attempt, not the live session. Close CK3 first, then run it.");
                stepToolTip.SetToolTip(workflowRepairSaveButton, "Fix save belongs to the next attempt, not the live session. Close CK3 first, then run it.");
            }
            else
            {
                stepToolTip.SetToolTip(workflowApplySafeStartButton, BuildWorkflowFixHostHintText());
                stepToolTip.SetToolTip(workflowRepairSaveButton, BuildWorkflowFixSaveHintText());
            }
        }

        private bool ShouldShowWorkflowRepairBoundary(string scenario)
        {
            return String.Equals(scenario, "After OOS", StringComparison.OrdinalIgnoreCase)
                || String.Equals(scenario, "Hotjoin", StringComparison.OrdinalIgnoreCase);
        }

        private void AppendWorkflowStepSection(StringBuilder sb, List<WorkflowStepState> states, string timing)
        {
            bool any = false;
            foreach (WorkflowStepState state in states)
            {
                if (!String.Equals(NullText(state.Timing), timing, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!any)
                {
                    sb.AppendLine();
                    sb.AppendLine(timing);
                    any = true;
                }

                sb.AppendLine("- " + BuildWorkflowStateLine(state));
            }
        }

        private string BuildWorkflowTimingPrefix(WorkflowStepState state)
        {
            string timing = NullText(state.Timing).Trim();
            return timing.Length == 0 ? "" : timing + ": ";
        }

        private void OpenSelectedWorkflowSaveFolder()
        {
            string path = workflowSelectedSavePath;
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("Select an existing save first.", "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Process.Start("explorer.exe", "/select,\"" + path + "\"");
        }

        private void OpenWorkflowOosFolder()
        {
            string path = Path.Combine(ck3Docs, "oos");
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }

        private async void CompareWorkflowParity()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "CK3 parity manifest (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.Title = "Select other players' parity manifests";
                dialog.CheckFileExists = true;
                dialog.Multiselect = true;
                string initialDir = stabilizerRoot;
                if (Directory.Exists(initialDir))
                    dialog.InitialDirectory = initialDir;

                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames == null || dialog.FileNames.Length == 0)
                    return;

                try
                {
                    workflowCompareParityButton.Enabled = false;
                    UseWaitCursor = true;
                    SetStatusText("Comparing parity files...");
                    string[] selectedPaths = (string[])dialog.FileNames.Clone();
                    string report = "";
                    int otherCount = 0;
                    string reportPath = StabilizerFile("ck3_stabilizer_parity_compare.txt");

                    await Task.Run(delegate
                    {
                        WriteMultiplayerParityManifest();
                        string localPath = StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt");
                        ParityManifestRecord local = ParseParityManifest(localPath);
                        List<ParityManifestRecord> others = new List<ParityManifestRecord>();
                        foreach (string path in selectedPaths)
                        {
                            if (String.Equals(path, localPath, StringComparison.OrdinalIgnoreCase))
                                continue;
                            others.Add(ParseParityManifest(path));
                        }

                        if (others.Count == 0)
                            throw new InvalidOperationException("Select at least one other player's parity manifest.");

                        otherCount = others.Count;
                        report = BuildParityComparisonReport(local, others);
                        SafeAtomicFile.WriteAllText(reportPath, report, Encoding.UTF8);
                    });

                    workflowSummaryBox.Text = report;
                    ApplyWorkflowSummaryStyling();
                    SetStatusText("Parity comparison finished: " + otherCount + " player file(s).");
                    Log("FILE Parity comparison report written: " + reportPath);
                    MessageBox.Show(
                        "Parity comparison finished.\r\n\r\nCompared players: " + otherCount + "\r\nReport: " + reportPath,
                        "CK3MPS parity",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Parity comparison failed.\r\n\r\n" + ex.Message, "CK3MPS parity", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Log("WARN Parity comparison failed: " + ex.Message);
                }
                finally
                {
                    UseWaitCursor = false;
                    workflowCompareParityButton.Enabled = true;
                }
            }
        }

        private void DuplicateSelectedWorkflowSave()
        {
            string path = workflowSelectedSavePath;
            string normalizedPath;
            string reason;
            if (!TryGetManagedWorkflowSavePath(path, out normalizedPath, out reason))
            {
                MessageBox.Show(reason, "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string copyPath = DuplicateWorkflowSaveCore(normalizedPath, "");
            workflowSelectedSavePath = copyPath;
            SaveAppConfig();
            InvalidateWorkflowSaveSelectionState();
            MessageBox.Show("Backup save created:\r\n\r\n" + copyPath, "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private string DuplicateWorkflowSaveCore(string normalizedPath, string explicitCopyPath)
        {
            string copyPath = explicitCopyPath;
            if (String.IsNullOrWhiteSpace(copyPath))
            {
                string dir = Path.GetDirectoryName(normalizedPath);
                string name = Path.GetFileNameWithoutExtension(normalizedPath);
                string ext = Path.GetExtension(normalizedPath);
                copyPath = Path.Combine(dir, name + "_workflow_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ext);
                copyPath = UniquePath(copyPath);
            }

            string tempPath = copyPath + ".tmp";
            if (File.Exists(copyPath))
                throw new IOException("Workflow backup destination already exists: " + copyPath);

            bool createdTemp = false;
            try
            {
                File.Copy(normalizedPath, tempPath, false);
                createdTemp = true;
                if (!File.Exists(tempPath))
                    throw new IOException("Temporary workflow backup copy was not created.");
                if (!FileContentsEqual(normalizedPath, tempPath))
                    throw new IOException("Temporary workflow backup copy does not match the source save.");
                if (File.Exists(copyPath))
                    throw new IOException("Workflow backup destination already exists before the final rename: " + copyPath);
                File.Move(tempPath, copyPath);
                return copyPath;
            }
            catch
            {
                if (createdTemp && File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                throw;
            }
        }

        private void ArchiveWorkflowIncident()
        {
            WriteWorkflowStatusReport();
            WriteHostSuitabilityReport();
            WriteHostSavePreparationReport();
            WriteMultiplayerParityManifest();
            AnalyzeLatestOosReport();
            WriteOosEvidencePack();
            RecordIncidentHistoryEvent("archive_incident", AnalyzeOosIncidentState(), "Workflow archive incident");
        }

        private void OpenParityRoom()
        {
            ParityRoomSession session = new ParityRoomSession();
            session.LocalPlayerLabel = Environment.UserName;

            Form form = new Form();
            form.Text = "CK3MPS parity room";
            form.StartPosition = FormStartPosition.CenterParent;
            form.Size = new Size(1220, 760);
            form.MinimumSize = new Size(1040, 680);
            form.Padding = new Padding(12);

            TableLayoutPanel rootLayout = new TableLayoutPanel();
            rootLayout.Dock = DockStyle.Fill;
            rootLayout.ColumnCount = 1;
            rootLayout.RowCount = 3;
            rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            form.Controls.Add(rootLayout);

            FlowLayoutPanel commandBar = new FlowLayoutPanel();
            commandBar.Dock = DockStyle.Fill;
            commandBar.AutoSize = true;
            commandBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            commandBar.WrapContents = true;
            commandBar.Margin = new Padding(0, 0, 0, 8);
            rootLayout.Controls.Add(commandBar, 0, 0);

            Panel statusPanel = new Panel();
            statusPanel.Dock = DockStyle.Fill;
            statusPanel.Height = 46;
            statusPanel.Margin = new Padding(0, 0, 0, 10);
            rootLayout.Controls.Add(statusPanel, 0, 1);

            TableLayoutPanel contentLayout = new TableLayoutPanel();
            contentLayout.Dock = DockStyle.Fill;
            contentLayout.ColumnCount = 2;
            contentLayout.RowCount = 2;
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66F));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 52F));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 48F));
            rootLayout.Controls.Add(contentLayout, 0, 2);

            Button createButton = new Button();
            createButton.Text = "Create local room";
            createButton.Size = new Size(132, 32);
            createButton.Margin = new Padding(0, 0, 8, 8);
            commandBar.Controls.Add(createButton);

            Button joinButton = new Button();
            joinButton.Text = "Join room";
            joinButton.Size = new Size(96, 32);
            joinButton.Margin = new Padding(0, 0, 8, 8);
            commandBar.Controls.Add(joinButton);

            Button compareButton = new Button();
            compareButton.Text = "Compare now";
            compareButton.Size = new Size(106, 32);
            compareButton.Margin = new Padding(0, 0, 8, 8);
            commandBar.Controls.Add(compareButton);

            Button exportButton = new Button();
            exportButton.Text = "Export my parity";
            exportButton.Size = new Size(118, 32);
            exportButton.Margin = new Padding(0, 0, 8, 8);
            commandBar.Controls.Add(exportButton);

            Button sendParityButton = new Button();
            sendParityButton.Text = "Send parity only";
            sendParityButton.Size = new Size(118, 32);
            sendParityButton.Margin = new Padding(0, 0, 8, 8);
            commandBar.Controls.Add(sendParityButton);

            Button sendButton = new Button();
            sendButton.Text = "Send OOS data";
            sendButton.Size = new Size(112, 32);
            sendButton.Margin = new Padding(0, 0, 8, 8);
            commandBar.Controls.Add(sendButton);

            Button incidentPackButton = new Button();
            incidentPackButton.Text = "Collect incident pack";
            incidentPackButton.Size = new Size(132, 32);
            incidentPackButton.Margin = new Padding(0, 0, 0, 8);
            commandBar.Controls.Add(incidentPackButton);

            Label infoLabel = new Label();
            infoLabel.Dock = DockStyle.Top;
            infoLabel.Height = 18;
            statusPanel.Controls.Add(infoLabel);

            Label roomVerdictLabel = new Label();
            roomVerdictLabel.Dock = DockStyle.Bottom;
            roomVerdictLabel.Height = 20;
            roomVerdictLabel.Font = new Font(Font, FontStyle.Bold);
            statusPanel.Controls.Add(roomVerdictLabel);

            GroupBox playersGroup = new GroupBox();
            playersGroup.Text = "Players";
            playersGroup.Dock = DockStyle.Fill;
            playersGroup.Margin = new Padding(0, 0, 10, 10);
            contentLayout.Controls.Add(playersGroup, 0, 0);

            DataGridView playersGrid = new DataGridView();
            playersGrid.Dock = DockStyle.Fill;
            playersGrid.AllowUserToAddRows = false;
            playersGrid.AllowUserToDeleteRows = false;
            playersGrid.AllowUserToResizeRows = false;
            playersGrid.ReadOnly = true;
            playersGrid.MultiSelect = false;
            playersGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            playersGrid.RowHeadersVisible = false;
            playersGrid.BackgroundColor = Color.White;
            playersGrid.BorderStyle = BorderStyle.None;
            playersGrid.GridColor = Color.Gainsboro;
            playersGrid.EnableHeadersVisualStyles = false;
            playersGrid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            playersGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(242, 244, 247);
            playersGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(42, 42, 42);
            playersGrid.Columns.Add("Player", "Player");
            playersGrid.Columns.Add("Parity", "Parity");
            playersGrid.Columns.Add("Oos", "OOS");
            playersGrid.Columns.Add("Path", "Path");
            playersGrid.Columns.Add("Risk", "Risk");
            playersGrid.Columns["Player"].Width = 92;
            playersGrid.Columns["Parity"].Width = 56;
            playersGrid.Columns["Oos"].Width = 56;
            playersGrid.Columns["Path"].Width = 98;
            playersGrid.Columns["Risk"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            playersGroup.Controls.Add(playersGrid);

            GroupBox diagnosisGroup = new GroupBox();
            diagnosisGroup.Text = "Session diagnosis";
            diagnosisGroup.Dock = DockStyle.Fill;
            diagnosisGroup.Margin = new Padding(0, 0, 10, 0);
            contentLayout.Controls.Add(diagnosisGroup, 0, 1);

            RichTextBox diagnosisBox = new RichTextBox();
            ConfigureLogView(diagnosisBox);
            diagnosisBox.Dock = DockStyle.Fill;
            diagnosisGroup.Controls.Add(diagnosisBox);

            GroupBox diffGroup = new GroupBox();
            diffGroup.Text = "Differences";
            diffGroup.Dock = DockStyle.Fill;
            diffGroup.Margin = new Padding(0, 0, 0, 10);
            contentLayout.Controls.Add(diffGroup, 1, 0);

            DataGridView diffGrid = new DataGridView();
            diffGrid.Dock = DockStyle.Fill;
            diffGrid.AllowUserToAddRows = false;
            diffGrid.AllowUserToDeleteRows = false;
            diffGrid.AllowUserToResizeRows = false;
            diffGrid.ReadOnly = true;
            diffGrid.MultiSelect = false;
            diffGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            diffGrid.RowHeadersVisible = false;
            diffGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            diffGrid.BackgroundColor = Color.White;
            diffGrid.BorderStyle = BorderStyle.None;
            diffGrid.GridColor = Color.Gainsboro;
            diffGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            diffGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            diffGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 232, 252);
            diffGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(24, 24, 24);
            diffGrid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            diffGrid.EnableHeadersVisualStyles = false;
            diffGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(242, 244, 247);
            diffGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(42, 42, 42);
            diffGrid.Columns.Add("Player", "Player");
            diffGrid.Columns.Add("Area", "Check");
            diffGrid.Columns.Add("HostValue", "Host");
            diffGrid.Columns.Add("PlayerValue", "Remote");
            diffGrid.Columns.Add("Status", "Status");
            diffGrid.Columns["Player"].Width = 104;
            diffGrid.Columns["Area"].Width = 168;
            diffGrid.Columns["HostValue"].Width = 146;
            diffGrid.Columns["PlayerValue"].Width = 146;
            diffGrid.Columns["Status"].Width = 84;
            foreach (DataGridViewColumn column in diffGrid.Columns)
                column.SortMode = DataGridViewColumnSortMode.Automatic;
            diffGroup.Controls.Add(diffGrid);

            TableLayoutPanel lowerRightLayout = new TableLayoutPanel();
            lowerRightLayout.Dock = DockStyle.Fill;
            lowerRightLayout.ColumnCount = 2;
            lowerRightLayout.RowCount = 1;
            lowerRightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            lowerRightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            contentLayout.Controls.Add(lowerRightLayout, 1, 1);

            GroupBox actionsGroup = new GroupBox();
            actionsGroup.Text = "Player responsibilities";
            actionsGroup.Dock = DockStyle.Fill;
            actionsGroup.Margin = new Padding(0, 0, 8, 0);
            lowerRightLayout.Controls.Add(actionsGroup, 0, 0);

            RichTextBox actionsBox = new RichTextBox();
            ConfigureLogView(actionsBox);
            actionsBox.Dock = DockStyle.Fill;
            actionsGroup.Controls.Add(actionsBox);

            GroupBox blockedGroup = new GroupBox();
            blockedGroup.Text = "Blocked and allowed";
            blockedGroup.Dock = DockStyle.Fill;
            blockedGroup.Margin = new Padding(8, 0, 0, 0);
            lowerRightLayout.Controls.Add(blockedGroup, 1, 0);

            RichTextBox blockedBox = new RichTextBox();
            ConfigureLogView(blockedBox);
            blockedBox.Dock = DockStyle.Fill;
            blockedGroup.Controls.Add(blockedBox);

            System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 220;
            bool refreshBusy = false;
            bool pendingManifestRefresh = false;
            bool pendingOosRefresh = false;
            bool pendingPersistReport = false;

            Action<ParityRoomViewState> applyViewState = delegate (ParityRoomViewState view)
            {
                infoLabel.Text = view.InfoText;
                PopulateParityPlayersGrid(playersGrid, view.PlayerRows);
                PopulateParityDifferenceGrid(diffGrid, view.DifferenceRows);
                PopulateParityRoomPanels(view.ActionsText, diagnosisBox, actionsBox, blockedBox);
                roomVerdictLabel.Text = view.SafeToStart ? "Safe to start" : "Unsafe to start";
                roomVerdictLabel.ForeColor = view.SafeToStart ? Color.FromArgb(22, 120, 48) : Color.FromArgb(170, 40, 40);
            };

            Func<bool, bool, ParityRoomViewState> buildViewState = delegate (bool refreshManifest, bool refreshOos)
            {
                if (refreshManifest || refreshOos)
                    RefreshParityRoomLocalState(session, refreshManifest, refreshOos);

                ParityRoomViewState view = new ParityRoomViewState();
                view.InfoText = BuildParityRoomInfoText(session);
                view.PlayerRows.AddRange(BuildParityRoomPlayerRows(session));
                string differencesText;
                string actionsText;
                List<ParityDifferenceRow> rows;
                bool safeToStart;
                BuildParityRoomComparisonTexts(session, out differencesText, out actionsText, out rows, out safeToStart);
                view.ActionsText = actionsText;
                view.SafeToStart = safeToStart;
                view.DifferenceRows.AddRange(rows);
                return view;
            };

            Func<bool, bool, bool, Task> refreshAllAsync = null;
            refreshAllAsync = async delegate (bool refreshManifest, bool refreshOos, bool persistReport)
            {
                if (refreshBusy)
                {
                    pendingManifestRefresh = pendingManifestRefresh || refreshManifest;
                    pendingOosRefresh = pendingOosRefresh || refreshOos;
                    pendingPersistReport = pendingPersistReport || persistReport;
                    refreshTimer.Stop();
                    refreshTimer.Start();
                    return;
                }

                refreshBusy = true;
                compareButton.Enabled = false;
                sendParityButton.Enabled = false;
                sendButton.Enabled = false;
                try
                {
                    ParityRoomViewState view = await Task.Run(delegate
                    {
                        return buildViewState(refreshManifest, refreshOos);
                    });

                    if (form.IsDisposed)
                        return;

                    applyViewState(view);

                    if (persistReport && session.Hosting)
                    {
                        string reportPath = StabilizerFile("ck3_stabilizer_parity_room_compare.txt");
                        SafeAtomicFile.WriteAllText(reportPath, BuildParityDifferenceGridReport(diffGrid) + Environment.NewLine + Environment.NewLine + actionsBox.Text, Encoding.UTF8);
                        RecordIncidentHistoryEvent("parity_room_compare", AnalyzeOosIncidentState(), "Parity room comparison report");
                        Log("FILE Parity room comparison report written: " + reportPath);
                    }
                }
                catch (Exception ex)
                {
                    if (!form.IsDisposed)
                    {
                        MessageBox.Show("Parity room refresh failed.\r\n\r\n" + ex.Message, "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        Log("WARN Parity room refresh failed: " + ex.Message);
                    }
                }
                finally
                {
                    refreshBusy = false;
                    compareButton.Enabled = true;
                    sendParityButton.Enabled = true;
                    sendButton.Enabled = true;
                }

                if (pendingManifestRefresh || pendingOosRefresh || pendingPersistReport)
                {
                    bool nextManifest = pendingManifestRefresh;
                    bool nextOos = pendingOosRefresh;
                    bool nextPersist = pendingPersistReport;
                    pendingManifestRefresh = false;
                    pendingOosRefresh = false;
                    pendingPersistReport = false;
                    await refreshAllAsync(nextManifest, nextOos, nextPersist);
                }
            };

            refreshTimer.Tick += async delegate
            {
                refreshTimer.Stop();
                bool nextManifest = pendingManifestRefresh;
                bool nextOos = pendingOosRefresh;
                bool nextPersist = pendingPersistReport;
                pendingManifestRefresh = false;
                pendingOosRefresh = false;
                pendingPersistReport = false;
                await refreshAllAsync(nextManifest, nextOos, nextPersist);
            };

            Action<bool, bool, bool> queueRefresh = delegate (bool refreshManifest, bool refreshOos, bool persistReport)
            {
                pendingManifestRefresh = pendingManifestRefresh || refreshManifest;
                pendingOosRefresh = pendingOosRefresh || refreshOos;
                pendingPersistReport = pendingPersistReport || persistReport;
                refreshTimer.Stop();
                refreshTimer.Start();
            };

            createButton.Click += delegate
            {
                try
                {
                    StartParityRoomHost(session, delegate
                    {
                        if (!form.IsDisposed && form.IsHandleCreated)
                            form.BeginInvoke((MethodInvoker)delegate { queueRefresh(false, false, false); });
                    });
                    queueRefresh(true, true, false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not create parity room.\r\n\r\n" + ex.Message, "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            incidentPackButton.Click += async delegate
            {
                try
                {
                    incidentPackButton.Enabled = false;
                    string packPath = await Task.Run(delegate { return WriteParityRoomIncidentPack(session); });
                    queueRefresh(false, false, false);
                    MessageBox.Show("Incident pack collected:\r\n\r\n" + packPath, "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Process.Start("explorer.exe", packPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not collect the incident pack.\r\n\r\n" + ex.Message, "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    incidentPackButton.Enabled = true;
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

                try
                {
                    joinButton.Enabled = false;
                    await Task.Run(delegate { SendParityRoomPayload(session, true, false); });
                    queueRefresh(true, false, false);
                    MessageBox.Show("Joined the parity room and sent your manifest.", "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not join the parity room.\r\n\r\n" + ex.Message, "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    joinButton.Enabled = true;
                }
            };

            compareButton.Click += async delegate
            {
                await refreshAllAsync(true, true, true);
            };

            exportButton.Click += delegate
            {
                RefreshParityRoomLocalState(session, true, false);
                string path = StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt");
                MessageBox.Show("Local parity manifest is ready:\r\n\r\n" + path, "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            };

            sendParityButton.Click += async delegate
            {
                if (!session.Joined)
                {
                    MessageBox.Show("Join a room first.", "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    sendParityButton.Enabled = false;
                    await Task.Run(delegate
                    {
                        RefreshParityRoomLocalState(session, true, false);
                        SendParityRoomPayload(session, true, false);
                    });
                    queueRefresh(true, false, false);
                    MessageBox.Show("Your parity data were sent to the host.", "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not send parity data.\r\n\r\n" + ex.Message, "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    sendParityButton.Enabled = true;
                }
            };

            sendButton.Click += async delegate
            {
                if (!session.Joined)
                {
                    MessageBox.Show("Join a room first.", "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!TryConfirmParityRoomOosShare(session))
                    return;

                try
                {
                    sendButton.Enabled = false;
                    await Task.Run(delegate
                    {
                        RefreshParityRoomLocalState(session, false, true);
                        SendParityRoomPayload(session, false, true);
                    });
                    queueRefresh(false, true, false);
                    MessageBox.Show("Your OOS data were sent to the host.", "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not send OOS data.\r\n\r\n" + ex.Message, "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    sendButton.Enabled = true;
                }
            };

            form.FormClosed += delegate
            {
                refreshTimer.Stop();
                refreshTimer.Dispose();
                StopParityRoomHost(session);
            };

            form.Shown += async delegate
            {
                await refreshAllAsync(true, true, false);
            };
            form.ShowDialog(this);
        }

        private bool TryConfirmParityRoomOosShare(ParityRoomSession session)
        {
            if (session == null)
                return false;
            if (session.RawOosShareConsented)
                return true;

            string forced = Environment.GetEnvironmentVariable("CK3MPS_TEST_RAW_OOS_CONSENT");
            if (String.Equals(forced, "allow", StringComparison.OrdinalIgnoreCase))
            {
                session.RawOosShareConsented = true;
                session.OosReportsShareConsented = true;
                session.RawOosDumpsShareConsented = true;
                return true;
            }
            if (String.Equals(forced, "deny", StringComparison.OrdinalIgnoreCase))
                return false;

            using (Form dialog = new Form())
            {
                dialog.Text = "Share OOS data";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.ClientSize = new Size(520, 220);
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;

                Label explanation = new Label
                {
                    Left = 16,
                    Top = 14,
                    Width = 486,
                    Height = 52,
                    Text = "Choose exactly what to share with the host for this room session. Raw dumps and error logs can contain game or machine-specific details and are off by default."
                };
                CheckBox reports = new CheckBox
                {
                    Left = 20,
                    Top = 76,
                    Width = 470,
                    Checked = true,
                    Text = "Share OOS summaries, metadata, reports and recovery guidance"
                };
                CheckBox raw = new CheckBox
                {
                    Left = 20,
                    Top = 108,
                    Width = 470,
                    Checked = false,
                    Text = "Share raw save/modifier dumps and error-log excerpts"
                };
                Button send = new Button { Text = "Share selected", Left = 300, Top = 166, Width = 112, DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = "Cancel", Left = 420, Top = 166, Width = 82, DialogResult = DialogResult.Cancel };
                dialog.Controls.Add(explanation);
                dialog.Controls.Add(reports);
                dialog.Controls.Add(raw);
                dialog.Controls.Add(send);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = send;
                dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) != DialogResult.OK || (!reports.Checked && !raw.Checked))
                    return false;

                session.OosReportsShareConsented = reports.Checked;
                session.RawOosDumpsShareConsented = raw.Checked;
                session.RawOosShareConsented = true;
                return true;
            }
        }

        private string BuildParityRoomInfoText(ParityRoomSession session)
        {
            if (session.Hosting && session.Listener != null)
                return "Room mode: hosting | Host: 127.0.0.1 | Port: " + session.JoinPort + " | Code: " + session.RoomCode + " | Secret: " + session.SharedSecret;
            if (session.Joined)
                return "Room mode: joined | Host: " + session.JoinHost + ":" + session.JoinPort + " | Code: " + session.RoomCode + " | Player: " + session.LocalPlayerLabel;
            return "Create a local loopback room or join an existing local host room.";
        }

        private void RefreshParityRoomLocalState(ParityRoomSession session, bool refreshManifest, bool refreshOos)
        {
            if (session == null)
                return;

            if (refreshManifest)
            {
                WriteMultiplayerParityManifest();
                string manifestPath = StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt");
                session.LocalManifestText = ReadTextIfExists(manifestPath);
                session.LocalManifest = String.IsNullOrWhiteSpace(session.LocalManifestText)
                    ? null
                    : ParseParityManifestText(session.LocalManifestText, manifestPath, session.LocalPlayerLabel);
            }

            if (refreshOos)
            {
                AnalyzeLatestOosReport();
                OosDeepInsight insight = AnalyzeLatestOosDeepInsight();
                session.LocalInsight = insight;
                session.LocalOosSummaryText = ReadTextIfExists(StabilizerFile("ck3_stabilizer_latest_oos_summary.txt"));
                session.LocalOosMetadataText = ReadTextIfExists(FindLatestOosMetadataFile());
                session.LocalOosDeepReportText = ReadTextIfExists(StabilizerFile("ck3_stabilizer_latest_oos_deep_report.txt"));
                session.LocalOosRunbookText = ReadTextIfExists(StabilizerFile("ck3_stabilizer_recovery_runbook.txt"));
                session.LocalOosContaminationText = ReadTextIfExists(StabilizerFile("ck3_stabilizer_session_contamination_score.txt"));
                session.LocalOosSaveDumpText = ReadTrimmedTextIfExists(insight.SaveDumpPath, 24000);
                session.LocalOosModifierDumpText = ReadTrimmedTextIfExists(insight.ModifierDumpPath, 24000);
                session.LocalOosErrorLogText = ReadTrimmedTextIfExists(insight.ErrorLogPath, 16000);
            }

            session.LastComparisonSignature = "";
        }

        private void UpdateParityRoomPeerDerivedData(ParityRoomPeer peer)
        {
            if (peer == null)
                return;

            peer.ParsedManifest = String.IsNullOrWhiteSpace(peer.ManifestText)
                ? null
                : ParseParityManifestText(peer.ManifestText, peer.PlayerLabel, peer.PlayerLabel);

            string level = ExtractReportValue(peer.OosContaminationText, "Level");
            string score = ExtractReportValue(peer.OosContaminationText, "Score");
            peer.ParsedRecoveryPath = ExtractReportValue(peer.OosDeepReportText, "Recovery path");
            peer.ParsedHotjoin = ExtractReportValue(peer.OosDeepReportText, "Controlled hotjoin");
            peer.ParsedContamination = level;
            if (!String.IsNullOrWhiteSpace(score))
                peer.ParsedContamination = NullText(level) + " " + score.Replace("/100", "");
            if (String.IsNullOrWhiteSpace(peer.ParsedContamination))
                peer.ParsedContamination = ExtractReportValue(peer.OosDeepReportText, "Session contamination");
        }

        private string BuildParityRoomComparisonSignature(ParityRoomSession session, List<ParityRoomPeer> peers)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(NullText(session.LocalPlayerLabel)).Append('|');
            sb.Append(NullText(session.LocalManifestText).Length).Append('|');
            sb.Append(NullText(session.LocalInsight == null ? "" : session.LocalInsight.OosType)).Append('|');
            sb.Append(session.LocalInsight == null ? 0 : session.LocalInsight.SessionContaminationScore).Append('|');
            foreach (ParityRoomPeer peer in peers)
            {
                sb.Append(NullText(peer.PlayerLabel)).Append('|');
                sb.Append(peer.ReceivedUtc.Ticks).Append('|');
                sb.Append(NullText(peer.ManifestText).Length).Append('|');
                sb.Append(NullText(peer.OosDeepReportText).Length).Append('|');
                sb.Append(NullText(peer.OosContaminationText).Length).Append('|');
            }
            return sb.ToString();
        }

        private List<ParityDifferenceRow> CloneParityDifferenceRows(List<ParityDifferenceRow> rows)
        {
            List<ParityDifferenceRow> copy = new List<ParityDifferenceRow>();
            if (rows == null)
                return copy;

            foreach (ParityDifferenceRow row in rows)
            {
                ParityDifferenceRow cloned = new ParityDifferenceRow();
                cloned.Player = row.Player;
                cloned.Area = row.Area;
                cloned.HostValue = row.HostValue;
                cloned.PlayerValue = row.PlayerValue;
                cloned.Status = row.Status;
                copy.Add(cloned);
            }

            return copy;
        }

        private string BuildParityRoomPlayersText(ParityRoomSession session)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Local player");
            sb.AppendLine("- " + session.LocalPlayerLabel);
            sb.AppendLine("- Manifest: " + StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt"));
            sb.AppendLine("- Latest OOS: " + NullText(FindLatestOosMetadataFile()));
            sb.AppendLine();
            sb.AppendLine("Remote players");

            lock (session.Sync)
            {
                if (session.Peers.Count == 0)
                {
                    sb.AppendLine("- none yet");
                }
                else
                {
                    foreach (ParityRoomPeer peer in session.Peers)
                    {
                        sb.AppendLine("- " + peer.PlayerLabel + " | " + peer.Endpoint);
                        sb.AppendLine("  manifest: " + YesNo(!String.IsNullOrWhiteSpace(peer.ManifestText)));
                        sb.AppendLine("  oos summary: " + YesNo(!String.IsNullOrWhiteSpace(peer.OosSummaryText)));
                        sb.AppendLine("  oos metadata: " + YesNo(!String.IsNullOrWhiteSpace(peer.OosMetadataText)));
                        sb.AppendLine("  deep parser: " + YesNo(!String.IsNullOrWhiteSpace(peer.OosDeepReportText)));
                        sb.AppendLine("  runbook: " + YesNo(!String.IsNullOrWhiteSpace(peer.OosRecoveryRunbookText)));
                        sb.AppendLine("  received: " + peer.ReceivedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                    }
                }
            }

            return sb.ToString();
        }

        private List<ParityRoomPlayerRow> BuildParityRoomPlayerRows(ParityRoomSession session)
        {
            List<ParityRoomPlayerRow> rows = new List<ParityRoomPlayerRow>();
            if (session == null)
                return rows;

            string localRisk = "Ready";
            OosDeepInsight localInsight = session.LocalInsight ?? new OosDeepInsight();
            if (!String.IsNullOrWhiteSpace(localInsight.OosType))
                localRisk = localInsight.SessionContaminationLevel + " " + localInsight.SessionContaminationScore;

            ParityRoomPlayerRow local = new ParityRoomPlayerRow();
            local.Player = FormatParityPlayerLabel(session.LocalPlayerLabel);
            local.Parity = String.IsNullOrWhiteSpace(session.LocalManifestText) ? "No" : "Yes";
            local.Oos = String.IsNullOrWhiteSpace(localInsight.OosType) ? "No" : "Yes";
            local.Path = String.IsNullOrWhiteSpace(localInsight.OosType) ? "-" : localInsight.RecoveryPath;
            local.Risk = localRisk;
            local.Tone = "local";
            rows.Add(local);

            lock (session.Sync)
            {
                foreach (ParityRoomPeer peer in session.Peers)
                {
                    ParityRoomPlayerRow row = new ParityRoomPlayerRow();
                    row.Player = FormatParityPlayerLabel(peer.PlayerLabel);
                    row.Parity = YesNo(peer.ParsedManifest != null || !String.IsNullOrWhiteSpace(peer.ManifestText));
                    row.Oos = YesNo(!String.IsNullOrWhiteSpace(peer.OosDeepReportText) || !String.IsNullOrWhiteSpace(peer.OosSummaryText));
                    row.Path = String.IsNullOrWhiteSpace(peer.ParsedRecoveryPath) ? "-" : peer.ParsedRecoveryPath;
                    row.Risk = String.IsNullOrWhiteSpace(peer.ParsedContamination) ? "-" : peer.ParsedContamination;
                    row.Tone = "";
                    if (NullText(row.Path).IndexOf("rollback", StringComparison.OrdinalIgnoreCase) >= 0
                        || NullText(row.Risk).IndexOf("CRITICAL", StringComparison.OrdinalIgnoreCase) >= 0)
                        row.Tone = "critical";
                    else if (NullText(row.Path).IndexOf("rehost", StringComparison.OrdinalIgnoreCase) >= 0)
                        row.Tone = "warning";
                    rows.Add(row);
                }
            }

            return rows;
        }

        private void PopulateParityPlayersGrid(DataGridView grid, List<ParityRoomPlayerRow> rows)
        {
            if (grid == null)
                return;

            grid.SuspendLayout();
            grid.Rows.Clear();
            List<ParityRoomPlayerRow> safeRows = rows ?? new List<ParityRoomPlayerRow>();
            foreach (ParityRoomPlayerRow row in safeRows)
            {
                int index = grid.Rows.Add(row.Player, row.Parity, row.Oos, row.Path, row.Risk);
                if (String.Equals(row.Tone, "local", StringComparison.OrdinalIgnoreCase))
                    grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(232, 247, 236);
                else if (String.Equals(row.Tone, "critical", StringComparison.OrdinalIgnoreCase))
                    grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(252, 235, 235);
                else if (String.Equals(row.Tone, "warning", StringComparison.OrdinalIgnoreCase))
                    grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(250, 244, 224);
            }

            grid.ClearSelection();
            grid.ResumeLayout();
        }

        private void PopulateParityRoomPanels(string actionsText, RichTextBox diagnosisBox, RichTextBox responsibilitiesBox, RichTextBox blockedBox)
        {
            if (diagnosisBox != null)
                diagnosisBox.Text = ExtractParityRoomSection(actionsText, "Observed", "Interpreted as")
                    + Environment.NewLine + ExtractParityRoomSection(actionsText, "Interpreted as", "Risk")
                    + Environment.NewLine + ExtractParityRoomSection(actionsText, "Risk", "Required actions");
            if (responsibilitiesBox != null)
                responsibilitiesBox.Text = BuildParityResponsibilitiesText(actionsText);
            if (blockedBox != null)
                blockedBox.Text = ExtractParityRoomSection(actionsText, "Blocked actions", null)
                    + Environment.NewLine + BuildParityAllowedText(actionsText);
        }

        private void BuildParityRoomComparisonTexts(ParityRoomSession session, out string differencesText, out string actionsText, out List<ParityDifferenceRow> rows, out bool safeToStart)
        {
            rows = new List<ParityDifferenceRow>();
            safeToStart = false;
            differencesText = "No remote player manifests yet.";
            actionsText = "Create a room, then let players join and send their parity data.";

            List<ParityRoomPeer> peers;
            lock (session.Sync)
                peers = new List<ParityRoomPeer>(session.Peers);
            if (peers.Count == 0)
                return;

            string signature = BuildParityRoomComparisonSignature(session, peers);
            if (String.Equals(session.LastComparisonSignature, signature, StringComparison.Ordinal))
            {
                differencesText = session.LastComparisonDifferencesText;
                actionsText = session.LastComparisonActionsText;
                rows = CloneParityDifferenceRows(session.LastComparisonRows);
                safeToStart = session.LastComparisonSafeToStart;
                return;
            }

            if (session.LocalManifest == null)
                RefreshParityRoomLocalState(session, true, true);
            ParityManifestRecord local = session.LocalManifest;
            OosDeepInsight localInsight = session.LocalInsight ?? new OosDeepInsight();
            List<ParityManifestRecord> others = new List<ParityManifestRecord>();
            List<string> oosNotes = new List<string>();
            List<string> diagnosisNotes = new List<string>();
            foreach (ParityRoomPeer peer in peers)
            {
                if (peer.ParsedManifest == null && !String.IsNullOrWhiteSpace(peer.ManifestText))
                    UpdateParityRoomPeerDerivedData(peer);

                if (peer.ParsedManifest == null)
                    continue;

                others.Add(peer.ParsedManifest);
                if (!String.IsNullOrWhiteSpace(peer.OosMetadataText) || !String.IsNullOrWhiteSpace(peer.OosSummaryText))
                {
                    oosNotes.Add(peer.PlayerLabel + ": OOS data received"
                        + (String.IsNullOrWhiteSpace(peer.OosMetadataText) ? "" : " | metadata")
                        + (String.IsNullOrWhiteSpace(peer.OosSummaryText) ? "" : " | summary"));
                }

                string peerPath = peer.ParsedRecoveryPath;
                string peerContamination = peer.ParsedContamination;
                string peerHotjoin = peer.ParsedHotjoin;
                if (!String.IsNullOrWhiteSpace(peerPath) || !String.IsNullOrWhiteSpace(peerContamination))
                {
                    diagnosisNotes.Add(peer.PlayerLabel + ": path=" + NullText(peerPath)
                        + " | contamination=" + NullText(peerContamination)
                        + (String.IsNullOrWhiteSpace(peerHotjoin) ? "" : " | hotjoin=" + peerHotjoin));
                }
            }

            if (others.Count == 0)
            {
                differencesText = "Remote players are connected, but no manifest payload is available yet.";
                actionsText = "Ask the players to click Send parity only or re-join the room.";
                return;
            }

            List<string> findings;
            List<string> actions;
            BuildParityComparisonData(local, others, oosNotes, out findings, out actions, out rows, out safeToStart);
            BuildParityRoomRecoveryActions(localInsight, peers, diagnosisNotes, actions, ref safeToStart);

            StringBuilder diff = new StringBuilder();
            diff.AppendLine("Differences");
            if (findings.Count == 0)
                diff.AppendLine("- OK: all connected players match the host parity baseline.");
            else
                foreach (string finding in findings)
                    diff.AppendLine("- " + finding);

            if (oosNotes.Count > 0)
            {
                diff.AppendLine();
                diff.AppendLine("OOS data");
                foreach (string note in oosNotes)
                    diff.AppendLine("- " + note);
            }

            if (diagnosisNotes.Count > 0)
            {
                diff.AppendLine();
                diff.AppendLine("Recovery diagnosis");
                foreach (string note in diagnosisNotes)
                    diff.AppendLine("- " + note);
            }

            StringBuilder act = new StringBuilder();
            act.AppendLine("Diagnosis and fixes");
            BuildParityRoomDiagnosisText(act, localInsight, peers, findings, diagnosisNotes, actions, safeToStart);

            differencesText = diff.ToString();
            actionsText = act.ToString();
            session.LastComparisonSignature = signature;
            session.LastComparisonDifferencesText = differencesText;
            session.LastComparisonActionsText = actionsText;
            session.LastComparisonSafeToStart = safeToStart;
            session.LastComparisonRows.Clear();
            session.LastComparisonRows.AddRange(CloneParityDifferenceRows(rows));
        }

        private void StartParityRoomHost(ParityRoomSession session, Action onPeerChanged)
        {
            StopParityRoomHost(session);

            TcpListener listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
            session.Listener = listener;
            session.CancelSource = new CancellationTokenSource();
            session.Hosting = true;
            session.Joined = false;
            session.JoinPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            session.RoomCode = GenerateParityRoomCode();
            session.SharedSecret = GenerateParityRoomSecret();
            lock (session.Sync)
            {
                session.Peers.Clear();
                session.RequestTimesByEndpoint.Clear();
                session.ActiveClientTasks.Clear();
                session.ActiveClients.Clear();
            }

            session.AcceptLoopTask = Task.Run(delegate { RunParityRoomAcceptLoop(session, onPeerChanged); });
        }

        private void StopParityRoomHost(ParityRoomSession session)
        {
            Task acceptTask;
            Task[] clientTasks;
            TcpClient[] activeClients;
            try
            {
                if (session.CancelSource != null)
                    session.CancelSource.Cancel();
            }
            catch { }

            try
            {
                if (session.Listener != null)
                    session.Listener.Stop();
            }
            catch { }

            lock (session.Sync)
            {
                acceptTask = session.AcceptLoopTask;
                clientTasks = session.ActiveClientTasks.ToArray();
                activeClients = session.ActiveClients.ToArray();
            }

            foreach (TcpClient client in activeClients)
            {
                try { client.Close(); } catch { }
            }

            try
            {
                List<Task> pending = new List<Task>(clientTasks);
                if (acceptTask != null)
                    pending.Add(acceptTask);
                if (pending.Count > 0)
                    Task.WaitAll(pending.ToArray(), ParityRoomSession.SocketTimeoutMs + 1000);
            }
            catch (AggregateException) { }

            session.Listener = null;
            session.CancelSource = null;
            session.AcceptLoopTask = null;
            lock (session.Sync)
            {
                session.ActiveClientTasks.Clear();
                session.ActiveClients.Clear();
            }
            session.Hosting = false;
        }

        private void RunParityRoomAcceptLoop(ParityRoomSession session, Action onPeerChanged)
        {
            while (session.Listener != null && session.CancelSource != null && !session.CancelSource.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = session.Listener.AcceptTcpClient();
                    if (!session.ClientSlots.Wait(0))
                    {
                        RejectBusyParityRoomClient(client, session.SharedSecret);
                        continue;
                    }

                    TcpClient acceptedClient = client;
                    client = null;
                    Task clientTask = new Task(delegate
                    {
                        try
                        {
                            HandleParityRoomClient(session, acceptedClient, onPeerChanged);
                        }
                        finally
                        {
                            lock (session.Sync)
                                session.ActiveClients.Remove(acceptedClient);
                            session.ClientSlots.Release();
                        }
                    });
                    lock (session.Sync)
                    {
                        session.ActiveClients.Add(acceptedClient);
                        session.ActiveClientTasks.RemoveAll(delegate(Task task) { return task.IsCompleted; });
                        session.ActiveClientTasks.Add(clientTask);
                    }
                    clientTask.Start();
                }
                catch
                {
                    if (client != null)
                    {
                        try { client.Close(); } catch { }
                    }
                    if (session.CancelSource == null || session.CancelSource.IsCancellationRequested)
                        return;
                }
            }
        }

        private void RejectBusyParityRoomClient(TcpClient client, string sharedSecret)
        {
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    WriteParityRoomMessage(stream, "ERROR\nroom busy", sharedSecret);
                }
            }
            catch
            {
            }
        }

        private void HandleParityRoomClient(ParityRoomSession session, TcpClient client, Action onPeerChanged)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                try
                {
                    client.ReceiveTimeout = ParityRoomSession.SocketTimeoutMs;
                    client.SendTimeout = ParityRoomSession.SocketTimeoutMs;
                    if (!TryConsumeParityRoomRateLimit(session, client))
                    {
                        WriteParityRoomMessage(stream, "ERROR\nrate limit exceeded", session.SharedSecret);
                        return;
                    }
                    string payload = ReadParityRoomMessage(stream, session.SharedSecret);
                    Dictionary<string, string> fields = ParseParityRoomPayload(payload);
                    string validationError;
                    if (!ValidateAndRememberParityRoomPayload(session, fields, out validationError))
                    {
                        WriteParityRoomMessage(stream, "ERROR\n" + validationError, session.SharedSecret);
                        return;
                    }

                    ParityRoomPeer peer = new ParityRoomPeer();
                    peer.PeerId = DictionaryValue(fields, "peer_id");
                    peer.PlayerLabel = DecodePayloadField(fields, "player", 256);
                    peer.ManifestText = DecodePayloadField(fields, "manifest", ParityRoomSession.MaxFieldChars);
                    peer.OosSummaryText = DecodePayloadField(fields, "oos_summary", ParityRoomSession.MaxFieldChars);
                    peer.OosMetadataText = DecodePayloadField(fields, "oos_metadata", ParityRoomSession.MaxFieldChars);
                    peer.OosDeepReportText = DecodePayloadField(fields, "oos_deep_report", ParityRoomSession.MaxFieldChars);
                    peer.OosRecoveryRunbookText = DecodePayloadField(fields, "oos_runbook", ParityRoomSession.MaxFieldChars);
                    peer.OosContaminationText = DecodePayloadField(fields, "oos_contamination", ParityRoomSession.MaxFieldChars);
                    peer.OosSaveDumpText = DecodePayloadField(fields, "oos_save_dump", ParityRoomSession.MaxFieldChars);
                    peer.OosModifierDumpText = DecodePayloadField(fields, "oos_modifier_dump", ParityRoomSession.MaxFieldChars);
                    peer.OosErrorLogText = DecodePayloadField(fields, "oos_error_log", ParityRoomSession.MaxFieldChars);
                    peer.Endpoint = NullText(client.Client.RemoteEndPoint == null ? "" : client.Client.RemoteEndPoint.ToString());
                    peer.ReceivedUtc = DateTime.UtcNow;
                    if (String.IsNullOrWhiteSpace(peer.PlayerLabel))
                        peer.PlayerLabel = peer.Endpoint;
                    UpdateParityRoomPeerDerivedData(peer);

                    lock (session.Sync)
                    {
                        int index = session.Peers.FindIndex(delegate (ParityRoomPeer existing)
                        {
                            return String.Equals(existing.PeerId, peer.PeerId, StringComparison.OrdinalIgnoreCase);
                        });
                        if (index >= 0)
                        {
                            ParityRoomPeer existing = session.Peers[index];
                            if (String.IsNullOrWhiteSpace(peer.ManifestText))
                                peer.ManifestText = existing.ManifestText;
                            if (String.IsNullOrWhiteSpace(peer.OosSummaryText))
                                peer.OosSummaryText = existing.OosSummaryText;
                            if (String.IsNullOrWhiteSpace(peer.OosMetadataText))
                                peer.OosMetadataText = existing.OosMetadataText;
                            if (String.IsNullOrWhiteSpace(peer.OosDeepReportText))
                                peer.OosDeepReportText = existing.OosDeepReportText;
                            if (String.IsNullOrWhiteSpace(peer.OosRecoveryRunbookText))
                                peer.OosRecoveryRunbookText = existing.OosRecoveryRunbookText;
                            if (String.IsNullOrWhiteSpace(peer.OosContaminationText))
                                peer.OosContaminationText = existing.OosContaminationText;
                            if (String.IsNullOrWhiteSpace(peer.OosSaveDumpText))
                                peer.OosSaveDumpText = existing.OosSaveDumpText;
                            if (String.IsNullOrWhiteSpace(peer.OosModifierDumpText))
                                peer.OosModifierDumpText = existing.OosModifierDumpText;
                            if (String.IsNullOrWhiteSpace(peer.OosErrorLogText))
                                peer.OosErrorLogText = existing.OosErrorLogText;
                            session.Peers[index] = peer;
                        }
                        else if (session.Peers.Count < ParityRoomSession.MaxPeers)
                            session.Peers.Add(peer);
                        else
                            throw new InvalidOperationException("room peer limit reached");
                    }

                    WriteParityRoomMessage(stream, "OK\nreceived", session.SharedSecret);
                    if (onPeerChanged != null)
                        onPeerChanged();
                }
                catch (Exception ex)
                {
                    try
                    {
                        WriteParityRoomMessage(stream, "ERROR\n" + ex.Message, session.SharedSecret);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private bool TryConsumeParityRoomRateLimit(ParityRoomSession session, TcpClient client)
        {
            string endpoint = client != null && client.Client.RemoteEndPoint != null
                ? client.Client.RemoteEndPoint.ToString().Split(':')[0]
                : "unknown";
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-1);
            lock (session.Sync)
            {
                Queue<DateTime> requests;
                if (!session.RequestTimesByEndpoint.TryGetValue(endpoint, out requests))
                {
                    requests = new Queue<DateTime>();
                    session.RequestTimesByEndpoint[endpoint] = requests;
                }
                while (requests.Count > 0 && requests.Peek() < cutoff)
                    requests.Dequeue();
                if (requests.Count >= ParityRoomSession.MaxRequestsPerMinute)
                    return false;
                requests.Enqueue(DateTime.UtcNow);
                return true;
            }
        }

        private void SendParityRoomPayload(ParityRoomSession session, bool includeManifest, bool includeOos)
        {
            if (!session.Joined)
                throw new InvalidOperationException("Join a room first.");

            string payload = BuildParityRoomPayload(session, session.LocalPlayerLabel, includeManifest, includeOos);
            using (TcpClient client = new TcpClient())
            {
                client.ReceiveTimeout = ParityRoomSession.SocketTimeoutMs;
                client.SendTimeout = ParityRoomSession.SocketTimeoutMs;
                client.Connect(session.JoinHost, session.JoinPort);
                using (NetworkStream stream = client.GetStream())
                {
                    WriteParityRoomMessage(stream, payload, session.SharedSecret);
                    client.Client.Shutdown(SocketShutdown.Send);
                    string reply = ReadParityRoomMessage(stream, session.SharedSecret);
                    if (reply.IndexOf("OK", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException("Host rejected the parity room payload.");
                }
            }
        }

        private string BuildParityRoomPayload(string playerLabel, bool includeManifest, bool includeOos)
        {
            ParityRoomSession temp = new ParityRoomSession();
            RefreshParityRoomLocalState(temp, includeManifest, includeOos);
            string manifestText = includeManifest ? temp.LocalManifestText : "";
            string oosSummary = includeOos ? temp.LocalOosSummaryText : "";
            string oosMetadata = includeOos ? temp.LocalOosMetadataText : "";
            string oosDeepReport = includeOos ? temp.LocalOosDeepReportText : "";
            string oosRunbook = includeOos ? temp.LocalOosRunbookText : "";
            string oosContamination = includeOos ? temp.LocalOosContaminationText : "";
            string oosSaveDump = includeOos ? temp.LocalOosSaveDumpText : "";
            string oosModifierDump = includeOos ? temp.LocalOosModifierDumpText : "";
            string oosErrorLog = includeOos ? temp.LocalOosErrorLogText : "";

            return BuildParityRoomPayloadText(
                "",
                "",
                "",
                playerLabel,
                manifestText,
                oosSummary,
                oosMetadata,
                oosDeepReport,
                oosRunbook,
                oosContamination,
                oosSaveDump,
                oosModifierDump,
                oosErrorLog,
                "");
        }

        private string BuildParityRoomPayload(ParityRoomSession session, string playerLabel, bool includeManifest, bool includeOos)
        {
            if (session != null)
                RefreshParityRoomLocalState(session, includeManifest, includeOos);

            string manifestText = includeManifest && session != null ? session.LocalManifestText : "";
            bool includeReports = includeOos && session != null && session.OosReportsShareConsented;
            bool includeRaw = includeOos && session != null && session.RawOosDumpsShareConsented;
            string oosSummary = includeReports ? session.LocalOosSummaryText : "";
            string oosMetadata = includeReports ? session.LocalOosMetadataText : "";
            string oosDeepReport = includeReports ? session.LocalOosDeepReportText : "";
            string oosRunbook = includeReports ? session.LocalOosRunbookText : "";
            string oosContamination = includeReports ? session.LocalOosContaminationText : "";
            string oosSaveDump = includeRaw ? session.LocalOosSaveDumpText : "";
            string oosModifierDump = includeRaw ? session.LocalOosModifierDumpText : "";
            string oosErrorLog = includeRaw ? session.LocalOosErrorLogText : "";

            string createdUtc = DateTime.UtcNow.ToString("o");
            string nonce = GenerateParityRoomNonce();
            string signature = ComputeParityRoomSignatureV1(
                session.SharedSecret,
                ParityRoomSession.ProtocolVersion,
                ParityRoomSession.MessageType,
                session.LocalPeerId,
                session.RoomCode,
                createdUtc,
                nonce,
                playerLabel,
                manifestText,
                oosSummary,
                oosMetadata,
                oosDeepReport,
                oosRunbook,
                oosContamination,
                oosSaveDump,
                oosModifierDump,
                oosErrorLog);
            return BuildParityRoomPayloadTextV1(
                ParityRoomSession.ProtocolVersion,
                ParityRoomSession.MessageType,
                session.LocalPeerId,
                session.RoomCode,
                createdUtc,
                nonce,
                playerLabel,
                manifestText,
                oosSummary,
                oosMetadata,
                oosDeepReport,
                oosRunbook,
                oosContamination,
                oosSaveDump,
                oosModifierDump,
                oosErrorLog,
                signature);
        }

        private bool ShowParityJoinDialog(IWin32Window owner, out string host, out int port, out string code, out string secret, out string playerLabel)
        {
            host = "";
            port = 0;
            code = "";
            secret = "";
            playerLabel = "";

            using (Form dialog = new Form())
            {
                dialog.Text = "Join parity room";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Size = new Size(420, 290);
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;

                Label hostLabel = new Label { Text = "Host", Left = 16, Top = 20, Width = 100 };
                TextBox hostBox = new TextBox { Left = 130, Top = 16, Width = 240, Text = DetectPrimaryIpv4Address() };
                Label portLabel = new Label { Text = "Port", Left = 16, Top = 58, Width = 100 };
                TextBox portBox = new TextBox { Left = 130, Top = 54, Width = 120 };
                Label codeLabel = new Label { Text = "Room code", Left = 16, Top = 96, Width = 100 };
                TextBox codeBox = new TextBox { Left = 130, Top = 92, Width = 120 };
                Label secretLabel = new Label { Text = "Room secret", Left = 16, Top = 134, Width = 100 };
                TextBox secretBox = new TextBox { Left = 130, Top = 130, Width = 240 };
                Label playerLabelCaption = new Label { Text = "Player label", Left = 16, Top = 172, Width = 100 };
                TextBox playerBox = new TextBox { Left = 130, Top = 168, Width = 240, Text = Environment.UserName };
                Button okButton = new Button { Text = "Join", Left = 214, Top = 208, Width = 74, DialogResult = DialogResult.OK };
                Button cancelButton = new Button { Text = "Cancel", Left = 296, Top = 208, Width = 74, DialogResult = DialogResult.Cancel };

                dialog.Controls.Add(hostLabel);
                dialog.Controls.Add(hostBox);
                dialog.Controls.Add(portLabel);
                dialog.Controls.Add(portBox);
                dialog.Controls.Add(codeLabel);
                dialog.Controls.Add(codeBox);
                dialog.Controls.Add(secretLabel);
                dialog.Controls.Add(secretBox);
                dialog.Controls.Add(playerLabelCaption);
                dialog.Controls.Add(playerBox);
                dialog.Controls.Add(okButton);
                dialog.Controls.Add(cancelButton);
                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;

                if (dialog.ShowDialog(owner) != DialogResult.OK)
                    return false;

                int parsedPort;
                if (!Int32.TryParse(portBox.Text.Trim(), out parsedPort) || parsedPort <= 0 || parsedPort > 65535)
                {
                    MessageBox.Show("Enter a valid port.", "CK3MPS parity room", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                host = hostBox.Text.Trim();
                port = parsedPort;
                code = codeBox.Text.Trim();
                secret = secretBox.Text.Trim();
                playerLabel = playerBox.Text.Trim();
                return !String.IsNullOrWhiteSpace(host) && !String.IsNullOrWhiteSpace(code) && !String.IsNullOrWhiteSpace(secret);
            }
        }

        private void BuildParityComparisonData(ParityManifestRecord local, List<ParityManifestRecord> others, List<string> oosNotes, out List<string> findings, out List<string> actions, out List<ParityDifferenceRow> rows, out bool safeToStart)
        {
            findings = new List<string>();
            actions = new List<string>();
            rows = new List<ParityDifferenceRow>();
            safeToStart = true;

            string[] strictKeys = new[]
            {
                "Installed version",
                "Steam build",
                "Steam branch",
                "Launch options",
                "Local parity fingerprint",
                "DLC loadout clean",
                "DLC footprint fingerprint",
                "Steam Workshop fingerprint",
                "No active mods",
                "No disabled DLCs",
                "ck3.exe",
                "launcher-settings.json",
                "dlc_load.json",
                "pdx_settings.txt"
            };

            string[] saveKeys = new[]
            {
                "Best clean manual save candidate",
                "Best clean manual save hash",
                "Best clean manual save version",
                "Best clean manual save readable",
                "Active continue title"
            };

            foreach (ParityManifestRecord other in others)
            {
                List<string> playerFindings = new List<string>();
                CompareParityKeys(local, other, strictKeys, playerFindings, rows);
                CompareParityKeys(local, other, saveKeys, playerFindings, rows);
                CompareParityBooleans(local, other, "Installed/save version parity", playerFindings, rows);
                CompareParityBooleans(local, other, "-noasync", playerFindings, rows);
                CompareParityBooleans(local, other, "risky launch options absent", playerFindings, rows);

                if (playerFindings.Count == 0)
                {
                    findings.Add("OK: " + other.PlayerLabel + " matches the host parity manifest.");
                    rows.Add(new ParityDifferenceRow
                    {
                        Player = other.PlayerLabel,
                        Area = "All parity checks",
                        HostValue = "match",
                        PlayerValue = "match",
                        Status = "OK"
                    });
                }
                else
                {
                    safeToStart = false;
                    findings.AddRange(playerFindings);
                }
            }

            if (oosNotes != null && oosNotes.Count > 0 && !ContainsParityFinding(findings, "mismatch"))
                actions.Add("Parity looks clean. Focus on OOS recovery: compare OOS metadata and prefer rehost/rollback over profile fixes.");
            if (oosNotes != null && oosNotes.Count > 0)
                safeToStart = false;

            BuildParityRecommendations(findings, rows, oosNotes, actions);
        }

        private string GenerateParityRoomCode()
        {
            return DateTime.Now.ToString("HHmmss");
        }

        private string GenerateParityRoomSecret()
        {
            byte[] bytes = new byte[24];
            using (System.Security.Cryptography.RandomNumberGenerator rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private string DetectPrimaryIpv4Address()
        {
            try
            {
                foreach (IPAddress address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                        return address.ToString();
                }
            }
            catch
            {
            }

            return "127.0.0.1";
        }

        private Dictionary<string, string> ParseParityRoomPayload(string text)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in NullText(text).Replace("\r\n", "\n").Split('\n'))
            {
                int separator = rawLine.IndexOf('=');
                if (separator <= 0)
                    continue;
                string key = rawLine.Substring(0, separator).Trim();
                string value = rawLine.Substring(separator + 1).Trim();
                fields[key] = value;
            }
            return fields;
        }

        private bool ValidateAndRememberParityRoomPayload(ParityRoomSession session, Dictionary<string, string> fields, out string error)
        {
            error = "invalid parity payload";
            if (session == null)
            {
                error = "session unavailable";
                return false;
            }

            if (!String.Equals(DictionaryValue(fields, "protocol_version"), ParityRoomSession.ProtocolVersion, StringComparison.Ordinal)
                || !String.Equals(DictionaryValue(fields, "message_type"), ParityRoomSession.MessageType, StringComparison.Ordinal))
            {
                error = "unsupported protocol or message type";
                return false;
            }

            string peerId = DictionaryValue(fields, "peer_id");
            Guid parsedPeerId;
            if (!Guid.TryParseExact(peerId, "N", out parsedPeerId))
            {
                error = "bad peer id";
                return false;
            }

            string code = DictionaryValue(fields, "room_code");
            if (!String.Equals(code, session.RoomCode, StringComparison.Ordinal))
            {
                error = "bad room code";
                return false;
            }

            string createdUtcText = DictionaryValue(fields, "created_utc");
            DateTime createdUtc;
            if (!DateTime.TryParse(createdUtcText, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out createdUtc))
            {
                error = "bad timestamp";
                return false;
            }

            if (Math.Abs((DateTime.UtcNow - createdUtc).TotalMinutes) > ParityRoomSession.MaxReplayAgeMinutes)
            {
                error = "stale payload";
                return false;
            }

            string nonce = DictionaryValue(fields, "nonce");
            if (String.IsNullOrWhiteSpace(nonce) || nonce.Length > 128)
            {
                error = "bad nonce";
                return false;
            }

            string expectedSignature = ComputeParityRoomSignatureV1(
                session.SharedSecret,
                DictionaryValue(fields, "protocol_version"),
                DictionaryValue(fields, "message_type"),
                peerId,
                code,
                createdUtcText,
                nonce,
                DecodePayloadField(fields, "player", 256),
                DecodePayloadField(fields, "manifest", ParityRoomSession.MaxFieldChars),
                DecodePayloadField(fields, "oos_summary", ParityRoomSession.MaxFieldChars),
                DecodePayloadField(fields, "oos_metadata", ParityRoomSession.MaxFieldChars),
                DecodePayloadField(fields, "oos_deep_report", ParityRoomSession.MaxFieldChars),
                DecodePayloadField(fields, "oos_runbook", ParityRoomSession.MaxFieldChars),
                DecodePayloadField(fields, "oos_contamination", ParityRoomSession.MaxFieldChars),
                DecodePayloadField(fields, "oos_save_dump", ParityRoomSession.MaxFieldChars),
                DecodePayloadField(fields, "oos_modifier_dump", ParityRoomSession.MaxFieldChars),
                DecodePayloadField(fields, "oos_error_log", ParityRoomSession.MaxFieldChars));
            string providedSignature = DictionaryValue(fields, "signature");
            if (!FixedTimeEquals(expectedSignature, providedSignature))
            {
                error = "bad signature";
                return false;
            }

            lock (session.Sync)
            {
                if (session.SeenPayloadNonces.Contains(nonce))
                {
                    error = "replayed payload";
                    return false;
                }

                session.SeenPayloadNonces.Add(nonce);
                session.SeenPayloadNonceOrder.Enqueue(nonce);
                while (session.SeenPayloadNonceOrder.Count > ParityRoomSession.MaxReplayNonces)
                {
                    string oldest = session.SeenPayloadNonceOrder.Dequeue();
                    session.SeenPayloadNonces.Remove(oldest);
                }
            }

            error = "";
            return true;
        }

        private string ReadParityRoomMessage(NetworkStream stream, string sharedSecret)
        {
            if (stream == null)
                throw new InvalidOperationException("Parity room stream is unavailable.");

            byte[] lengthBytes = ReadExactBytes(stream, 4);
            int length = BitConverter.ToInt32(lengthBytes, 0);
            if (length < 0 || length > ParityRoomSession.MaxPayloadBytes)
                throw new InvalidOperationException("Parity room payload is too large.");
            byte[] packetBytes = ReadExactBytes(stream, length);
            return UnprotectParityRoomPayload(packetBytes, sharedSecret);
        }

        private void WriteParityRoomMessage(NetworkStream stream, string payload, string sharedSecret)
        {
            if (stream == null)
                throw new InvalidOperationException("Parity room stream is unavailable.");

            byte[] packetBytes = ProtectParityRoomPayload(NullText(payload), sharedSecret);
            if (packetBytes.Length > ParityRoomSession.MaxPayloadBytes)
                throw new InvalidOperationException("Parity room payload exceeds the size limit.");

            byte[] lengthBytes = BitConverter.GetBytes(packetBytes.Length);
            stream.Write(lengthBytes, 0, lengthBytes.Length);
            if (packetBytes.Length > 0)
                stream.Write(packetBytes, 0, packetBytes.Length);
            stream.Flush();
        }

        private byte[] ReadExactBytes(Stream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read <= 0)
                    throw new EndOfStreamException("Parity room connection closed before the payload was complete.");
                offset += read;
            }
            return buffer;
        }

        private string EncodePayloadField(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(NullText(text)));
        }

        private byte[] ProtectParityRoomPayload(string payload, string sharedSecret)
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(NullText(payload));
            byte[] encryptionKey = DeriveParityRoomKey(sharedSecret, "enc");
            byte[] authKey = DeriveParityRoomKey(sharedSecret, "auth");
            byte[] iv = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                rng.GetBytes(iv);

            byte[] cipherBytes;
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encryptionKey;
                aes.IV = iv;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                    cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            }

            byte[] packetWithoutMac = new byte[1 + iv.Length + cipherBytes.Length];
            packetWithoutMac[0] = 1;
            Buffer.BlockCopy(iv, 0, packetWithoutMac, 1, iv.Length);
            Buffer.BlockCopy(cipherBytes, 0, packetWithoutMac, 1 + iv.Length, cipherBytes.Length);

            byte[] mac;
            using (HMACSHA256 hmac = new HMACSHA256(authKey))
                mac = hmac.ComputeHash(packetWithoutMac);

            byte[] packet = new byte[packetWithoutMac.Length + mac.Length];
            Buffer.BlockCopy(packetWithoutMac, 0, packet, 0, packetWithoutMac.Length);
            Buffer.BlockCopy(mac, 0, packet, packetWithoutMac.Length, mac.Length);
            return packet;
        }

        private string UnprotectParityRoomPayload(byte[] packetBytes, string sharedSecret)
        {
            byte[] packet = packetBytes ?? new byte[0];
            if (packet.Length < 1 + 16 + 32)
                throw new InvalidOperationException("Parity room packet is malformed.");
            if (packet[0] != 1)
                throw new InvalidOperationException("Parity room packet version is not supported.");

            byte[] authKey = DeriveParityRoomKey(sharedSecret, "auth");
            int macOffset = packet.Length - 32;
            byte[] expectedMac;
            using (HMACSHA256 hmac = new HMACSHA256(authKey))
                expectedMac = hmac.ComputeHash(packet, 0, macOffset);

            byte[] providedMac = new byte[32];
            Buffer.BlockCopy(packet, macOffset, providedMac, 0, providedMac.Length);
            if (!FixedTimeEqualsBytes(expectedMac, providedMac))
                throw new InvalidOperationException("Parity room packet authentication failed.");

            byte[] encryptionKey = DeriveParityRoomKey(sharedSecret, "enc");
            byte[] iv = new byte[16];
            Buffer.BlockCopy(packet, 1, iv, 0, iv.Length);
            int cipherOffset = 1 + iv.Length;
            int cipherLength = macOffset - cipherOffset;
            if (cipherLength < 0)
                throw new InvalidOperationException("Parity room packet cipher section is malformed.");

            byte[] cipherBytes = new byte[cipherLength];
            Buffer.BlockCopy(packet, cipherOffset, cipherBytes, 0, cipherLength);
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encryptionKey;
                aes.IV = iv;
                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                    if (plainBytes.Length > ParityRoomSession.MaxPayloadBytes)
                        throw new InvalidOperationException("Parity room decrypted payload exceeds the size limit.");
                    return Encoding.UTF8.GetString(plainBytes);
                }
            }
        }

        private byte[] DeriveParityRoomKey(string sharedSecret, string label)
        {
            byte[] salt = Encoding.UTF8.GetBytes("CK3MPS parity room HKDF-SHA256 v1");
            byte[] inputKey = Encoding.UTF8.GetBytes(NullText(sharedSecret));
            byte[] pseudoRandomKey;
            using (HMACSHA256 extract = new HMACSHA256(salt))
                pseudoRandomKey = extract.ComputeHash(inputKey);
            byte[] info = Encoding.UTF8.GetBytes(NullText(label) + "\x01");
            using (HMACSHA256 expand = new HMACSHA256(pseudoRandomKey))
                return expand.ComputeHash(info);
        }

        private string BuildParityRoomPayloadText(string roomCode, string createdUtc, string nonce, string playerLabel, string manifestText, string oosSummary, string oosMetadata, string oosDeepReport, string oosRunbook, string oosContamination, string oosSaveDump, string oosModifierDump, string oosErrorLog, string signature)
        {
            return BuildParityRoomPayloadTextV1(
                ParityRoomSession.ProtocolVersion,
                ParityRoomSession.MessageType,
                LegacyParityPeerId(playerLabel),
                roomCode,
                createdUtc,
                nonce,
                playerLabel,
                manifestText,
                oosSummary,
                oosMetadata,
                oosDeepReport,
                oosRunbook,
                oosContamination,
                oosSaveDump,
                oosModifierDump,
                oosErrorLog,
                signature);
        }

        private string BuildParityRoomPayloadTextV1(string protocolVersion, string messageType, string peerId, string roomCode, string createdUtc, string nonce, string playerLabel, string manifestText, string oosSummary, string oosMetadata, string oosDeepReport, string oosRunbook, string oosContamination, string oosSaveDump, string oosModifierDump, string oosErrorLog, string signature)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("protocol_version=" + NullText(protocolVersion));
            sb.AppendLine("message_type=" + NullText(messageType));
            sb.AppendLine("peer_id=" + NullText(peerId));
            if (!String.IsNullOrWhiteSpace(roomCode))
                sb.AppendLine("room_code=" + roomCode);
            if (!String.IsNullOrWhiteSpace(createdUtc))
                sb.AppendLine("created_utc=" + createdUtc);
            if (!String.IsNullOrWhiteSpace(nonce))
                sb.AppendLine("nonce=" + nonce);
            sb.AppendLine("player=" + EncodePayloadField(playerLabel));
            sb.AppendLine("manifest=" + EncodePayloadField(manifestText));
            sb.AppendLine("oos_summary=" + EncodePayloadField(oosSummary));
            sb.AppendLine("oos_metadata=" + EncodePayloadField(oosMetadata));
            sb.AppendLine("oos_deep_report=" + EncodePayloadField(oosDeepReport));
            sb.AppendLine("oos_runbook=" + EncodePayloadField(oosRunbook));
            sb.AppendLine("oos_contamination=" + EncodePayloadField(oosContamination));
            sb.AppendLine("oos_save_dump=" + EncodePayloadField(oosSaveDump));
            sb.AppendLine("oos_modifier_dump=" + EncodePayloadField(oosModifierDump));
            sb.AppendLine("oos_error_log=" + EncodePayloadField(oosErrorLog));
            if (!String.IsNullOrWhiteSpace(signature))
                sb.AppendLine("signature=" + signature);
            return sb.ToString();
        }

        private string GenerateParityRoomNonce()
        {
            byte[] bytes = new byte[18];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private string ComputeParityRoomSignature(string secret, string roomCode, string createdUtc, string nonce, string playerLabel, string manifestText, string oosSummary, string oosMetadata, string oosDeepReport, string oosRunbook, string oosContamination, string oosSaveDump, string oosModifierDump, string oosErrorLog)
        {
            return ComputeParityRoomSignatureV1(
                secret,
                ParityRoomSession.ProtocolVersion,
                ParityRoomSession.MessageType,
                LegacyParityPeerId(playerLabel),
                roomCode,
                createdUtc,
                nonce,
                playerLabel,
                manifestText,
                oosSummary,
                oosMetadata,
                oosDeepReport,
                oosRunbook,
                oosContamination,
                oosSaveDump,
                oosModifierDump,
                oosErrorLog);
        }

        private string ComputeParityRoomSignatureV1(string secret, string protocolVersion, string messageType, string peerId, string roomCode, string createdUtc, string nonce, string playerLabel, string manifestText, string oosSummary, string oosMetadata, string oosDeepReport, string oosRunbook, string oosContamination, string oosSaveDump, string oosModifierDump, string oosErrorLog)
        {
            string canonical =
                "protocol_version=" + NullText(protocolVersion) + "\n" +
                "message_type=" + NullText(messageType) + "\n" +
                "peer_id=" + NullText(peerId) + "\n" +
                "room_code=" + NullText(roomCode) + "\n" +
                "created_utc=" + NullText(createdUtc) + "\n" +
                "nonce=" + NullText(nonce) + "\n" +
                "player=" + NullText(playerLabel) + "\n" +
                "manifest=" + NullText(manifestText) + "\n" +
                "oos_summary=" + NullText(oosSummary) + "\n" +
                "oos_metadata=" + NullText(oosMetadata) + "\n" +
                "oos_deep_report=" + NullText(oosDeepReport) + "\n" +
                "oos_runbook=" + NullText(oosRunbook) + "\n" +
                "oos_contamination=" + NullText(oosContamination) + "\n" +
                "oos_save_dump=" + NullText(oosSaveDump) + "\n" +
                "oos_modifier_dump=" + NullText(oosModifierDump) + "\n" +
                "oos_error_log=" + NullText(oosErrorLog);

            using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(NullText(secret))))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
        }

        private string LegacyParityPeerId(string playerLabel)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes("CK3MPS legacy peer|" + NullText(playerLabel)));
                byte[] id = new byte[16];
                Array.Copy(hash, id, id.Length);
                return new Guid(id).ToString("N");
            }
        }

        private bool FixedTimeEquals(string left, string right)
        {
            byte[] leftBytes = Encoding.UTF8.GetBytes(NullText(left));
            byte[] rightBytes = Encoding.UTF8.GetBytes(NullText(right));
            return FixedTimeEqualsBytes(leftBytes, rightBytes);
        }

        private bool FixedTimeEqualsBytes(byte[] leftBytes, byte[] rightBytes)
        {
            byte[] left = leftBytes ?? new byte[0];
            byte[] right = rightBytes ?? new byte[0];
            if (left.Length != right.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < left.Length; i++)
                diff |= left[i] ^ right[i];
            return diff == 0;
        }

        private string DecodePayloadField(Dictionary<string, string> fields, string key, int maxChars)
        {
            string value = DictionaryValue(fields, key);
            if (String.IsNullOrWhiteSpace(value))
                return "";
            long maxEncodedChars = (((long)maxChars * 4L + 2L) / 3L) * 4L;
            if (value.Length > maxEncodedChars)
                throw new InvalidOperationException("Parity room encoded field exceeds the allowed size: " + key);
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("Parity room field is not valid Base64: " + key);
            }
            string decoded = new UTF8Encoding(false, true).GetString(bytes);
            if (decoded.Length > maxChars)
                throw new InvalidOperationException("Parity room field exceeds the allowed size: " + key);
            return decoded;
        }

        private string DictionaryValue(Dictionary<string, string> fields, string key)
        {
            string value;
            return fields != null && fields.TryGetValue(key, out value) ? value : "";
        }

        private string ReadTextIfExists(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "";

            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    char[] buffer = new char[8192];
                    int remaining = MaxOosTextReadBytes;
                    StringBuilder sb = new StringBuilder();
                    while (remaining > 0)
                    {
                        int read = reader.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                        if (read <= 0)
                            break;
                        sb.Append(buffer, 0, read);
                        remaining -= read;
                    }
                    if (!reader.EndOfStream)
                        sb.Append(Environment.NewLine).Append("[truncated by CK3MPS]");
                    return sb.ToString();
                }
            }
            catch
            {
                return "";
            }
        }

        private string ReadTrimmedTextIfExists(string path, int maxChars)
        {
            string text = ReadTextIfExists(path);
            if (maxChars > 0 && text.Length > maxChars)
                return text.Substring(0, maxChars) + Environment.NewLine + "[truncated by CK3MPS parity room]";
            return text;
        }

        private ParityManifestRecord ParseParityManifest(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Parity manifest not found.", path);

            return ParseParityManifestText(File.ReadAllText(path, Encoding.UTF8), path, "");
        }

        private ParityManifestRecord ParseParityManifestText(string text, string path, string fallbackLabel)
        {
            ParityManifestRecord record = new ParityManifestRecord();
            record.Path = path;
            record.PlayerLabel = String.IsNullOrWhiteSpace(fallbackLabel)
                ? Path.GetFileNameWithoutExtension(path)
                : fallbackLabel;
            foreach (string rawLine in NullText(text).Replace("\r\n", "\n").Split('\n'))
            {
                string line = NullText(rawLine).Trim();
                if (!line.StartsWith("-", StringComparison.Ordinal))
                    continue;

                int colon = line.IndexOf(':');
                if (colon <= 2)
                    continue;

                string key = line.Substring(1, colon - 1).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (key.Length == 0)
                    continue;

                record.Values[key] = value;
            }

            string bestSavePlayer = ParityValue(record, "Best clean manual save player");
            string continueTitle = ParityValue(record, "Active continue title");
            if (!String.IsNullOrWhiteSpace(bestSavePlayer))
                record.PlayerLabel = bestSavePlayer;
            else if (!String.IsNullOrWhiteSpace(continueTitle))
                record.PlayerLabel = continueTitle;
            return record;
        }

        private string BuildParityComparisonReport(ParityManifestRecord local, List<ParityManifestRecord> others)
        {
            List<string> findings = new List<string>();
            List<string> actions = new List<string>();
            List<ParityDifferenceRow> rows = new List<ParityDifferenceRow>();
            bool safeToStart;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Parity comparison");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Host manifest");
            sb.AppendLine("- Player label: " + local.PlayerLabel);
            sb.AppendLine("- File: " + local.Path);
            sb.AppendLine("- Installed version: " + ParityValue(local, "Installed version"));
            sb.AppendLine("- Steam build: " + ParityValue(local, "Steam build"));
            sb.AppendLine("- Steam branch: " + ParityValue(local, "Steam branch"));
            sb.AppendLine("- Local parity fingerprint: " + ParityValue(local, "Local parity fingerprint"));
            sb.AppendLine();
            sb.AppendLine("Compared players");
            foreach (ParityManifestRecord other in others)
                sb.AppendLine("- " + other.PlayerLabel + " | " + other.Path);
            sb.AppendLine();

            BuildParityComparisonData(local, others, null, out findings, out actions, out rows, out safeToStart);

            sb.AppendLine("Result");
            sb.AppendLine("- Verdict: " + (safeToStart ? "SAFE TO START" : "UNSAFE TO START"));
            if (findings.Count == 0)
            {
                sb.AppendLine("- OK: all selected manifests match the host baseline.");
            }
            else
            {
                foreach (string finding in findings)
                    sb.AppendLine("- " + finding);
            }
            if (rows.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Table");
                foreach (ParityDifferenceRow row in rows)
                    sb.AppendLine("- " + row.Player + " | " + row.Area + " | host=" + row.HostValue + " | player=" + row.PlayerValue + " | " + row.Status);
            }
            sb.AppendLine();
            sb.AppendLine("Recommended actions");
            if (actions.Count == 0)
            {
                sb.AppendLine("- No blockers found. You can compare the selected save and start from a fresh lobby.");
            }
            else
            {
                foreach (string action in actions)
                    sb.AppendLine("- " + action);
            }

            return sb.ToString();
        }

        private void PopulateParityDifferenceGrid(DataGridView grid, List<ParityDifferenceRow> rows)
        {
            if (grid == null)
                return;

            grid.SuspendLayout();
            grid.Rows.Clear();
            List<ParityDifferenceRow> safeRows = rows ?? new List<ParityDifferenceRow>();
            safeRows.Sort(delegate (ParityDifferenceRow left, ParityDifferenceRow right)
            {
                int byStatus = GetParityRowSortRank(left.Status).CompareTo(GetParityRowSortRank(right.Status));
                if (byStatus != 0)
                    return byStatus;

                int byPlayer = StringComparer.OrdinalIgnoreCase.Compare(left.Player, right.Player);
                if (byPlayer != 0)
                    return byPlayer;

                return StringComparer.OrdinalIgnoreCase.Compare(left.Area, right.Area);
            });
            if (safeRows.Count == 0)
            {
                int emptyIndex = grid.Rows.Add("No players", "No parity data yet", "-", "-", "Wait");
                grid.Rows[emptyIndex].DefaultCellStyle.BackColor = Color.FromArgb(250, 244, 224);
                return;
            }

            foreach (ParityDifferenceRow row in safeRows)
            {
                int index = grid.Rows.Add(
                    FormatParityPlayerLabel(row.Player),
                    ShortParityArea(row.Area),
                    ShortParityCellValue(row.Area, row.HostValue),
                    ShortParityCellValue(row.Area, row.PlayerValue),
                    NormalizeParityStatus(row.Status));
                DataGridViewRow gridRow = grid.Rows[index];
                string normalizedStatus = NormalizeParityStatus(row.Status);
                if (String.Equals(normalizedStatus, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    gridRow.DefaultCellStyle.BackColor = Color.FromArgb(232, 247, 236);
                    gridRow.DefaultCellStyle.ForeColor = Color.FromArgb(18, 110, 44);
                }
                else if (String.Equals(normalizedStatus, "Fix", StringComparison.OrdinalIgnoreCase))
                {
                    gridRow.DefaultCellStyle.BackColor = Color.FromArgb(252, 235, 235);
                    gridRow.DefaultCellStyle.ForeColor = Color.FromArgb(148, 36, 36);
                }
                else
                {
                    gridRow.DefaultCellStyle.BackColor = Color.FromArgb(250, 244, 224);
                    gridRow.DefaultCellStyle.ForeColor = Color.FromArgb(130, 90, 24);
                }
            }

            grid.ClearSelection();
            grid.ResumeLayout();
        }

        private string BuildParityDifferenceGridReport(DataGridView grid)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Differences");
            if (grid == null || grid.Rows.Count == 0)
            {
                sb.AppendLine("- No parity data yet.");
                return sb.ToString();
            }

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow)
                    continue;

                sb.AppendLine("- "
                    + NullText(row.Cells["Player"].Value == null ? "" : row.Cells["Player"].Value.ToString())
                    + " | "
                    + NullText(row.Cells["Area"].Value == null ? "" : row.Cells["Area"].Value.ToString())
                    + " | host="
                    + NullText(row.Cells["HostValue"].Value == null ? "" : row.Cells["HostValue"].Value.ToString())
                    + " | player="
                    + NullText(row.Cells["PlayerValue"].Value == null ? "" : row.Cells["PlayerValue"].Value.ToString())
                    + " | "
                    + NullText(row.Cells["Status"].Value == null ? "" : row.Cells["Status"].Value.ToString()));
            }

            return sb.ToString();
        }

        private void BuildParityRoomRecoveryActions(OosDeepInsight localInsight, List<ParityRoomPeer> peers, List<string> diagnosisNotes, List<string> actions, ref bool safeToStart)
        {
            if (localInsight == null)
                localInsight = new OosDeepInsight();

            if (!String.IsNullOrWhiteSpace(localInsight.OosType))
            {
                safeToStart = false;
                if (localInsight.HotjoinForbidden)
                    AddParityAction(actions, "Host must do: controlled hotjoin is forbidden. Use " + localInsight.RecoveryPath + " for this incident.");
                if (localInsight.SessionContaminationScore >= 80)
                    AddParityAction(actions, "Host must do: session contamination is CRITICAL. Do not continue this session state; load an older clean manual save.");
                else if (localInsight.SessionContaminationScore >= 45)
                    AddParityAction(actions, "Host must do: session contamination is elevated. Stop testing fixes inside the current running session and rehost from a clean baseline.");
                if (localInsight.FailedContextSwitchCount > 0 || localInsight.NullTargetCount > 0)
                    AddParityAction(actions, "Host must do: script/AI errors are present in host OOS logs. Treat this as save-state divergence, not only parity or internet drift.");
                if (localInsight.ArmyMentions > 0)
                    AddParityAction(actions, "Host must do: army-related state appears in the OOS dump. Prefer rollback or clean rehost; do not hotjoin into the live simulation.");
                if (localInsight.ModifierMentions > 0)
                    AddParityAction(actions, "Host must do: modifier divergence appears in the OOS dump. Compare DLC/playset parity, then use rollback or full rehost.");
            }

            foreach (ParityRoomPeer peer in peers)
            {
                string path = ExtractReportValue(peer.OosDeepReportText, "Recovery path");
                string contamination = ExtractReportValue(peer.OosDeepReportText, "Session contamination");
                string hotjoin = ExtractReportValue(peer.OosDeepReportText, "Controlled hotjoin");
                string reason = ExtractReportValue(peer.OosRecoveryRunbookText, "Reason");

                if (!String.IsNullOrWhiteSpace(path) || !String.IsNullOrWhiteSpace(contamination))
                {
                    safeToStart = false;
                    AddParityAction(actions, peer.PlayerLabel + ": reported path is " + NullText(path) + " with contamination " + NullText(contamination) + ".");
                }

                if (NullText(hotjoin).IndexOf("FORBIDDEN", StringComparison.OrdinalIgnoreCase) >= 0)
                    AddParityAction(actions, peer.PlayerLabel + ": controlled hotjoin is forbidden by their OOS parser. Keep this incident on rehost/rollback only.");

                if (!String.IsNullOrWhiteSpace(reason))
                    AddParityAction(actions, peer.PlayerLabel + ": " + reason);

                if (!String.IsNullOrWhiteSpace(peer.OosRecoveryRunbookText))
                {
                    string firstRunbookStep = ExtractFirstRunbookStep(peer.OosRecoveryRunbookText);
                    if (!String.IsNullOrWhiteSpace(firstRunbookStep))
                        AddParityAction(actions, peer.PlayerLabel + ": first runbook step -> " + firstRunbookStep);
                }
            }

            if (diagnosisNotes != null && diagnosisNotes.Count > 0)
                AddParityAction(actions, "Use the shared recovery diagnosis below to decide one path for the whole lobby, then have every player follow only that path.");
        }

        private void BuildParityRoomDiagnosisText(StringBuilder sb, OosDeepInsight localInsight, List<ParityRoomPeer> peers, List<string> findings, List<string> diagnosisNotes, List<string> actions, bool safeToStart)
        {
            string confidence = BuildParityRoomConfidence(localInsight, peers, findings);
            sb.AppendLine("- Verdict: " + (safeToStart ? "SAFE TO START" : "UNSAFE TO START"));
            sb.AppendLine("- Confidence: " + confidence);
            sb.AppendLine();

            sb.AppendLine("Observed");
            if (!String.IsNullOrWhiteSpace(localInsight.OosType))
                sb.AppendLine("- Host OOS type: " + localInsight.OosType);
            if (localInsight.FailedContextSwitchCount > 0 || localInsight.NullTargetCount > 0)
                sb.AppendLine("- Host script/AI errors: failed_context=" + localInsight.FailedContextSwitchCount + ", null_target=" + localInsight.NullTargetCount);
            if (localInsight.ModifierMentions > 0 || localInsight.ArmyMentions > 0 || localInsight.CharacterMentions > 0)
                sb.AppendLine("- Host deep dump signals: characters=" + localInsight.CharacterMentions + ", modifiers=" + localInsight.ModifierMentions + ", armies=" + localInsight.ArmyMentions + ", ai=" + localInsight.AiMentions);
            if (diagnosisNotes != null && diagnosisNotes.Count > 0)
                foreach (string note in diagnosisNotes)
                    sb.AppendLine("- " + note);
            if ((diagnosisNotes == null || diagnosisNotes.Count == 0) && String.IsNullOrWhiteSpace(localInsight.OosType))
                sb.AppendLine("- No deep OOS observations were shared yet.");
            sb.AppendLine();

            sb.AppendLine("Interpreted as");
            if (!String.IsNullOrWhiteSpace(localInsight.OosType))
                sb.AppendLine("- Host recovery path: " + localInsight.RecoveryPath);
            if (localInsight.HotjoinForbidden)
                sb.AppendLine("- Controlled hotjoin is unsafe for this incident.");
            else
                sb.AppendLine("- No strong host-side block on controlled hotjoin yet.");
            if (findings != null && findings.Count > 0)
                sb.AppendLine("- There are parity mismatches between host and remote players.");
            else
                sb.AppendLine("- Shared parity looks clean so far.");
            sb.AppendLine();

            sb.AppendLine("Risk");
            if (!String.IsNullOrWhiteSpace(localInsight.OosType))
                sb.AppendLine("- Host contamination: " + localInsight.SessionContaminationLevel + " (" + localInsight.SessionContaminationScore + "/100)");
            if (localInsight.SessionContaminationScore >= 80)
                sb.AppendLine("- Continuing this exact session state is very risky.");
            else if (localInsight.SessionContaminationScore >= 45)
                sb.AppendLine("- Continuing the live session is risky unless you rehost cleanly.");
            else if (!safeToStart)
                sb.AppendLine("- Risk remains because parity or OOS evidence is incomplete.");
            else
                sb.AppendLine("- No major continuation risk is currently visible.");
            sb.AppendLine();

            sb.AppendLine("Required actions");
            if (actions == null || actions.Count == 0)
            {
                sb.AppendLine("- No blockers found. Start from the selected save and keep the first month slow.");
            }
            else
            {
                foreach (string action in actions)
                    sb.AppendLine("- " + action);
            }
            sb.AppendLine();

            sb.AppendLine("Blocked actions");
            List<string> blocked = BuildParityRoomBlockedActions(localInsight, peers, findings, safeToStart);
            if (blocked.Count == 0)
                sb.AppendLine("- No explicit blocked actions.");
            else
                foreach (string line in blocked)
                    sb.AppendLine("- " + line);
        }

        private string BuildParityRoomConfidence(OosDeepInsight localInsight, List<ParityRoomPeer> peers, List<string> findings)
        {
            int score = 0;
            if (localInsight != null && !String.IsNullOrWhiteSpace(localInsight.OosType))
                score += 2;
            if (localInsight != null && (localInsight.FailedContextSwitchCount > 0 || localInsight.NullTargetCount > 0 || localInsight.ModifierMentions > 0))
                score += 2;
            if (peers != null)
            {
                foreach (ParityRoomPeer peer in peers)
                {
                    if (!String.IsNullOrWhiteSpace(peer.ManifestText))
                        score++;
                    if (!String.IsNullOrWhiteSpace(peer.OosDeepReportText))
                        score++;
                }
            }
            if (findings != null && findings.Count > 0)
                score++;
            return score >= 6 ? "High" : (score >= 3 ? "Medium" : "Low");
        }

        private List<string> BuildParityRoomBlockedActions(OosDeepInsight localInsight, List<ParityRoomPeer> peers, List<string> findings, bool safeToStart)
        {
            List<string> blocked = new List<string>();
            if (localInsight != null && localInsight.HotjoinForbidden)
                blocked.Add("Do not use controlled hotjoin for this incident.");
            if (localInsight != null && localInsight.SessionContaminationScore >= 80)
                blocked.Add("Do not continue from the currently desynced running session.");
            if (findings != null && findings.Count > 0)
                blocked.Add("Do not unpause until parity mismatches are cleared.");
            if (!safeToStart)
                blocked.Add("Do not treat the lobby as ready until every required host/player action is complete.");

            if (peers != null)
            {
                foreach (ParityRoomPeer peer in peers)
                {
                    string hotjoin = ExtractReportValue(peer.OosDeepReportText, "Controlled hotjoin");
                    if (NullText(hotjoin).IndexOf("FORBIDDEN", StringComparison.OrdinalIgnoreCase) >= 0)
                        blocked.Add("Do not hotjoin " + peer.PlayerLabel + " back into the live session.");
                }
            }

            return blocked;
        }

        private string ExtractParityRoomSection(string text, string header, string nextHeader)
        {
            string value = NullText(text).Replace("\r\n", "\n");
            int start = value.IndexOf(header, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return "";
            start = value.IndexOf('\n', start);
            if (start < 0)
                return "";
            start++;
            int end = nextHeader == null ? value.Length : value.IndexOf(nextHeader, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
                end = value.Length;
            return value.Substring(start, end - start).Trim();
        }

        private string BuildParityResponsibilitiesText(string actionsText)
        {
            List<string> host = new List<string>();
            List<string> everyone = new List<string>();
            List<string> players = new List<string>();
            foreach (string rawLine in NullText(actionsText).Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("-", StringComparison.Ordinal))
                    continue;
                string item = line.Substring(1).Trim();
                if (item.StartsWith("Host ", StringComparison.OrdinalIgnoreCase) || item.StartsWith("Host ", StringComparison.OrdinalIgnoreCase))
                    host.Add(item);
                else if (Regex.IsMatch(item, @"^[^:]+:\s"))
                    players.Add(item);
                else
                    everyone.Add(item);
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Host must do");
            if (host.Count == 0)
                sb.AppendLine("- Follow the room diagnosis and choose the single recovery path for the whole lobby.");
            else
                foreach (string item in host)
                    sb.AppendLine("- " + item);
            sb.AppendLine();
            sb.AppendLine("Everyone must do");
            if (everyone.Count == 0)
                sb.AppendLine("- Keep parity clean and follow the same save/recovery path.");
            else
                foreach (string item in everyone)
                    sb.AppendLine("- " + item);
            sb.AppendLine();
            sb.AppendLine("Specific players");
            if (players.Count == 0)
                sb.AppendLine("- No player-specific actions yet.");
            else
                foreach (string item in players)
                    sb.AppendLine("- " + item);
            return sb.ToString();
        }

        private string BuildParityAllowedText(string actionsText)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Allowed actions");
            string verdictLine = ExtractReportValue(actionsText, "Verdict");
            if (NullText(verdictLine).IndexOf("SAFE", StringComparison.OrdinalIgnoreCase) >= 0)
                sb.AppendLine("- Start from the selected save and keep the first month slow.");
            else
                sb.AppendLine("- Only collect evidence, compare parity, and follow the selected recovery path.");
            if (actionsText.IndexOf("Controlled hotjoin is unsafe", StringComparison.OrdinalIgnoreCase) < 0
                && actionsText.IndexOf("forbids controlled hotjoin", StringComparison.OrdinalIgnoreCase) < 0)
                sb.AppendLine("- Controlled hotjoin remains available only if parity is clean and no deep OOS blocker appears.");
            if (actionsText.IndexOf("rollback", StringComparison.OrdinalIgnoreCase) >= 0)
                sb.AppendLine("- Roll back to an older clean manual save if the same OOS repeats.");
            if (actionsText.IndexOf("rehost", StringComparison.OrdinalIgnoreCase) >= 0)
                sb.AppendLine("- Full rehost is allowed once all host and player blockers are fixed.");
            return sb.ToString();
        }

        private string WriteParityRoomIncidentPack(ParityRoomSession session)
        {
            EnsureStabilizerRoot();
            WriteMultiplayerParityManifest();
            AnalyzeLatestOosReport();
            string exportDir = Path.Combine(stabilizerRoot, "parity_room_incident_pack_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(exportDir);

            CopyIfExists(StabilizerFile("ck3_stabilizer_mp_parity_manifest.txt"), Path.Combine(exportDir, "host_mp_parity_manifest.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_latest_oos_summary.txt"), Path.Combine(exportDir, "host_latest_oos_summary.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_latest_oos_deep_report.txt"), Path.Combine(exportDir, "host_latest_oos_deep_report.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_session_contamination_score.txt"), Path.Combine(exportDir, "host_session_contamination_score.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_recovery_runbook.txt"), Path.Combine(exportDir, "host_recovery_runbook.txt"));
            CopyIfExists(StabilizerFile("ck3_stabilizer_incident_state.txt"), Path.Combine(exportDir, "host_incident_state.txt"));

            lock (session.Sync)
            {
                foreach (ParityRoomPeer peer in session.Peers)
                {
                    string playerDir = Path.Combine(exportDir, SafeFileName(peer.PlayerLabel));
                    Directory.CreateDirectory(playerDir);
                    WriteTextFile(Path.Combine(playerDir, "peer_manifest.txt"), peer.ManifestText);
                    WriteTextFile(Path.Combine(playerDir, "peer_oos_summary.txt"), peer.OosSummaryText);
                    WriteTextFile(Path.Combine(playerDir, "peer_oos_metadata.txt"), peer.OosMetadataText);
                    WriteTextFile(Path.Combine(playerDir, "peer_oos_deep_report.txt"), peer.OosDeepReportText);
                    WriteTextFile(Path.Combine(playerDir, "peer_recovery_runbook.txt"), peer.OosRecoveryRunbookText);
                    WriteTextFile(Path.Combine(playerDir, "peer_contamination_score.txt"), peer.OosContaminationText);
                    WriteTextFile(Path.Combine(playerDir, "peer_savegame_oos_dump.oos"), peer.OosSaveDumpText);
                    WriteTextFile(Path.Combine(playerDir, "peer_modifiers_oos_dump.oos"), peer.OosModifierDumpText);
                    WriteTextFile(Path.Combine(playerDir, "peer_error_log.log"), peer.OosErrorLogText);
                }
            }

            StringBuilder index = new StringBuilder();
            index.AppendLine("Parity room incident pack");
            index.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            index.AppendLine("Room: " + session.RoomCode);
            index.AppendLine("Host: " + session.LocalPlayerLabel);
            index.AppendLine();
            index.AppendLine("Contents");
            index.AppendLine("- host parity, deep OOS report, contamination score, recovery runbook");
            index.AppendLine("- peer parity, metadata, deep reports and raw OOS dumps when shared");
            lock (session.Sync)
            {
                index.AppendLine("- peers collected: " + session.Peers.Count);
                foreach (ParityRoomPeer peer in session.Peers)
                    index.AppendLine("- " + peer.PlayerLabel + " | endpoint=" + peer.Endpoint);
            }
            SafeAtomicFile.WriteAllText(Path.Combine(exportDir, "incident_pack_index.txt"), index.ToString(), Encoding.UTF8);
            RecordIncidentHistoryEvent("parity_room_incident_pack", AnalyzeOosIncidentState(), "Parity room incident pack exported");
            return exportDir;
        }

        private void WriteTextFile(string path, string text)
        {
            SafeAtomicFile.WriteAllText(path, NullText(text), Encoding.UTF8);
        }

        private string ExtractReportValue(string text, string label)
        {
            Match match = Regex.Match(NullText(text), @"(?im)^\s*-\s*" + Regex.Escape(label) + @"\s*:\s*(.+?)\s*$");
            if (!match.Success)
                match = Regex.Match(NullText(text), @"(?im)^\s*" + Regex.Escape(label) + @"\s*:\s*(.+?)\s*$");
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        private string ExtractFirstRunbookStep(string text)
        {
            Match match = Regex.Match(NullText(text), @"(?im)^\s*1\.\s+(.+?)\s*$");
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        private int GetParityRowSortRank(string status)
        {
            string normalized = NormalizeParityStatus(status);
            if (String.Equals(normalized, "Fix", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (String.Equals(normalized, "Wait", StringComparison.OrdinalIgnoreCase))
                return 1;
            return 2;
        }

        private string NormalizeParityStatus(string status)
        {
            string text = NullText(status).Trim();
            if (text.Length == 0)
                return "Wait";
            if (text.Equals("OK", StringComparison.OrdinalIgnoreCase))
                return "OK";
            if (text.Equals("Fix", StringComparison.OrdinalIgnoreCase))
                return "Fix";
            return "Wait";
        }

        private string FormatParityPlayerLabel(string player)
        {
            string text = NullText(player).Trim();
            if (text.Length <= 12)
                return text;
            return text.Substring(0, 12);
        }

        private string ShortParityArea(string area)
        {
            string key = NullText(area).Trim();
            switch (key)
            {
                case "Installed version": return "Game version";
                case "Steam build": return "Steam build";
                case "Steam branch": return "Steam branch";
                case "Launch options": return "Launch args";
                case "Local parity fingerprint": return "Profile hash";
                case "DLC loadout clean": return "DLC state";
                case "DLC footprint fingerprint": return "DLC hash";
                case "Steam Workshop fingerprint": return "Workshop hash";
                case "No active mods": return "Mods off";
                case "No disabled DLCs": return "DLC enabled";
                case "ck3.exe": return "Game exe";
                case "launcher-settings.json": return "Launcher cfg";
                case "dlc_load.json": return "DLC file";
                case "pdx_settings.txt": return "Pdx settings";
                case "Best clean manual save candidate": return "Host save";
                case "Best clean manual save hash": return "Save hash";
                case "Best clean manual save version": return "Save version";
                case "Best clean manual save readable": return "Save readable";
                case "Active continue title": return "Continue title";
                case "Installed/save version parity": return "Version parity";
                case "-noasync": return "Noasync";
                case "risky launch options absent": return "Safe args";
                case "All parity checks": return "All checks";
                default: return LimitParityText(key, 16);
            }
        }

        private string ShortParityCellValue(string area, string value)
        {
            string text = SafeParityValue(value);
            if (text.Equals("True", StringComparison.OrdinalIgnoreCase))
                return "Yes";
            if (text.Equals("False", StringComparison.OrdinalIgnoreCase))
                return "No";
            if (text.Equals("match", StringComparison.OrdinalIgnoreCase))
                return "Match";
            if (String.IsNullOrWhiteSpace(text))
                return "-";

            string key = NullText(area).Trim();
            if (key == "Launch options")
            {
                if (text.Equals("(empty)", StringComparison.OrdinalIgnoreCase))
                    return "None";
                return LimitParityText(text.Replace("-noasync", "noasync"), 18);
            }

            if (key == "Best clean manual save candidate" || key == "Active continue title")
                return LimitParityText(Path.GetFileNameWithoutExtension(text), 18);

            if (key.IndexOf("fingerprint", StringComparison.OrdinalIgnoreCase) >= 0
                || key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || key.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                || key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || key.IndexOf("hash", StringComparison.OrdinalIgnoreCase) >= 0)
                return LimitParityText(text, 14);

            return LimitParityText(text, 18);
        }

        private string LimitParityText(string text, int maxLength)
        {
            string value = NullText(text).Trim();
            if (value.Length <= maxLength)
                return value;
            return value.Substring(0, maxLength);
        }

        private void CompareParityKeys(ParityManifestRecord local, ParityManifestRecord other, string[] keys, List<string> findings, List<ParityDifferenceRow> rows)
        {
            foreach (string key in keys)
            {
                string left = ParityValue(local, key);
                string right = ParityValue(other, key);
                if (String.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                    continue;

                findings.Add("FIX: " + other.PlayerLabel + " mismatch in " + key + " | host=" + SafeParityValue(left) + " | player=" + SafeParityValue(right));
                rows.Add(new ParityDifferenceRow
                {
                    Player = other.PlayerLabel,
                    Area = key,
                    HostValue = SafeParityValue(left),
                    PlayerValue = SafeParityValue(right),
                    Status = "Fix"
                });
            }
        }

        private void CompareParityBooleans(ParityManifestRecord local, ParityManifestRecord other, string key, List<string> findings, List<ParityDifferenceRow> rows)
        {
            string left = ParityValue(local, key);
            string right = ParityValue(other, key);
            if (String.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                return;

            findings.Add("FIX: " + other.PlayerLabel + " differs in " + key + " | host=" + SafeParityValue(left) + " | player=" + SafeParityValue(right));
            rows.Add(new ParityDifferenceRow
            {
                Player = other.PlayerLabel,
                Area = key,
                HostValue = SafeParityValue(left),
                PlayerValue = SafeParityValue(right),
                Status = "Fix"
            });
        }

        private void BuildParityRecommendations(List<string> findings, List<ParityDifferenceRow> rows, List<string> oosNotes, List<string> actions)
        {
            bool versionMismatch = ContainsParityFinding(findings, "Installed version") || ContainsParityFinding(findings, "Steam build");
            bool branchMismatch = ContainsParityFinding(findings, "Steam branch");
            bool launchMismatch = ContainsParityFinding(findings, "Launch options")
                || ContainsParityFinding(findings, "-noasync")
                || ContainsParityFinding(findings, "risky launch options absent");
            bool modMismatch = ContainsParityFinding(findings, "No active mods")
                || ContainsParityFinding(findings, "No disabled DLCs")
                || ContainsParityFinding(findings, "DLC loadout clean")
                || ContainsParityFinding(findings, "DLC footprint fingerprint")
                || ContainsParityFinding(findings, "Steam Workshop fingerprint")
                || ContainsParityFinding(findings, "dlc_load.json");
            bool binaryMismatch = ContainsParityFinding(findings, "ck3.exe")
                || ContainsParityFinding(findings, "launcher-settings.json")
                || ContainsParityFinding(findings, "pdx_settings.txt")
                || ContainsParityFinding(findings, "Local parity fingerprint");
            bool saveMismatch = ContainsParityFinding(findings, "Best clean manual save")
                || ContainsParityFinding(findings, "Active continue title");

            if (versionMismatch)
                AddParityAction(actions, "Version/build mismatch: every player should fully update CK3 in Steam, restart Steam, then export parity again until Game version and Steam build match the host.");
            if (branchMismatch)
                AddParityAction(actions, "Steam branch mismatch: open CK3 Properties -> Betas and move every player to the same branch, preferably public/default.");
            if (launchMismatch)
                AddParityAction(actions, "Launch mismatch: copy the host launch options, keep noasync, and remove risky args like debug_mode, dx11, opengl or custom script flags.");
            if (modMismatch)
                AddParityAction(actions, "Mods/DLC mismatch: every player should disable all mods, match DLC ownership/loadout with the host, then re-open the launcher and export parity again.");
            if (binaryMismatch)
                AddParityAction(actions, "Game files/settings mismatch: run Steam file verification, then rebuild the safe profile so ck3.exe, launcher cfg and pdx settings match the host baseline.");
            if (saveMismatch)
                AddParityAction(actions, "Save mismatch: everyone must use the exact host-selected manual save. Do not use Continue and do not load a different local copy.");

            if (rows != null && rows.Count > 0)
            {
                foreach (ParityDifferenceRow row in rows)
                {
                    if (!String.Equals(row.Status, "Fix", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string check = NullText(row.Area);
                    if (check == "Best clean manual save readable")
                        AddParityAction(actions, row.Player + ": save is not readable. Re-copy the host save through Rehost pack or Browse the correct local save file.");
                    else if (check == "No active mods")
                        AddParityAction(actions, row.Player + ": mods are still active. Open launcher playset and disable all gameplay mods before joining.");
                    else if (check == "No disabled DLCs")
                        AddParityAction(actions, row.Player + ": DLC state differs. Re-enable CK3 DLC to match host or temporarily use the same DLC subset.");
                    else if (check == "-noasync")
                        AddParityAction(actions, row.Player + ": launch options are missing noasync. Add it before starting.");
                    else if (check == "risky launch options absent")
                        AddParityAction(actions, row.Player + ": risky launch flags are still present. Remove custom graphics/debug flags and export parity again.");
                    else if (check == "Steam Workshop fingerprint")
                        AddParityAction(actions, row.Player + ": workshop files differ. Unsubscribe from CK3 workshop items or clear workshop leftovers to match vanilla host.");
                    else if (check == "Local parity fingerprint")
                        AddParityAction(actions, row.Player + ": local profile differs from host. Run Fix host on that PC, then resend parity.");
                    else if (check == "ck3.exe")
                        AddParityAction(actions, row.Player + ": CK3 executable differs. Verify game files in Steam before retry.");
                }
            }

            if (oosNotes != null && oosNotes.Count > 0)
            {
                AddParityAction(actions, "OOS evidence is present: use Send OOS data from every affected player, compare metadata first, and only then decide between clean rehost and older rollback save.");
            }

            if (findings.Count > 0 && actions.Count == 0)
                AddParityAction(actions, "A parity mismatch exists. Re-generate manifests on every machine and compare them again before starting.");
        }

        private void AddParityAction(List<string> actions, string text)
        {
            string value = NullText(text).Trim();
            if (value.Length == 0)
                return;

            foreach (string existing in actions)
            {
                if (String.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            actions.Add(value);
        }

        private bool ContainsParityFinding(List<string> findings, string needle)
        {
            foreach (string finding in findings)
            {
                if (finding.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private string ParityValue(ParityManifestRecord record, string key)
        {
            if (record == null || String.IsNullOrWhiteSpace(key))
                return "";

            string value;
            return record.Values.TryGetValue(key, out value) ? value : "";
        }

        private string SafeParityValue(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? "(missing)" : value;
        }

        private void WorkflowStepsListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= workflowStepStates.Count)
                return;

            WorkflowStepState state = workflowStepStates[e.Index];
            Color color = Color.FromArgb(35, 35, 35);
            if (state.Manual)
                color = Color.FromArgb(180, 120, 0);
            else if (state.Blocked)
                color = Color.FromArgb(170, 35, 35);
            else if (state.Passed)
                color = Color.FromArgb(0, 120, 35);

            Rectangle badgeRect = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top + 3, 64, e.Bounds.Height - 6);
            using (SolidBrush badgeBrush = new SolidBrush(color))
                e.Graphics.FillRectangle(badgeBrush, badgeRect);

            TextRenderer.DrawText(
                e.Graphics,
                GetWorkflowStatusTag(state),
                Font,
                badgeRect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            Rectangle separatorRect = new Rectangle(badgeRect.Right + 8, e.Bounds.Top + 2, 18, e.Bounds.Height - 4);
            TextRenderer.DrawText(
                e.Graphics,
                "-",
                Font,
                separatorRect,
                Color.FromArgb(90, 90, 90),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            Rectangle textRect = new Rectangle(separatorRect.Right + 2, e.Bounds.Top + 2, Math.Max(0, e.Bounds.Width - badgeRect.Width - separatorRect.Width - 20), e.Bounds.Height - 4);
            TextRenderer.DrawText(
                e.Graphics,
                MakeWorkflowStepLabelReadable(state),
                Font,
                textRect,
                e.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            e.DrawFocusRectangle();
        }

        private string GetWorkflowStatusTag(WorkflowStepState state)
        {
            if (state.Manual)
                return "MANUAL";
            if (state.Blocked)
                return "FIX";
            if (state.Passed)
                return "OK";
            return "WAIT";
        }

        private string MakeWorkflowStepLabelReadable(WorkflowStepState state)
        {
            string text = NullText(state.Label);
            text = text.Replace("Automatic: ", "");
            text = text.Replace("Manual: ", "");
            text = text.Replace("this PC and network", "host PC and internet");
            text = text.Replace("selected host save", "selected save");
            text = text.Replace("selected rollback/rehost save file", "selected save file");
            text = text.Replace("will use Load Game, not Resume/Continue.", "use Load Game, not Resume/Continue.");
            text = text.Replace("will stay on speed 1-2 with no extra clicks.", "stay on speed 1-2 with no extra clicks.");
            return text;
        }

        private string BuildWorkflowStateLine(WorkflowStepState state)
        {
            string tag = GetWorkflowStatusTag(state);
            string detail = NullText(state.Detail);
            if (detail.Length > 140)
                detail = detail.Substring(0, 140).TrimEnd() + "...";
            return tag + ": " + MakeWorkflowStepLabelReadable(state) + " " + (String.IsNullOrWhiteSpace(detail) ? "" : "- " + detail);
        }

        private string MakeWorkflowSaveVerdictReadable(HostSaveCandidateResult save)
        {
            string fileName = MakeWorkflowFileLabelReadable(save.Save.Path, "no save selected");
            if (String.IsNullOrWhiteSpace(fileName))
                fileName = "no save selected";
            return fileName + " | " + CompactWorkflowText(save.Verdict, 18) + " (" + save.Score + "/100)";
        }

        private string MakeWorkflowFileLabelReadable(string path, string fallback)
        {
            string fileName = NullText(Path.GetFileName(path));
            if (String.IsNullOrWhiteSpace(fileName))
                return fallback;
            return CompactWorkflowText(fileName, 52);
        }

        private string CompactWorkflowText(string text, int maxLength)
        {
            string value = NullText(text).Trim();
            if (maxLength < 8)
                maxLength = 8;
            if (value.Length <= maxLength)
                return value;

            string compact = value.Replace("_", " ").Replace(".ck3", "").Replace(".txt", "");
            compact = Regex.Replace(compact, "\\s+", " ").Trim();
            if (compact.Length <= maxLength)
                return compact;

            return compact.Substring(0, maxLength).TrimEnd();
        }

        private void ApplyWorkflowVerdictStyle(string text)
        {
            Color accent = Color.FromArgb(158, 168, 180);
            Color surface = Color.FromArgb(251, 251, 251);
            workflowStatusPanel.BackColor = surface;
            workflowStatusAccentPanel.BackColor = accent;
            workflowVerdictLabel.ForeColor = Color.FromArgb(45, 45, 45);
            workflowHintLabel.ForeColor = Color.FromArgb(96, 96, 96);

            if (String.IsNullOrEmpty(text))
                return;

            if (text.IndexOf("Fix", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Change host", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                accent = Color.FromArgb(181, 71, 71);
                workflowVerdictLabel.ForeColor = Color.FromArgb(145, 45, 45);
            }
            else if (text.IndexOf("checking", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("waiting", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                accent = Color.FromArgb(194, 152, 46);
                workflowVerdictLabel.ForeColor = Color.FromArgb(135, 105, 20);
            }
            else if (text.IndexOf("Ready", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("selected", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                accent = Color.FromArgb(54, 146, 89);
                workflowVerdictLabel.ForeColor = Color.FromArgb(35, 120, 60);
            }

            workflowStatusAccentPanel.BackColor = accent;
        }

        private void ApplyWorkflowSummaryStyling()
        {
            workflowSummaryBox.SuspendLayout();
            try
            {
                string text = workflowSummaryBox.Text ?? "";
                workflowSummaryBox.SelectAll();
                workflowSummaryBox.SelectionColor = Color.FromArgb(35, 35, 35);
                workflowSummaryBox.SelectionFont = new Font(workflowSummaryBox.Font, FontStyle.Regular);

                int lineStart = 0;
                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                foreach (string line in lines)
                {
                    int length = line.Length;
                    Color color = Color.FromArgb(35, 35, 35);
                    FontStyle style = FontStyle.Regular;

                    if (line == "Quick summary" || line == "Automatic checks" || line == "Manual steps" || line == "Result" || line == "Recommended actions"
                        || line == "Do now" || line == "Before next launch" || line == "Before start" || line == "In lobby" || line == "In new lobby" || line == "In game" || line == "Scenario logic")
                    {
                        color = Color.FromArgb(25, 70, 140);
                        style = FontStyle.Bold;
                    }
                    else if (line.StartsWith("Recommendation:", StringComparison.OrdinalIgnoreCase))
                    {
                        color = Color.FromArgb(120, 80, 0);
                        style = FontStyle.Bold;
                    }
                    else if (line.StartsWith("- FIX:", StringComparison.OrdinalIgnoreCase))
                    {
                        color = Color.FromArgb(160, 25, 25);
                    }
                    else if (line.StartsWith("- OK:", StringComparison.OrdinalIgnoreCase))
                    {
                        color = Color.FromArgb(20, 120, 45);
                    }
                    else if (line.StartsWith("- WAIT:", StringComparison.OrdinalIgnoreCase))
                    {
                        color = Color.FromArgb(120, 110, 0);
                    }
                    else if (line.StartsWith("- ", StringComparison.OrdinalIgnoreCase))
                    {
                        color = Color.FromArgb(55, 55, 55);
                    }

                    workflowSummaryBox.Select(lineStart, length);
                    workflowSummaryBox.SelectionColor = color;
                    workflowSummaryBox.SelectionFont = new Font(workflowSummaryBox.Font, style);
                    lineStart += length + 1;
                }

                workflowSummaryBox.Select(0, 0);
            }
            finally
            {
                workflowSummaryBox.ResumeLayout();
            }
        }
    }
}
