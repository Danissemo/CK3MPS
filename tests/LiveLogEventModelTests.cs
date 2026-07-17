using System;
using CK3MPS;

internal static class LiveLogEventModelTests
{
    private static int failures;

    private static void Main()
    {
        TestDuplicateInfoAggregates();
        TestProgressUpdatesOneLine();
        TestResultAndErrorAreVisible();
        TestDiagnosticsHiddenAndPersisted();
        TestUiBufferIsBounded();
        TestOperationIdsStaySeparate();
        TestCancellationIsLoggedAsResult();

        if (failures > 0)
            Environment.Exit(1);
    }

    private static void TestDuplicateInfoAggregates()
    {
        LiveLogEventModel model = new LiveLogEventModel(500, TimeSpan.FromMilliseconds(1), false);
        for (int i = 0; i < 100; i++)
        {
            LiveLogEvent evt = LiveLogEvent.FromLegacyLine("scan", "INFO scan unchanged");
            evt.TimestampUtc = DateTime.UtcNow.AddMilliseconds(i * 2);
            model.Accept(evt);
        }

        Assert(model.VisibleLines.Count == 1, "100 identical INFO events aggregate into one visible line");
        Assert(model.VisibleLines[0].RepeatCount == 100, "aggregate line keeps repeat count");
    }

    private static void TestProgressUpdatesOneLine()
    {
        LiveLogEventModel model = new LiveLogEventModel(500, TimeSpan.FromMilliseconds(1), false);
        for (int i = 0; i < 50; i++)
        {
            model.Accept(new LiveLogEvent
            {
                EventId = "scan-progress",
                OperationId = "scan",
                Type = LiveLogEventType.Progress,
                Severity = LiveLogSeverity.Info,
                TimestampUtc = DateTime.UtcNow.AddMilliseconds(i * 2),
                Text = "INFO scan progress " + i
            });
        }

        Assert(model.VisibleLines.Count == 1, "progress updates one UI line instead of adding many rows");
        Assert(model.VisibleLines[0].Text.EndsWith("49", StringComparison.Ordinal), "progress line keeps latest text");
    }

    private static void TestResultAndErrorAreVisible()
    {
        LiveLogEventModel model = new LiveLogEventModel(500, TimeSpan.FromSeconds(10), false);
        for (int i = 0; i < 20; i++)
            model.Accept(LiveLogEvent.FromLegacyLine("apply", "INFO unchanged refresh"));

        model.Accept(LiveLogEvent.FromLegacyLine("apply", "RESULT| NOT READY. Failed checks before final summary: 2"));
        model.Accept(LiveLogEvent.FromLegacyLine("apply", "ERROR rollback failure: restore manifest missing"));
        model.Accept(LiveLogEvent.FromLegacyLine("apply", "ERROR security refusal: unmanaged restore point"));
        model.Accept(LiveLogEvent.FromLegacyLine("apply", "FAIL failed postconditions: save hygiene"));

        Assert(ContainsVisible(model, "NOT READY"), "NOT READY result is always visible");
        Assert(ContainsVisible(model, "rollback failure"), "rollback failure is always visible");
        Assert(ContainsVisible(model, "security refusal"), "security refusal is always visible");
        Assert(ContainsVisible(model, "failed postconditions"), "failed postconditions are always visible");
    }

    private static void TestDiagnosticsHiddenAndPersisted()
    {
        LiveLogEventModel normal = new LiveLogEventModel(500, TimeSpan.FromSeconds(1), false);
        normal.Accept(LiveLogEvent.FromLegacyLine("scan", "DEBUG adapter probe elapsed=1ms"));
        Assert(normal.VisibleLines.Count == 0, "diagnostic is hidden in normal mode");
        Assert(normal.DiagnosticFileEvents.Count == 1, "diagnostic is kept for file persistence");

        LiveLogEventModel diagnostic = new LiveLogEventModel(500, TimeSpan.FromSeconds(1), true);
        diagnostic.Accept(LiveLogEvent.FromLegacyLine("scan", "DEBUG adapter probe elapsed=1ms"));
        Assert(diagnostic.VisibleLines.Count == 1, "diagnostic is visible in diagnostic mode");
    }

    private static void TestUiBufferIsBounded()
    {
        LiveLogEventModel model = new LiveLogEventModel(50, TimeSpan.FromMilliseconds(1), false);
        for (int i = 0; i < 80; i++)
        {
            model.Accept(new LiveLogEvent
            {
                EventId = "result-" + i,
                OperationId = "op",
                Type = LiveLogEventType.Result,
                Severity = LiveLogSeverity.Info,
                TimestampUtc = DateTime.UtcNow.AddMilliseconds(i),
                Text = "OK result " + i
            });
        }

        Assert(model.VisibleLines.Count == 50, "UI buffer is bounded");
        Assert(model.VisibleLines[0].Text == "OK result 30", "old UI rows are safely trimmed first");
    }

    private static void TestOperationIdsStaySeparate()
    {
        LiveLogEventModel model = new LiveLogEventModel(500, TimeSpan.FromMilliseconds(1), false);
        model.Accept(new LiveLogEvent { EventId = "progress", OperationId = "scan-a", Type = LiveLogEventType.Progress, Severity = LiveLogSeverity.Info, TimestampUtc = DateTime.UtcNow, Text = "INFO scan A" });
        model.Accept(new LiveLogEvent { EventId = "progress", OperationId = "scan-b", Type = LiveLogEventType.Progress, Severity = LiveLogSeverity.Info, TimestampUtc = DateTime.UtcNow, Text = "INFO scan B" });

        Assert(model.VisibleLines.Count == 2, "parallel operations keep separate progress rows");
        Assert(model.VisibleLines[0].OperationId != model.VisibleLines[1].OperationId, "operation ids do not mix");
    }

    private static void TestCancellationIsLoggedAsResult()
    {
        LiveLogEventModel model = new LiveLogEventModel(500, TimeSpan.FromSeconds(1), false);
        model.Accept(new LiveLogEvent
        {
            EventId = "cancelled",
            OperationId = "apply",
            Type = LiveLogEventType.Result,
            Severity = LiveLogSeverity.Warning,
            TimestampUtc = DateTime.UtcNow,
            Text = "RESULT| CANCELLED by user. No further changes applied."
        });

        Assert(ContainsVisible(model, "CANCELLED"), "operation cancellation is logged visibly");
    }

    private static bool ContainsVisible(LiveLogEventModel model, string text)
    {
        foreach (LiveLogRenderedLine line in model.VisibleLines)
            if ((line.DisplayText ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static void Assert(bool condition, string message)
    {
        if (condition)
        {
            Console.WriteLine("PASS " + message);
            return;
        }

        failures++;
        Console.Error.WriteLine("FAIL " + message);
    }
}
