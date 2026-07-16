using System;
using System.Threading;

namespace CK3MPS
{
    internal sealed partial class MainForm
    {
        private sealed class WorkflowAnalysisSnapshot
        {
            public HostSuitabilityResult Host = new HostSuitabilityResult();
            public HostSaveCandidateResult Save = new HostSaveCandidateResult();
            public OosDeepInsight Oos = new OosDeepInsight();
            public OosIncidentState Incident = new OosIncidentState();
            public DateTime CapturedUtc;
        }

        private readonly AsyncLocal<WorkflowAnalysisSnapshot> workflowAnalysisContext = new AsyncLocal<WorkflowAnalysisSnapshot>();
        private readonly object workflowRefreshCancellationSync = new object();
        private CancellationTokenSource workflowRefreshCancellation;

        private WorkflowAnalysisSnapshot CaptureWorkflowAnalysisSnapshot(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkflowAnalysisSnapshot snapshot = new WorkflowAnalysisSnapshot();
            snapshot.Host = AnalyzeHostSuitability();
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.Save = AnalyzeWorkflowHostSaveCandidate();
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.Oos = AnalyzeLatestOosDeepInsight();
            cancellationToken.ThrowIfCancellationRequested();

            WorkflowAnalysisSnapshot previous = workflowAnalysisContext.Value;
            workflowAnalysisContext.Value = snapshot;
            try
            {
                snapshot.Incident = AnalyzeOosIncidentState();
            }
            finally
            {
                workflowAnalysisContext.Value = previous;
            }
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.CapturedUtc = DateTime.UtcNow;
            return snapshot;
        }

        private WorkflowAnalysisSnapshot CurrentWorkflowAnalysis()
        {
            WorkflowAnalysisSnapshot snapshot = workflowAnalysisContext.Value;
            return snapshot ?? CaptureWorkflowAnalysisSnapshot(CancellationToken.None);
        }

        private WorkflowScenarioSnapshot BuildWorkflowScenarioSnapshotCore(string scenario, CancellationToken cancellationToken)
        {
            WorkflowAnalysisSnapshot analysis = CaptureWorkflowAnalysisSnapshot(cancellationToken);
            WorkflowAnalysisSnapshot previous = workflowAnalysisContext.Value;
            workflowAnalysisContext.Value = analysis;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                WorkflowScenarioSnapshot snapshot = new WorkflowScenarioSnapshot();
                snapshot.Scenario = scenario;
                BuildWorkflowScenarioSteps(scenario, snapshot.States);
                cancellationToken.ThrowIfCancellationRequested();
                snapshot.Verdict = BuildWorkflowVerdictLine(scenario, snapshot.States);
                snapshot.Summary = BuildWorkflowScenarioSummaryText(scenario, snapshot.States);
                cancellationToken.ThrowIfCancellationRequested();
                return snapshot;
            }
            finally
            {
                workflowAnalysisContext.Value = previous;
            }
        }

        private CancellationToken BeginWorkflowRefreshCancellation()
        {
            lock (workflowRefreshCancellationSync)
            {
                if (workflowRefreshCancellation != null)
                {
                    try { workflowRefreshCancellation.Cancel(); } catch { }
                    workflowRefreshCancellation.Dispose();
                }
                workflowRefreshCancellation = new CancellationTokenSource();
                return workflowRefreshCancellation.Token;
            }
        }

        private void CancelWorkflowScenarioRefresh()
        {
            lock (workflowRefreshCancellationSync)
            {
                workflowLoadGeneration++;
                if (workflowRefreshCancellation != null)
                {
                    try { workflowRefreshCancellation.Cancel(); } catch { }
                    workflowRefreshCancellation.Dispose();
                    workflowRefreshCancellation = null;
                }
            }
            workflowRefreshPending = false;
            workflowRenderTimer.Stop();
        }

        private bool WorkflowRefreshStillCurrent(int generation, string scenario, CancellationToken cancellationToken)
        {
            return !cancellationToken.IsCancellationRequested
                && generation == workflowLoadGeneration
                && String.Equals(scenario, currentWorkflowScenario, StringComparison.OrdinalIgnoreCase);
        }
    }
}
