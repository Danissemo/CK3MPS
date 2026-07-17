using System;
using System.Collections.Generic;

namespace CK3MPS
{
    internal interface IClock
    {
        DateTime UtcNow { get; }
    }

    internal sealed class SystemClock : IClock
    {
        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }
    }

    internal sealed class LogEventRequest
    {
        public LogEventRequest(string operationId, string legacyLine)
        {
            OperationId = String.IsNullOrWhiteSpace(operationId) ? "default" : operationId;
            LegacyLine = legacyLine ?? "";
        }

        public string OperationId { get; private set; }
        public string LegacyLine { get; private set; }
    }

    internal sealed class LogEventResult
    {
        public LogEventResult(LiveLogEvent evt, LiveLogRenderedLine renderedLine)
        {
            Event = evt;
            RenderedLine = renderedLine;
        }

        public LiveLogEvent Event { get; private set; }
        public LiveLogRenderedLine RenderedLine { get; private set; }
        public bool ShouldRender
        {
            get { return RenderedLine != null; }
        }
    }

    internal interface ILoggingEventService
    {
        LogEventResult Publish(LogEventRequest request);
        IList<LiveLogRenderedLine> SnapshotVisibleLines();
        IList<LiveLogEvent> SnapshotDiagnosticEvents();
    }

    /// <summary>
    /// UI-independent boundary for legacy log classification, aggregation and visibility policy.
    /// The service never touches WinForms controls, files or MessageBox.
    /// </summary>
    internal sealed class LoggingEventService : ILoggingEventService
    {
        private readonly object sync = new object();
        private readonly LiveLogEventModel model;
        private readonly IClock clock;

        public LoggingEventService(int maxUiLines, TimeSpan rateLimitWindow, bool diagnosticsVisible, IClock clock)
        {
            model = new LiveLogEventModel(maxUiLines, rateLimitWindow, diagnosticsVisible);
            this.clock = clock ?? new SystemClock();
        }

        public LogEventResult Publish(LogEventRequest request)
        {
            if (request == null)
                throw new ArgumentNullException("request");

            LiveLogEvent evt = LiveLogEvent.FromLegacyLine(request.OperationId, request.LegacyLine);
            evt.TimestampUtc = clock.UtcNow;

            lock (sync)
            {
                return new LogEventResult(evt, model.Accept(evt));
            }
        }

        public IList<LiveLogRenderedLine> SnapshotVisibleLines()
        {
            lock (sync)
                return new List<LiveLogRenderedLine>(model.VisibleLines).AsReadOnly();
        }

        public IList<LiveLogEvent> SnapshotDiagnosticEvents()
        {
            lock (sync)
                return new List<LiveLogEvent>(model.DiagnosticFileEvents).AsReadOnly();
        }
    }
}
