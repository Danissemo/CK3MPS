using System;
using CK3MPS;

internal static class LoggingEventServiceTests
{
    private sealed class FixedClock : IClock
    {
        public DateTime Value;
        public DateTime UtcNow { get { return Value; } }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static int Main()
    {
        FixedClock clock = new FixedClock { Value = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc) };
        LoggingEventService service = new LoggingEventService(100, TimeSpan.FromSeconds(2), false, clock);

        LogEventResult warning = service.Publish(new LogEventRequest("scan-1", "WARN path is missing"));
        Assert(warning.ShouldRender, "Warnings must be rendered.");
        Assert(warning.Event.Type == LiveLogEventType.Warning, "Legacy warning classification changed.");
        Assert(warning.Event.TimestampUtc == clock.Value, "Clock dependency was not used.");

        LogEventResult diagnostic = service.Publish(new LogEventRequest("scan-1", "DEBUG internal detail"));
        Assert(!diagnostic.ShouldRender, "Diagnostics must remain hidden in normal mode.");
        Assert(service.SnapshotDiagnosticEvents().Count == 2, "Hidden diagnostics must remain available for support logs.");

        LogEventResult firstProgress = service.Publish(new LogEventRequest("scan-1", "INFO scanning files"));
        clock.Value = clock.Value.AddMilliseconds(200);
        LogEventResult repeatedProgress = service.Publish(new LogEventRequest("scan-1", "INFO scanning files"));
        Assert(firstProgress.ShouldRender && repeatedProgress.ShouldRender, "Progress aggregation must preserve a render model.");
        Assert(repeatedProgress.RenderedLine.RepeatCount == 2, "Repeated progress must aggregate.");

        LogEventResult otherOperation = service.Publish(new LogEventRequest("scan-2", "INFO scanning files"));
        Assert(otherOperation.RenderedLine.RepeatCount == 1, "Operations must not share aggregation state.");

        LogEventResult error = service.Publish(new LogEventRequest("scan-1", "ERROR failed postcondition"));
        Assert(error.ShouldRender, "Errors must never be suppressed.");
        Assert(error.Event.Severity == LiveLogSeverity.Error, "Error severity mapping changed.");

        Console.WriteLine("LoggingEventService characterization tests passed.");
        return 0;
    }
}
