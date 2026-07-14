using System;
using System.Text;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private bool ConfirmStabilizationPreview()
        {
            string preview = BuildStabilizationPreview();
            LogSection("Stabilization preview");
            foreach (string line in preview.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                if (!String.IsNullOrWhiteSpace(line))
                    Log("INFO " + line);

            DialogResult result = MessageBox.Show(
                preview + "\r\n\r\nContinue and apply these selected actions?",
                "CK3MPS preview",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            return result == DialogResult.Yes;
        }

        private void ShowStabilizationPreview(bool writeToLogOnly)
        {
            string preview = BuildStabilizationPreview();
            if (writeToLogOnly)
            {
                LogSection("Dry-run stabilization plan");
                foreach (string line in preview.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                    if (!String.IsNullOrWhiteSpace(line))
                        Log("INFO " + line);
                return;
            }

            MessageBox.Show(preview, "CK3MPS preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private string BuildStabilizationPreview()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Selected preset: " + NullText(Convert.ToString(presetBox.SelectedItem)));
            sb.AppendLine("Selected steps: " + CountSelectedSteps());
            sb.AppendLine("Game folder: " + NullText(ck3Install));
            sb.AppendLine("Settings/saves: " + NullText(ck3Docs));
            sb.AppendLine();
            sb.AppendLine("Changes that will be attempted:");

            bool any = false;
            for (int i = 0; i < steps.Items.Count; i++)
            {
                if (!WillRunInStabilize(i))
                    continue;
                any = true;
                sb.AppendLine("- " + StepTitle(i));
                sb.AppendLine("  " + PreviewImpactText(i));
            }

            if (!any)
                sb.AppendLine("- No optional steps selected.");

            sb.AppendLine();
            sb.AppendLine("Safety:");
            sb.AppendLine("- Files/folders changed by CK3MPS are recorded in quarantine restore manifest first.");
            sb.AppendLine("- Restore tab can restore selected entries to the previous value or reset supported entries to launcher/game/Windows defaults.");
            sb.AppendLine("- Report-only steps create CK3MPS text files under Documents\\Paradox Interactive\\CK3MPS.");
            return sb.ToString();
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
                    return "Creates a Windows restore point if System Restore is available.";
                case 1:
                    return "Validates paths and closed CK3/Launcher processes.";
                case 2:
                    return "Creates/uses quarantine and starts a restore run record.";
                case 3:
                    return "Clears Windows DNS resolver cache.";
                case 4:
                    return "Reads network/adapters and writes diagnostics.";
                case 5:
                    return "May delete old CK3MPS/CK3 Stabilizer firewall rules and add current ck3.exe rules.";
                case 6:
                    return "Writes selected Windows game/GPU/network registry values; previous values are recorded.";
                case 7:
                    return "Writes selected powercfg adapter stability values; prior command output is recorded.";
                case 8:
                case 9:
                case 13:
                case 22:
                case 25:
                case 26:
                case 27:
                case 28:
                    return "Diagnostic/report step. It does not move user saves or mods.";
                case 10:
                    return "Copies Steam, launcher, dlc_load.json and pdx_settings.txt sources to quarantine backups.";
                case 11:
                    return "Edits Steam CK3 launch/cloud settings after backup.";
                case 12:
                    return "Moves Paradox Launcher CK3 database/cache files to quarantine so launcher rebuilds defaults.";
                case 14:
                    return "Rewrites dlc_load.json to no active mods and no disabled DLCs after backup.";
                case 15:
                    return "Rewrites pdx_settings.txt multiplayer stability keys after backup.";
                case 16:
                case 17:
                    return "Writes CK3MPS report/profile text files only.";
                case 18:
                    return "Moves CK3 player UI state to quarantine; CK3 recreates defaults.";
                case 19:
                    return "Moves OOS/crash/dump/exception diagnostic folders to quarantine reports.";
                case 20:
                    return "Moves CK3/Launcher cache folders to quarantine; game/launcher recreate defaults.";
                case 21:
                    return "Moves local .mod descriptor files to quarantine.";
                case 23:
                    return "May quarantine suspicious save pointers/files when a safer manual save exists.";
                case 24:
                    return "Broad CK3 Documents cleanup, keeping saves but moving generated clutter to quarantine.";
            }
            return "See step help for details.";
        }
    }
}
