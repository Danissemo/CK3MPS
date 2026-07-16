using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace CK3MPS
{
    internal sealed class HostSuitabilityResult { }
    internal sealed class HostSaveCandidateResult { }
    internal sealed class OosDeepInsight { }
    internal sealed class OosIncidentState { }
    internal sealed class WorkflowStepState { }

    internal sealed class WorkflowScenarioSnapshot
    {
        public string Scenario = "";
        public string Verdict = "";
        public string Summary = "";
        public List<WorkflowStepState> States = new List<WorkflowStepState>();
    }

    internal sealed partial class MainForm : Form
    {
        private readonly System.Windows.Forms.Timer workflowRenderTimer = new System.Windows.Forms.Timer();
        private bool workflowRefreshPending;
        private int workflowLoadGeneration;
        private string currentWorkflowScenario = "Start Session";

        private int hostCalls;
        private int saveCalls;
        private int oosCalls;
        private int incidentCalls;
        private bool failSaveAnalysis;

        private HostSuitabilityResult AnalyzeHostSuitability()
        {
            hostCalls++;
            return new HostSuitabilityResult();
        }

        private HostSaveCandidateResult AnalyzeWorkflowHostSaveCandidate()
        {
            saveCalls++;
            if (failSaveAnalysis)
                throw new InvalidOperationException("injected save analysis failure");
            return new HostSaveCandidateResult();
        }

        private OosDeepInsight AnalyzeLatestOosDeepInsight()
        {
            oosCalls++;
            return new OosDeepInsight();
        }

        private OosIncidentState AnalyzeOosIncidentState()
        {
            incidentCalls++;
            CurrentWorkflowAnalysis();
            return new OosIncidentState();
        }

        private void BuildWorkflowScenarioSteps(string scenario, List<WorkflowStepState> states)
        {
            CurrentWorkflowAnalysis();
            CurrentWorkflowAnalysis();
            states.Add(new WorkflowStepState());
        }

        private string BuildWorkflowVerdictLine(string scenario, List<WorkflowStepState> states)
        {
            CurrentWorkflowAnalysis();
            return "verdict:" + scenario;
        }

        private string BuildWorkflowScenarioSummaryText(string scenario, List<WorkflowStepState> states)
        {
            CurrentWorkflowAnalysis();
            return "summary:" + scenario;
        }

        internal CancellationToken TestBeginRefresh(string scenario)
        {
            currentWorkflowScenario = scenario;
            workflowRefreshPending = true;
            workflowLoadGeneration++;
            return BeginWorkflowRefreshCancellation();
        }

        internal bool TestIsCurrent(int generation, string scenario, CancellationToken token)
        {
            return WorkflowRefreshStillCurrent(generation, scenario, token);
        }

        internal void TestCancelRefresh()
        {
            CancelWorkflowScenarioRefresh();
        }

        internal void TestCloseForm()
        {
            OnFormClosing(new FormClosingEventArgs(CloseReason.UserClosing, false));
        }

        internal WorkflowScenarioSnapshot TestBuildSnapshot(string scenario, CancellationToken token)
        {
            return BuildWorkflowScenarioSnapshotCore(scenario, token);
        }

        internal void TestResetAnalysisCounters()
        {
            hostCalls = 0;
            saveCalls = 0;
            oosCalls = 0;
            incidentCalls = 0;
            failSaveAnalysis = false;
        }

        internal void TestInjectSaveFailure(bool enabled)
        {
            failSaveAnalysis = enabled;
        }

        internal int TestHostCalls { get { return hostCalls; } }
        internal int TestSaveCalls { get { return saveCalls; } }
        internal int TestOosCalls { get { return oosCalls; } }
        internal int TestIncidentCalls { get { return incidentCalls; } }
        internal int TestGeneration { get { return workflowLoadGeneration; } }
        internal bool TestPending { get { return workflowRefreshPending; } }
    }
}

internal static class WorkflowRefreshRegressionTests
{
    [STAThread]
    private static int Main()
    {
        try
        {
            TestRapidScenarioSwitchAndStaleRender();
            TestExplicitCancellationResetsPending();
            TestFormClosingCancelsActiveRefresh();
            TestSingleImmutableAnalysisSnapshot();
            TestAnalysisFailureDoesNotPoisonSnapshotContext();
            Console.WriteLine("Workflow refresh regression tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void TestRapidScenarioSwitchAndStaleRender()
    {
        using (CK3MPS.MainForm form = new CK3MPS.MainForm())
        {
            CancellationToken first = form.TestBeginRefresh("Start Session");
            int firstGeneration = form.TestGeneration;
            Assert(form.TestIsCurrent(firstGeneration, "Start Session", first), "first refresh should initially be current");

            CancellationToken second = form.TestBeginRefresh("After OOS");
            int secondGeneration = form.TestGeneration;
            Assert(first.IsCancellationRequested, "starting a new scenario must cancel the previous refresh");
            Assert(!form.TestIsCurrent(firstGeneration, "Start Session", first), "old generation must not render after a rapid scenario switch");
            Assert(!form.TestIsCurrent(secondGeneration, "Start Session", second), "scenario mismatch must reject a render even with the current token");
            Assert(form.TestIsCurrent(secondGeneration, "After OOS", second), "new scenario refresh should remain current");

            CancellationToken third = form.TestBeginRefresh("Rehost");
            int thirdGeneration = form.TestGeneration;
            CancellationToken fourth = form.TestBeginRefresh("Hotjoin");
            int fourthGeneration = form.TestGeneration;
            Assert(third.IsCancellationRequested, "each rapid switch must cancel its immediate predecessor");
            Assert(!form.TestIsCurrent(thirdGeneration, "Rehost", third), "queued stale refresh must be rejected");
            Assert(form.TestIsCurrent(fourthGeneration, "Hotjoin", fourth), "latest refresh must win");
        }
    }

    private static void TestExplicitCancellationResetsPending()
    {
        using (CK3MPS.MainForm form = new CK3MPS.MainForm())
        {
            CancellationToken token = form.TestBeginRefresh("Start Session");
            int generation = form.TestGeneration;
            Assert(form.TestPending, "refresh should mark pending before cancellation");
            form.TestCancelRefresh();
            Assert(token.IsCancellationRequested, "explicit cancellation must cancel the active token");
            Assert(!form.TestPending, "explicit cancellation must reset workflowRefreshPending");
            Assert(!form.TestIsCurrent(generation, "Start Session", token), "cancelled refresh must never be current");
        }
    }

    private static void TestFormClosingCancelsActiveRefresh()
    {
        using (CK3MPS.MainForm form = new CK3MPS.MainForm())
        {
            CancellationToken token = form.TestBeginRefresh("After OOS");
            int generation = form.TestGeneration;
            form.TestCloseForm();
            Assert(token.IsCancellationRequested, "form closing must cancel the active refresh");
            Assert(!form.TestPending, "form closing must clear the pending flag");
            Assert(!form.TestIsCurrent(generation, "After OOS", token), "closing form must invalidate stale BeginInvoke work");

            CancellationToken afterClose = form.TestBeginRefresh("Rehost");
            Assert(afterClose.IsCancellationRequested, "no new refresh may start after shutdown begins");
        }
    }

    private static void TestSingleImmutableAnalysisSnapshot()
    {
        using (CK3MPS.MainForm form = new CK3MPS.MainForm())
        {
            form.TestResetAnalysisCounters();
            CK3MPS.WorkflowScenarioSnapshot snapshot = form.TestBuildSnapshot("Start Session", CancellationToken.None);
            Assert(snapshot != null && snapshot.States.Count == 1, "scenario snapshot should be built");
            Assert(form.TestHostCalls == 1, "host analysis must run exactly once per snapshot");
            Assert(form.TestSaveCalls == 1, "selected save analysis must run exactly once per snapshot");
            Assert(form.TestOosCalls == 1, "OOS analysis must run exactly once per snapshot");
            Assert(form.TestIncidentCalls == 1, "incident analysis must run exactly once per snapshot");
        }
    }

    private static void TestAnalysisFailureDoesNotPoisonSnapshotContext()
    {
        using (CK3MPS.MainForm form = new CK3MPS.MainForm())
        {
            form.TestResetAnalysisCounters();
            form.TestInjectSaveFailure(true);
            bool failed = false;
            try
            {
                form.TestBuildSnapshot("Start Session", CancellationToken.None);
            }
            catch (InvalidOperationException ex)
            {
                failed = ex.Message.IndexOf("injected", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            Assert(failed, "injected analysis failure should propagate to the refresh error path");

            form.TestInjectSaveFailure(false);
            CK3MPS.WorkflowScenarioSnapshot recovered = form.TestBuildSnapshot("Start Session", CancellationToken.None);
            Assert(recovered != null, "a later refresh should recover after an analysis failure");
            Assert(form.TestHostCalls == 2, "failed snapshot must not be reused as cached analysis state");
            Assert(form.TestSaveCalls == 2, "save analysis should rerun cleanly after failure");
            Assert(form.TestOosCalls == 1 && form.TestIncidentCalls == 1, "only the successful retry should reach OOS and incident analysis");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
