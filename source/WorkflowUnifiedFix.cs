using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private readonly Button workflowUnifiedFixSaveHostButton = new Button();
        private bool workflowUnifiedFixUiConfigured;
        private bool workflowUnifiedFixInProgress;

        static MainForm()
        {
            Application.Idle += delegate
            {
                foreach (Form form in Application.OpenForms)
                {
                    MainForm main = form as MainForm;
                    if (main == null)
                        continue;
                    main.ConfigureUnifiedFixSaveHostWorkflowUi();
                }
            };
        }

        private void ConfigureUnifiedFixSaveHostWorkflowUi()
        {
            if (workflowUnifiedFixUiConfigured || workflowHeaderPanel == null || !workflowHeaderPanel.IsHandleCreated)
                return;
            if (!workflowHeaderPanel.Controls.Contains(workflowApplySafeStartButton))
                return;

            workflowUnifiedFixUiConfigured = true;
            workflowApplySafeStartButton.Visible = false;
            workflowApplySafeStartButton.Enabled = false;
            workflowRepairSaveButton.Visible = false;
            workflowRepairSaveButton.Enabled = false;

            workflowUnifiedFixSaveHostButton.Text = "Fix save + host";
            workflowUnifiedFixSaveHostButton.Click += delegate { RunUnifiedFixSaveHostWorkflow(); };
            workflowHeaderPanel.Controls.Add(workflowUnifiedFixSaveHostButton);
            StyleWorkflowActionButton(workflowUnifiedFixSaveHostButton, true);
            stepToolTip.SetToolTip(workflowUnifiedFixSaveHostButton, BuildUnifiedFixSaveHostHintText());
            workflowHeaderPanel.Resize += delegate { LayoutUnifiedFixSaveHostButton(); };
            workflowPage.Resize += delegate { LayoutUnifiedFixSaveHostButton(); };
            LayoutUnifiedFixSaveHostButton();
            UpdateUnifiedFixSaveHostAvailability();
        }

        private void LayoutUnifiedFixSaveHostButton()
        {
            if (!workflowUnifiedFixUiConfigured)
                return;

            workflowApplySafeStartButton.Visible = false;
            workflowRepairSaveButton.Visible = false;
            workflowUnifiedFixSaveHostButton.Location = workflowApplySafeStartButton.Location;
            workflowUnifiedFixSaveHostButton.Size = new Size(150, workflowApplySafeStartButton.Height > 0 ? workflowApplySafeStartButton.Height : 34);
            UpdateUnifiedFixSaveHostAvailability();
        }

        private void UpdateUnifiedFixSaveHostAvailability()
        {
            if (!workflowUnifiedFixUiConfigured)
                return;

            bool gameRunning = IsGameRunning();
            bool allowedScenario = String.Equals(currentWorkflowScenario, "Start Session", StringComparison.OrdinalIgnoreCase)
                || String.Equals(currentWorkflowScenario, "After OOS", StringComparison.OrdinalIgnoreCase)
                || String.Equals(currentWorkflowScenario, "Rehost", StringComparison.OrdinalIgnoreCase);

            workflowUnifiedFixSaveHostButton.Enabled = allowedScenario && !gameRunning && !workflowUnifiedFixInProgress;
            if (!allowedScenario)
                stepToolTip.SetToolTip(workflowUnifiedFixSaveHostButton, "Fix save + host is not part of Hotjoin. If hotjoin fails, close CK3 and continue through Rehost or After OOS.");
            else if (gameRunning)
                stepToolTip.SetToolTip(workflowUnifiedFixSaveHostButton, "Fix save + host belongs to the next attempt, not the live session. Close CK3 first, then run it.");
            else
                stepToolTip.SetToolTip(workflowUnifiedFixSaveHostButton, BuildUnifiedFixSaveHostHintText());
        }

        private string BuildUnifiedFixSaveHostHintText()
        {
            return "Fix save + host runs one verified workflow: snapshot, supported save fixes, host profile fixes, repeat save/host checks, readiness and one honest result.";
        }

        private void RunUnifiedFixSaveHostWorkflow()
        {
            if (workflowUnifiedFixInProgress)
            {
                MessageBox.Show("Fix save + host is already running.", "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (IsGameRunning())
            {
                MessageBox.Show("Close CK3 and Paradox Launcher before running Fix save + host.", "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!ValidateBeforeRun())
            {
                MessageBox.Show("Fix the configured game/settings paths first.", "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            UnifiedFixPlan plan;
            try
            {
                plan = BuildUnifiedFixPlan();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not prepare Fix save + host.\r\n\r\n" + ex.Message, "CK3MPS workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                BuildUnifiedFixPreflightText(plan),
                "Fix save + host",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (confirmation != DialogResult.OK)
            {
                Log("INFO Workflow Fix save + host cancelled before mutation phase.");
                return;
            }

            workflowUnifiedFixInProgress = true;
            workflowUnifiedFixSaveHostButton.Enabled = false;
            workflowUnifiedFixSaveHostButton.Text = "Running...";
            workflowProgressBar.Visible = true;
            workflowProgressBar.Style = ProgressBarStyle.Blocks;
            workflowProgressBar.Minimum = 0;
            workflowProgressBar.Maximum = 8;
            workflowProgressBar.Value = 0;
            UseWaitCursor = true;

            UnifiedFixResult result = new UnifiedFixResult();
            result.BeforeHost = plan.BeforeHost;
            result.BeforeSave = plan.BeforeSave;
            try
            {
                RunUnifiedFixSaveHostWorkflowCore(plan, result);
            }
            catch (Exception ex)
            {
                result.Status = "Failed";
                result.RemainingIssues.Add("Critical failure: " + ex.Message);
                result.RollbackActions.Add("Stopped immediately after critical failure. Use Restore tab data created by the attempted operation if any state must be reverted.");
                Log("FAIL Workflow Fix save + host critical failure: " + ex.Message);
            }
            finally
            {
                UseWaitCursor = false;
                workflowProgressBar.Visible = false;
                workflowUnifiedFixInProgress = false;
                workflowUnifiedFixSaveHostButton.Text = "Fix save + host";
                UpdateUnifiedFixSaveHostAvailability();
            }

            FinalizeUnifiedFixResult(result);
        }

        private UnifiedFixPlan BuildUnifiedFixPlan()
        {
            CancelWorkflowScenarioRefresh();
            ClearWorkflowScenarioSnapshots();
            InvalidateHostSuitabilityCache();
            InvalidateHostSaveAnalysisCache();

            UnifiedFixPlan plan = new UnifiedFixPlan();
            plan.BeforeHost = AnalyzeHostSuitability();
            plan.BeforeSave = AnalyzeWorkflowHostSaveCandidate();
            plan.SaveSupported = plan.BeforeSave != null
                && plan.BeforeSave.Save != null
                && plan.BeforeSave.Save.Readable
                && plan.BeforeSave.Save.VersionMatchesInstalled
                && !String.IsNullOrWhiteSpace(plan.BeforeSave.Save.Path)
                && File.Exists(plan.BeforeSave.Save.Path);
            plan.NeedsSaveFix = plan.BeforeSave != null
                && (plan.BeforeSave.Score < 70 || !AllCriticalRulesSafe(plan.BeforeSave.Save.Rules));
            plan.NeedsHostFix = plan.BeforeHost != null && !plan.BeforeHost.Suitable;

            if (plan.BeforeSave == null || plan.BeforeSave.Save == null || String.IsNullOrWhiteSpace(plan.BeforeSave.Save.Path) || !File.Exists(plan.BeforeSave.Save.Path))
                plan.Unsupported.Add("No existing selected save is available for save mutation.");
            else if (!plan.BeforeSave.Save.Readable)
                plan.Unsupported.Add("Selected save is not safely readable; save mutation is blocked.");
            else if (!plan.BeforeSave.Save.VersionMatchesInstalled)
                plan.Unsupported.Add("Selected save version does not match the installed CK3 version; save mutation is blocked.");

            foreach (string issue in plan.BeforeSave.Issues)
                plan.SaveFindings.Add(issue);
            foreach (string risk in plan.BeforeHost.Risks)
                plan.HostFindings.Add(risk);
            if (plan.NeedsSaveFix && plan.SaveSupported)
                plan.WillFix.Add("selected save critical-rule/profile findings");
            if (plan.NeedsHostFix)
                plan.WillFix.Add("host profile and parity/readiness findings");
            if (plan.WillFix.Count == 0 && plan.Unsupported.Count == 0)
                plan.WillFix.Add("nothing; current snapshot has no supported fixable findings");
            plan.BackupActions.Add("Steam and launcher settings backup before host mutation.");
            plan.BackupActions.Add("Restore manifest entries for repaired save/surgery baseline files.");
            plan.BackupActions.Add("Workflow/parity/readiness reports refreshed after verification.");
            return plan;
        }

        private void RunUnifiedFixSaveHostWorkflowCore(UnifiedFixPlan plan, UnifiedFixResult result)
        {
            EnsureStabilizerRoot();
            if (String.IsNullOrEmpty(GetKnownQuarantine()))
                CreateQuarantine();
            SetUnifiedProgress(1, "Captured immutable workflow snapshot.");

            if (plan.NeedsSaveFix)
            {
                if (!plan.SaveSupported)
                {
                    result.Unsupported.AddRange(plan.Unsupported);
                    Log("WARN Workflow Fix save + host skipped unsupported save mutation.");
                }
                else
                {
                    SetUnifiedProgress(2, "Applying supported selected-save fix.");
                    bool repaired = EnsureSafeWorkflowHostSave();
                    PrepareWorkflowSaveSurgeryBaseline();
                    InvalidateHostSaveAnalysisCache();
                    HostSaveCandidateResult afterSaveOnly = AnalyzeWorkflowHostSaveCandidate();
                    bool verified = afterSaveOnly != null && afterSaveOnly.Score >= plan.BeforeSave.Score && AllCriticalRulesSafe(afterSaveOnly.Save.Rules);
                    if (!repaired || !verified)
                    {
                        result.RemainingIssues.Add("Save mutation did not verify cleanly; stopping before host mutation.");
                        result.RollbackActions.Add("Save repair uses a copied safe save/baseline with restore records; original selected save is not overwritten by this unified workflow.");
                        result.AfterSave = afterSaveOnly;
                        result.Status = "Failed";
                        return;
                    }
                    result.FixedIssues.Add("Save critical rules verified after repair: " + plan.BeforeSave.Score + " -> " + afterSaveOnly.Score + ".");
                }
            }
            else
            {
                result.FixedIssues.Add("Save had no supported fixable finding in the captured snapshot.");
            }

            SetUnifiedProgress(3, "Preparing host transaction and backup data.");
            if (plan.NeedsHostFix)
            {
                BackupSteamAndLauncherSettings();
                StabilizeSteamSettings();
                ForceNoMods();
                StabilizePdxSettings();
                WriteStableGameRuleProfile();
                result.FixedIssues.Add("Host profile mutation applied for exact host findings.");
            }
            else
            {
                result.FixedIssues.Add("Host had no supported fixable finding in the captured snapshot.");
            }

            SetUnifiedProgress(4, "Refreshing reports and parity manifest.");
            WriteMultiplayerParityManifest();
            WriteHostSuitabilityReport();
            WriteHostSavePreparationReport();
            WriteRuntimeVerificationReport();

            SetUnifiedProgress(5, "Repeating save analysis.");
            InvalidateHostSaveAnalysisCache();
            result.AfterSave = AnalyzeWorkflowHostSaveCandidate();

            SetUnifiedProgress(6, "Repeating host prerequisite checks.");
            InvalidateHostSuitabilityCache();
            result.AfterHost = AnalyzeHostSuitability();

            SetUnifiedProgress(7, "Checking postconditions.");
            if (result.AfterSave != null && result.AfterSave.Score < 70)
                result.RemainingIssues.Add("Save readiness still below threshold: " + result.AfterSave.Score + "/100.");
            if (result.AfterHost != null && !result.AfterHost.Suitable)
                result.RemainingIssues.Add("Host readiness still has blockers: " + result.AfterHost.Score + "/100.");
            if (result.Unsupported.Count == 0)
                result.Unsupported.AddRange(plan.Unsupported);

            if (result.RemainingIssues.Count == 0 && (plan.NeedsHostFix || plan.NeedsSaveFix))
                result.Status = "Succeeded";
            else if (result.FixedIssues.Count > 0 && result.RemainingIssues.Count > 0)
                result.Status = "PartiallySucceeded";
            else if (result.Unsupported.Count > 0 && !plan.NeedsHostFix && (!plan.NeedsSaveFix || !plan.SaveSupported))
                result.Status = "Unsupported";
            else if (!plan.NeedsHostFix && !plan.NeedsSaveFix)
                result.Status = "Succeeded";
            else
                result.Status = "Failed";

            SetUnifiedProgress(8, "Unified result ready.");
        }

        private void SetUnifiedProgress(int value, string message)
        {
            workflowProgressBar.Value = Math.Min(workflowProgressBar.Maximum, Math.Max(workflowProgressBar.Minimum, value));
            workflowVerdictLabel.Text = "Status: " + message;
            ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
            Application.DoEvents();
        }

        private void FinalizeUnifiedFixResult(UnifiedFixResult result)
        {
            if (result.AfterHost == null)
                result.AfterHost = AnalyzeHostSuitability();
            if (result.AfterSave == null)
                result.AfterSave = AnalyzeWorkflowHostSaveCandidate();

            string report = BuildUnifiedFixResultReport(result);
            workflowSummaryBox.Text = report;
            workflowVerdictLabel.Text = "Status: Fix save + host " + result.Status;
            ApplyWorkflowVerdictStyle(workflowVerdictLabel.Text);
            ApplyWorkflowSummaryStyling();
            Log("RESULT| Fix save + host " + result.Status + " | host " + ScoreText(result.BeforeHost) + " -> " + ScoreText(result.AfterHost) + " | save " + ScoreText(result.BeforeSave) + " -> " + ScoreText(result.AfterSave));
            SetStatusText("Fix save + host " + result.Status + ". Host " + ScoreText(result.BeforeHost) + " -> " + ScoreText(result.AfterHost) + ", save " + ScoreText(result.BeforeSave) + " -> " + ScoreText(result.AfterSave) + ".");
            ClearWorkflowScenarioSnapshots();
            BeginWorkflowScenarioRefresh();
        }

        private string BuildUnifiedFixPreflightText(UnifiedFixPlan plan)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Fix save + host preflight");
            sb.AppendLine();
            sb.AppendLine("Found");
            AppendListOrNone(sb, plan.SaveFindings, "- save: no save findings");
            AppendListOrNone(sb, plan.HostFindings, "- host: no host findings");
            sb.AppendLine();
            sb.AppendLine("Will fix");
            AppendListOrNone(sb, plan.WillFix, "- nothing");
            sb.AppendLine();
            sb.AppendLine("Unsupported / blocked");
            AppendListOrNone(sb, plan.Unsupported, "- none");
            sb.AppendLine();
            sb.AppendLine("Backup / restore data");
            AppendListOrNone(sb, plan.BackupActions, "- none");
            sb.AppendLine();
            sb.AppendLine("Press OK to start. Cancel is safe here because no mutation has started yet.");
            return sb.ToString();
        }

        private string BuildUnifiedFixResultReport(UnifiedFixResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Fix save + host result");
            sb.AppendLine("Status: " + result.Status);
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Readiness before/after");
            sb.AppendLine("- Host: " + ScoreText(result.BeforeHost) + " -> " + ScoreText(result.AfterHost));
            sb.AppendLine("- Save: " + ScoreText(result.BeforeSave) + " -> " + ScoreText(result.AfterSave));
            sb.AppendLine();
            sb.AppendLine("Fixed problems");
            AppendListOrNone(sb, result.FixedIssues, "- none");
            sb.AppendLine();
            sb.AppendLine("Remaining problems");
            AppendListOrNone(sb, result.RemainingIssues, "- none");
            sb.AppendLine();
            sb.AppendLine("Unsupported cases");
            AppendListOrNone(sb, result.Unsupported, "- none");
            sb.AppendLine();
            sb.AppendLine("Rollback / recovery notes");
            AppendListOrNone(sb, result.RollbackActions, "- no rollback was required");
            return sb.ToString();
        }

        private void AppendListOrNone(StringBuilder sb, List<string> items, string noneLine)
        {
            if (items == null || items.Count == 0)
            {
                sb.AppendLine(noneLine);
                return;
            }

            foreach (string item in items)
                sb.AppendLine("- " + item);
        }

        private string ScoreText(HostSuitabilityResult result)
        {
            return result == null ? "n/a" : result.Score + "/100";
        }

        private string ScoreText(HostSaveCandidateResult result)
        {
            return result == null ? "n/a" : result.Score + "/100";
        }

        private sealed class UnifiedFixPlan
        {
            public HostSuitabilityResult BeforeHost;
            public HostSaveCandidateResult BeforeSave;
            public bool NeedsHostFix;
            public bool NeedsSaveFix;
            public bool SaveSupported;
            public readonly List<string> SaveFindings = new List<string>();
            public readonly List<string> HostFindings = new List<string>();
            public readonly List<string> WillFix = new List<string>();
            public readonly List<string> Unsupported = new List<string>();
            public readonly List<string> BackupActions = new List<string>();
        }

        private sealed class UnifiedFixResult
        {
            public string Status = "Failed";
            public HostSuitabilityResult BeforeHost;
            public HostSuitabilityResult AfterHost;
            public HostSaveCandidateResult BeforeSave;
            public HostSaveCandidateResult AfterSave;
            public readonly List<string> FixedIssues = new List<string>();
            public readonly List<string> RemainingIssues = new List<string>();
            public readonly List<string> Unsupported = new List<string>();
            public readonly List<string> RollbackActions = new List<string>();
        }
    }
}
