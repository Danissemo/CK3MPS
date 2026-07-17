# CK3MPS live logging policy

## Goal

The Main tab Live Log is for user-facing operation progress and final outcomes. It must not be used as an unbounded telemetry stream. Detailed probes still have to be preserved in the live log file/support package, but the default UI should stay readable.

## Structured event types

Every log event is represented as a structured `LiveLogEvent` with:

- `EventId`: stable event identifier used for deduplication and aggregation.
- `Type`: one of `UserAction`, `Progress`, `Result`, `Warning`, `Error`, or `Diagnostic`.
- `OperationId`: the action or workflow instance that emitted the event.
- `Severity`: `Info`, `Warning`, or `Error`.
- `TimestampUtc`: event creation time.
- `Text`: user-facing line.
- `DiagnosticPayload`: optional detail retained for file diagnostics.

## Default Live Log visibility

Normal mode shows only:

- action start / user action;
- meaningful progress or state changes;
- warnings and errors;
- final results.

Diagnostic lines (`VERBOSE`, `DEBUG`, `TRACE`, `DIAGNOSTIC`) are persisted to the file model but hidden from the default UI. They are only shown when diagnostic/debug verbosity is selected.

## Never suppress

These events must remain visible regardless of dedupe/rate limiting:

- final result lines;
- final errors;
- rollback failure;
- security refusal;
- `NOT READY`;
- failed checks and failed postconditions.

## Anti-spam behavior

The `LiveLogEventModel` enforces:

- deduplication of identical messages by `OperationId + EventId`;
- aggregation of repeat counts instead of adding duplicate rows;
- rate limiting for frequent progress/status events;
- replacement of the current operation progress row instead of appending progress spam;
- a bounded UI buffer with oldest-row trimming;
- separate operation rows so parallel scans/workflows do not mix.

## WinForms safety

UI flushing should use `BeginInvoke` and must not hold log locks while appending to WinForms controls. File logging errors should never block the UI path. Form close/dispose paths should clear pending UI work without synchronous flush deadlocks.

## Background refresh/scans

Background refreshes and watcher scans should log repeated status probes as `Diagnostic` or rate-limited `Progress`, not as visible `INFO` lines. A refresh may show a state change or final result, but repeated unchanged telemetry belongs in diagnostics.
