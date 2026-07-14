using System;
using System.Drawing;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private void BuildChecklistGroups()
        {
            updatingChecklistUi = true;
            try
            {
                checklistPanel.SuspendLayout();
                checklistContentPanel.SuspendLayout();
                checklistContentPanel.Controls.Clear();
                stepGroups.Clear();
                stepRows.Clear();
                for (int i = 0; i < ExpectedStepCount; i++)
                    stepRows.Add(null);

                AddStepGroup("Safety", new[] { 0, 1, 2 });
                AddStepGroup("Windows & Network", new[] { 3, 4, 5, 6, 7, 8, 9 });
                AddStepGroup("Steam & Launcher", new[] { 10, 11, 12, 13 });
                AddStepGroup("CK3 Profile", new[] { 14, 15, 16, 17 });
                AddStepGroup("User State, Cache & Files", new[] { 18, 19, 20, 21, 22, 23, 24 });
                AddStepGroup("OOS Reports & MP Readiness", new[] { 25, 26, 27, 28 });
            }
            finally
            {
                updatingChecklistUi = false;
                ResizeChecklistRows();
                RelayoutChecklistGroups();
                RefreshAllGroupStates();
                checklistContentPanel.ResumeLayout();
                checklistPanel.ResumeLayout();
            }
        }

        private void AddStepGroup(string title, int[] indices)
        {
            StepGroupUi group = new StepGroupUi(title, indices);
            stepGroups.Add(group);

            int groupWidth = ChecklistContentWidth();
            group.Header.Size = new Size(groupWidth, 30);
            group.Header.MinimumSize = new Size(groupWidth, 30);
            group.Header.MaximumSize = new Size(groupWidth, 30);
            group.Header.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            group.Header.BackColor = Color.FromArgb(242, 244, 247);

            group.ToggleButton.Text = "-";
            group.ToggleButton.Width = 28;
            group.ToggleButton.Height = 24;
            group.ToggleButton.Location = new Point(4, 3);
            group.ToggleButton.Click += delegate { ToggleStepGroup(group); };
            group.Header.Controls.Add(group.ToggleButton);

            group.CheckBox.Width = 22;
            group.CheckBox.Height = 24;
            group.CheckBox.Location = new Point(36, 3);
            group.CheckBox.ThreeState = true;
            group.CheckBox.CheckedChanged += delegate
            {
                if (updatingChecklistUi)
                    return;
                SetGroupChecked(group, group.CheckBox.Checked);
            };
            group.Header.Controls.Add(group.CheckBox);

            group.TitleLabel.Text = title;
            group.TitleLabel.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            group.TitleLabel.Location = new Point(62, 6);
            group.TitleLabel.Size = new Size(Math.Max(100, groupWidth - group.TitleLabel.Left - 8), 18);
            group.TitleLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            group.Header.Controls.Add(group.TitleLabel);

            HookChecklistWheel(group.Header);
            HookChecklistWheel(group.ToggleButton);
            HookChecklistWheel(group.CheckBox);
            HookChecklistWheel(group.TitleLabel);

            checklistContentPanel.Controls.Add(group.Header);

            foreach (int index in indices)
            {
                StepRowUi row = CreateStepRow(index);
                group.Rows.Add(row);
                stepRows[index] = row;
                checklistContentPanel.Controls.Add(row.RowPanel);
            }
        }

        private void ResizeChecklistRows()
        {
            int width = ChecklistContentWidth();
            foreach (StepGroupUi group in stepGroups)
            {
                group.Header.Size = new Size(width, 30);
                group.Header.MinimumSize = new Size(width, 30);
                group.Header.MaximumSize = new Size(width, 30);
                group.TitleLabel.Width = Math.Max(100, width - group.TitleLabel.Left - 8);
                foreach (StepRowUi row in group.Rows)
                {
                    row.RowPanel.Size = new Size(width, 28);
                    row.RowPanel.MinimumSize = new Size(width, 28);
                    row.RowPanel.MaximumSize = new Size(width, 28);
                    row.TitleLabel.Width = Math.Max(100, width - row.TitleLabel.Left - 8);
                }
            }
            LayoutChecklistViewport();
            RelayoutChecklistGroups();
        }

        private void RelayoutChecklistGroups()
        {
            int y = 4;
            foreach (StepGroupUi group in stepGroups)
            {
                group.Header.Location = new Point(0, y);
                y += group.Header.Height;
                foreach (StepRowUi row in group.Rows)
                {
                    row.RowPanel.Visible = group.Expanded;
                    row.RowPanel.Location = new Point(0, y);
                    if (group.Expanded)
                        y += row.RowPanel.Height;
                }
                y += 4;
            }

            int contentHeight = Math.Max(0, y + 4);
            checklistContentPanel.Size = new Size(ChecklistContentWidth(), Math.Max(checklistPanel.ClientSize.Height, contentHeight));
            SyncChecklistScrollBar(contentHeight);
            UpdateChecklistScrollPosition();
        }

        private StepRowUi CreateStepRow(int index)
        {
            StepRowUi row = new StepRowUi();
            row.Index = index;
            row.Title = StepTitle(index);
            row.HelpText = StepHelpText(index);

            int rowWidth = ChecklistContentWidth();
            row.RowPanel.Size = new Size(rowWidth, 28);
            row.RowPanel.MinimumSize = new Size(rowWidth, 28);
            row.RowPanel.MaximumSize = new Size(rowWidth, 28);
            row.RowPanel.Anchor = AnchorStyles.Left | AnchorStyles.Top;

            row.CheckBox.Width = 22;
            row.CheckBox.Height = 22;
            row.CheckBox.Location = new Point(38, 3);
            row.CheckBox.CheckedChanged += delegate
            {
                if (updatingChecklistUi)
                    return;
                if (row.Index >= 0 && row.Index < steps.Items.Count)
                    steps.SetItemChecked(row.Index, row.CheckBox.Checked);
                RefreshAllGroupStates();
            };
            row.RowPanel.Controls.Add(row.CheckBox);

            row.HelpButton.Text = "?";
            row.HelpButton.Width = 24;
            row.HelpButton.Height = 22;
            row.HelpButton.Location = new Point(66, 3);
            row.HelpButton.TabStop = false;
            stepToolTip.SetToolTip(row.HelpButton, row.HelpText);
            row.HelpButton.Click += delegate
            {
                stepToolTip.Show(row.HelpText, row.HelpButton, 0, row.HelpButton.Height + 2, 8000);
            };
            row.RowPanel.Controls.Add(row.HelpButton);

            row.TitleLabel.Text = row.Title;
            row.TitleLabel.Location = new Point(98, 6);
            row.TitleLabel.Size = new Size(Math.Max(100, rowWidth - row.TitleLabel.Left - 8), 18);
            row.TitleLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            stepToolTip.SetToolTip(row.TitleLabel, row.HelpText);
            row.RowPanel.Controls.Add(row.TitleLabel);

            HookChecklistWheel(row.RowPanel);
            HookChecklistWheel(row.CheckBox);
            HookChecklistWheel(row.HelpButton);
            HookChecklistWheel(row.TitleLabel);

            return row;
        }

        private void ToggleStepGroup(StepGroupUi group)
        {
            group.Expanded = !group.Expanded;
            group.ToggleButton.Text = group.Expanded ? "-" : "+";
            foreach (StepRowUi row in group.Rows)
                row.RowPanel.Visible = group.Expanded;
            RelayoutChecklistGroups();
        }

        private void SetGroupChecked(StepGroupUi group, bool value)
        {
            updatingChecklistUi = true;
            try
            {
                foreach (int index in group.StepIndices)
                    SetStepChecked(index, value);
            }
            finally
            {
                updatingChecklistUi = false;
                RefreshAllGroupStates();
            }
        }

        private void RefreshAllGroupStates()
        {
            updatingChecklistUi = true;
            try
            {
                foreach (StepGroupUi group in stepGroups)
                    RefreshGroupState(group);
            }
            finally
            {
                updatingChecklistUi = false;
            }
        }

        private void RefreshGroupState(StepGroupUi group)
        {
            int selected = 0;
            foreach (int index in group.StepIndices)
                if (IsStepChecked(index))
                    selected++;

            group.CheckBox.CheckState = selected == 0
                ? CheckState.Unchecked
                : (selected == group.StepIndices.Length ? CheckState.Checked : CheckState.Indeterminate);
        }

        private int ChecklistContentWidth()
        {
            int width = checklistPanel.ClientSize.Width - checklistScrollBar.Width - 6;
            if (width < 300)
                width = checklistPanel.ClientSize.Width - 8;
            return Math.Max(300, width);
        }

        private void LayoutChecklistViewport()
        {
            int scrollbarWidth = checklistScrollBar.Width;
            checklistScrollBar.Location = new Point(Math.Max(0, checklistPanel.ClientSize.Width - scrollbarWidth - 1), 1);
            checklistScrollBar.Height = Math.Max(0, checklistPanel.ClientSize.Height - 2);
        }

        private void SyncChecklistScrollBar(int contentHeight)
        {
            int viewportHeight = Math.Max(1, checklistPanel.ClientSize.Height - 2);
            int maxValue = Math.Max(0, contentHeight - viewportHeight);

            checklistScrollBar.Minimum = 0;
            checklistScrollBar.SmallChange = 24;
            checklistScrollBar.LargeChange = viewportHeight;
            checklistScrollBar.Maximum = maxValue + checklistScrollBar.LargeChange - 1;
            checklistScrollBar.Enabled = maxValue > 0;

            if (!checklistScrollBar.Enabled)
            {
                checklistScrollBar.Value = 0;
                return;
            }

            if (checklistScrollBar.Value > maxValue)
                checklistScrollBar.Value = maxValue;
        }

        private void UpdateChecklistScrollPosition()
        {
            checklistContentPanel.Location = new Point(0, -checklistScrollBar.Value);
        }

        private void ScrollChecklistWheel(int delta)
        {
            if (!checklistScrollBar.Enabled || delta == 0)
                return;

            int step = 84;
            int next = checklistScrollBar.Value - Math.Sign(delta) * step;
            int maxValue = Math.Max(checklistScrollBar.Minimum, checklistScrollBar.Maximum - checklistScrollBar.LargeChange + 1);
            if (next < checklistScrollBar.Minimum)
                next = checklistScrollBar.Minimum;
            if (next > maxValue)
                next = maxValue;

            checklistScrollBar.Value = next;
            UpdateChecklistScrollPosition();
        }

        private void HookChecklistWheel(Control control)
        {
            control.MouseWheel += delegate(object sender, MouseEventArgs e) { ScrollChecklistWheel(e.Delta); };
        }

        private string StepTitle(int index)
        {
            return index >= 0 && index < steps.Items.Count ? Convert.ToString(steps.Items[index]) : "Step " + (index + 1);
        }

        private string StepHelpText(int index)
        {
            switch (index)
            {
                case 0:
                    return "Creates a Windows System Restore point before CK3MPS changes anything. If System Restore is disabled, CK3MPS asks before trying to enable VSS/System Protection.";
                case 1:
                    return "Checks that the selected CK3 game and Documents folders are valid and that CK3/Paradox Launcher are closed. Does not change files.";
                case 2:
                    return "Creates the CK3MPS quarantine folder under Documents\\Paradox Interactive\\CK3MPS. Later moved/backed-up files are stored there instead of being deleted.";
                case 3:
                    return "Runs ipconfig /flushdns. This clears Windows DNS resolver cache only; it does not change network settings.";
                case 4:
                    return "Reads adapters, routes, DNS, MTU, TCP/IP state, ping baseline, and service reachability. It logs diagnostics and does not change settings.";
                case 5:
                    return "Adds Windows Firewall allow rules for ck3.exe when running elevated. This changes Windows Firewall rules only for CK3.";
                case 6:
                    return "Applies Windows game/network registry stability settings such as Game DVR/GPU/fullscreen profile and multimedia network profile when elevated.";
                case 7:
                    return "Applies conservative adapter/power stability settings where supported. It avoids route, VPN, MTU, and provider-specific rewrites.";
                case 8:
                    return "Checks running overlays, VPNs, background apps, Windows services, and power plan. It logs warnings and does not close apps.";
                case 9:
                    return "Tests TCP reachability to Paradox and Steam services. It only opens network checks and does not change settings.";
                case 10:
                    return "Copies Steam localconfig/sharedconfig/appmanifest, launcher database, dlc_load.json, and pdx_settings.txt into quarantine backups.";
                case 11:
                    return "Backs up and edits Steam config for CK3: keeps -noasync, removes risky debug/renderer launch options, and disables visible Steam Cloud flag for CK3.";
                case 12:
                    return "Moves Paradox Launcher CK3 database/cache files to quarantine so the launcher rebuilds clean state on next launch.";
                case 13:
                    return "Checks CK3/Launcher runtime hygiene: processes closed, overlay guidance, and one clean launcher path. Does not change files.";
                case 14:
                    return "Backs up and rewrites dlc_load.json to enabled_mods=[] and disabled_dlcs=[] for a clean no-mod/no-disabled-DLC multiplayer profile.";
                case 15:
                    return "Backs up and rewrites pdx_settings.txt with CK3 multiplayer stability settings: autosave/cloud/save-on-exit off, Vulkan/fullscreen/VSync, FPS cap, language, and selected graphics profile.";
                case 16:
                    return "Writes runtime verification report files and tracks whether the latest CK3 debug.log matches the applied profile.";
                case 17:
                    return "Writes a text profile with recommended in-game campaign/session rules. It does not edit CK3 save files.";
                case 18:
                    return "Moves CK3 player UI state folder to quarantine. CK3 can recreate it; this resets local UI/outliner state.";
                case 19:
                    return "Moves OOS, crashes, dumps, and exceptions folders to quarantine reports. It archives diagnostics instead of deleting them.";
                case 20:
                    return "Moves CK3 shader/launcher caches and Paradox Launcher browser caches to quarantine. CK3/Launcher will regenerate them.";
                case 21:
                    return "Moves local .mod descriptor files from the CK3 Documents mod folder to quarantine. It does not delete workshop content.";
                case 22:
                    return "Inspects CK3 binaries for known non-vanilla loader files and writes a report. It does not move or delete binaries.";
                case 23:
                    return "Checks active Continue save, suspicious save names, Steam Cloud remote saves, and version parity. In stabilize mode it can quarantine suspicious save pointers/files.";
                case 24:
                    return "Broad CK3 Documents cleanup: moves caches, logs, old launcher metadata, old stabilizer artifacts, and generated clutter to quarantine while keeping saves.";
                case 25:
                    return "Reads latest OOS metadata and writes a summary report. It does not change game files.";
                case 26:
                    return "Writes an evidence package index for support comparison after OOS. It collects paths, hashes, and report references.";
                case 27:
                    return "Writes OOS prevention rules/protocol notes for players. It creates report text only.";
                case 28:
                    return "Writes local multiplayer parity manifest and OOS risk score for host/client comparison. It creates report text only.";
            }
            return "No detailed help is available for this step.";
        }
    }
}
