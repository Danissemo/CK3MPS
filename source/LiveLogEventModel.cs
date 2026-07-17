using System;
using System.Collections.Generic;

namespace CK3MPS
{
    internal enum LiveLogEventType
    {
        UserAction,
        Progress,
        Result,
        Warning,
        Error,
        Diagnostic
    }

    internal enum LiveLogSeverity
    {
        Info,
        Warning,
        Error
    }

    internal sealed class LiveLogEvent
    {
        public string EventId;
        public LiveLogEventType Type;
        public string OperationId;
        public LiveLogSeverity Severity;
        public DateTime TimestampUtc;
        public string Text;
        public string DiagnosticPayload;

        public static LiveLogEvent FromLegacyLine(string operationId, string line)
        {
            string text = line ?? "";
            string trimmed = text.TrimStart();
            string upper = trimmed.ToUpperInvariant();
            LiveLogEventType type = LiveLogEventType.Progress;
            LiveLogSeverity severity = LiveLogSeverity.Info;

            if (upper.StartsWith("VERBOSE") || upper.StartsWith("DEBUG") || upper.StartsWith("TRACE") || upper.StartsWith("DIAGNOSTIC"))
                type = LiveLogEventType.Diagnostic;
            else if (upper.StartsWith("ERROR") || upper.StartsWith("FAIL") || upper.Contains(" NOT READY") || upper.Contains("FAILED POSTCONDITION") || upper.Contains("ROLLBACK FAILURE") || upper.Contains("SECURITY REFUSAL"))
            {
                type = LiveLogEventType.Error;
                severity = LiveLogSeverity.Error;
            }
            else if (upper.StartsWith("WARN") || upper.StartsWith("RISK") || upper.StartsWith("GUARD"))
            {
                type = LiveLogEventType.Warning;
                severity = LiveLogSeverity.Warning;
            }
            else if (upper.StartsWith("RESULT") || upper.StartsWith("OK") || upper.Contains(" READY"))
                type = LiveLogEventType.Result;
            else if (upper.StartsWith("START") || upper.StartsWith("ACTION"))
                type = LiveLogEventType.UserAction;

            return new LiveLogEvent
            {
                EventId = BuildStableEventId(type, text),
                Type = type,
                OperationId = String.IsNullOrWhiteSpace(operationId) ? "default" : operationId,
                Severity = severity,
                TimestampUtc = DateTime.UtcNow,
                Text = text,
                DiagnosticPayload = type == LiveLogEventType.Diagnostic ? text : null
            };
        }

        private static string BuildStableEventId(LiveLogEventType type, string text)
        {
            return type.ToString() + ":" + NormalizeForAggregation(text);
        }

        internal static string NormalizeForAggregation(string text)
        {
            return (text ?? "").Trim().ToUpperInvariant();
        }
    }

    internal sealed class LiveLogRenderedLine
    {
        public string OperationId;
        public string EventId;
        public LiveLogEventType Type;
        public LiveLogSeverity Severity;
        public string Text;
        public int RepeatCount;
        public bool ReplacesProgressLine;

        public string DisplayText
        {
            get
            {
                if (RepeatCount <= 1)
                    return Text ?? "";
                return (Text ?? "") + " (x" + RepeatCount + ")";
            }
        }
    }

    internal sealed class LiveLogEventModel
    {
        public const int DefaultMaxUiLines = 500;
        public static readonly TimeSpan DefaultRateLimitWindow = TimeSpan.FromSeconds(2);

        private readonly int maxUiLines;
        private readonly TimeSpan rateLimitWindow;
        private readonly bool diagnosticsVisible;
        private readonly List<LiveLogRenderedLine> visibleLines = new List<LiveLogRenderedLine>();
        private readonly List<LiveLogEvent> diagnosticFileEvents = new List<LiveLogEvent>();
        private readonly Dictionary<string, LiveLogRenderedLine> lastByAggregateKey = new Dictionary<string, LiveLogRenderedLine>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> lastProgressShownUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> progressLineIndexByOperation = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public LiveLogEventModel(int maxUiLines, TimeSpan rateLimitWindow, bool diagnosticsVisible)
        {
            this.maxUiLines = Math.Max(50, maxUiLines);
            this.rateLimitWindow = rateLimitWindow <= TimeSpan.Zero ? DefaultRateLimitWindow : rateLimitWindow;
            this.diagnosticsVisible = diagnosticsVisible;
        }

        public IList<LiveLogRenderedLine> VisibleLines
        {
            get { return visibleLines.AsReadOnly(); }
        }

        public IList<LiveLogEvent> DiagnosticFileEvents
        {
            get { return diagnosticFileEvents.AsReadOnly(); }
        }

        public LiveLogRenderedLine Accept(LiveLogEvent evt)
        {
            if (evt == null)
                return null;

            diagnosticFileEvents.Add(evt);

            if (evt.Type == LiveLogEventType.Diagnostic && !diagnosticsVisible)
                return null;

            if (IsAlwaysVisible(evt))
                return AddOrUpdateVisible(evt, false);

            if (evt.Type == LiveLogEventType.Progress)
            {
                string rateKey = evt.OperationId + "|" + evt.EventId;
                DateTime lastShown;
                if (lastProgressShownUtc.TryGetValue(rateKey, out lastShown) && evt.TimestampUtc - lastShown < rateLimitWindow)
                    return AggregateSuppressed(evt);
                lastProgressShownUtc[rateKey] = evt.TimestampUtc;
                return AddOrUpdateVisible(evt, true);
            }

            string aggregateKey = evt.OperationId + "|" + evt.EventId;
            if (lastByAggregateKey.ContainsKey(aggregateKey))
                return AggregateSuppressed(evt);

            return AddOrUpdateVisible(evt, false);
        }

        private LiveLogRenderedLine AggregateSuppressed(LiveLogEvent evt)
        {
            string aggregateKey = evt.OperationId + "|" + evt.EventId;
            LiveLogRenderedLine line;
            if (lastByAggregateKey.TryGetValue(aggregateKey, out line))
            {
                line.Text = evt.Text;
                line.Severity = evt.Severity;
                line.Type = evt.Type;
                line.RepeatCount++;
                return line;
            }
            return AddOrUpdateVisible(evt, evt.Type == LiveLogEventType.Progress);
        }

        private LiveLogRenderedLine AddOrUpdateVisible(LiveLogEvent evt, bool replaceProgress)
        {
            string aggregateKey = evt.OperationId + "|" + evt.EventId;
            LiveLogRenderedLine existing;
            if (lastByAggregateKey.TryGetValue(aggregateKey, out existing) && evt.Type != LiveLogEventType.Result && evt.Type != LiveLogEventType.Error)
            {
                existing.Text = evt.Text;
                existing.Severity = evt.Severity;
                existing.Type = evt.Type;
                existing.RepeatCount++;
                return existing;
            }

            LiveLogRenderedLine line = new LiveLogRenderedLine
            {
                OperationId = evt.OperationId,
                EventId = evt.EventId,
                Type = evt.Type,
                Severity = evt.Severity,
                Text = evt.Text,
                RepeatCount = 1,
                ReplacesProgressLine = replaceProgress
            };

            int index;
            if (replaceProgress && progressLineIndexByOperation.TryGetValue(evt.OperationId, out index) && index >= 0 && index < visibleLines.Count)
            {
                visibleLines[index] = line;
            }
            else
            {
                visibleLines.Add(line);
                if (replaceProgress)
                    progressLineIndexByOperation[evt.OperationId] = visibleLines.Count - 1;
            }

            lastByAggregateKey[aggregateKey] = line;
            TrimUiBuffer();
            return line;
        }

        private void TrimUiBuffer()
        {
            while (visibleLines.Count > maxUiLines)
            {
                visibleLines.RemoveAt(0);
                RebuildProgressIndex();
            }
        }

        private void RebuildProgressIndex()
        {
            progressLineIndexByOperation.Clear();
            for (int i = 0; i < visibleLines.Count; i++)
                if (visibleLines[i].Type == LiveLogEventType.Progress)
                    progressLineIndexByOperation[visibleLines[i].OperationId] = i;
        }

        private static bool IsAlwaysVisible(LiveLogEvent evt)
        {
            if (evt.Type == LiveLogEventType.Result || evt.Type == LiveLogEventType.Warning || evt.Type == LiveLogEventType.Error || evt.Severity != LiveLogSeverity.Info)
                return true;

            string upper = (evt.Text ?? "").ToUpperInvariant();
            return upper.Contains("NOT READY")
                || upper.Contains("ROLLBACK FAILURE")
                || upper.Contains("SECURITY REFUSAL")
                || upper.Contains("FAILED POSTCONDITION")
                || upper.Contains("FAILED CHECK");
        }
    }
}
