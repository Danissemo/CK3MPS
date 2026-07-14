using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

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
            List<PreviewLine> lines = new List<PreviewLine>();
            int selectedOptional = 0;
            for (int i = 0; i < steps.Items.Count; i++)
                if (IsStepChecked(i))
                    selectedOptional++;

            lines.Add(new PreviewLine("header", "Overview"));
            lines.Add(new PreviewLine("muted", "Preset: " + NullText(Convert.ToString(presetBox.SelectedItem)) + " | Graphics: " + CurrentGraphicsProfile() + " | Portable mode: " + YesNo(portableMode)));
            lines.Add(new PreviewLine("muted", "Optional steps selected: " + selectedOptional + " | Total actions that will run: " + CountSelectedSteps()));
            lines.Add(new PreviewLine("muted", "Game folder: " + NullText(ck3Install)));
            lines.Add(new PreviewLine("muted", "Settings/saves: " + NullText(ck3Docs)));

            lines.Add(new PreviewLine("blank", ""));
            lines.Add(new PreviewLine("header", "Core actions that will run"));
            if (selectedOptional > 0)
            {
                lines.Add(new PreviewLine("safe", "- Check CK3 folders and confirm CK3/launcher are closed."));
                lines.Add(new PreviewLine("safe", "- Create or reuse the current quarantine run and record rollback data first."));
            }
            else
            {
                lines.Add(new PreviewLine("warn", "- No optional steps are selected right now."));
            }

            bool anySelected = false;
            lines.Add(new PreviewLine("blank", ""));
            lines.Add(new PreviewLine("header", "Selected actions"));
            for (int i = 0; i < steps.Items.Count; i++)
            {
                if (!WillRunInStabilize(i))
                    continue;
                anySelected = true;
                lines.Add(new PreviewLine(PreviewTone(i), "- " + StepTitle(i)));
                lines.Add(new PreviewLine("muted", "  " + PreviewImpactText(i)));
            }

            if (!anySelected)
                lines.Add(new PreviewLine("warn", "- Nothing will run until you tick at least one optional step."));

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
            return IsStepChecked(index) || ((index == 1 || index == 2) && CountSelectedSteps() > 0);
        }

        private string PreviewImpactText(int index)
        {
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
    }
}
