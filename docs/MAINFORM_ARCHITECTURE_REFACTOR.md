# MainForm architecture refactor

This document tracks the staged extraction of business logic from the partial `MainForm` while preserving .NET Framework 4.8 and existing UI behavior.

## Boundaries

`MainForm` remains responsible for UI binding, state display, user commands, cancellation lifetime and dispatching UI updates. Services must not reference WinForms controls or show `MessageBox` directly.

External dependencies will be introduced behind interfaces for filesystem, registry, process execution, network, clock, environment, Windows restore APIs and UI dispatching.

## Extraction order

1. Logging/EventService.
2. RestoreOwnershipService.
3. ReadinessService.
4. FixWorkflowService.
5. ParityClient.
6. UpdateService.
7. ScanService and OperationCoordinator.

Each bounded context is moved independently and must be covered by characterization tests before behavior is redirected from `MainForm`.

## Stage 1: Logging/EventService

The first stage introduces `ILoggingEventService`, an injected `IClock`, immutable request/result models and a UI-independent implementation around the existing `LiveLogEventModel` policy.

Current guarantees captured by `LoggingEventServiceTests`:

- warning and error classification remains stable;
- diagnostics can be hidden from the UI without being lost from support diagnostics;
- repeated progress is aggregated;
- aggregation state is isolated per operation;
- timestamps come from the clock dependency;
- the service has no WinForms, file or `MessageBox` dependency.

The next change in this stage will replace the formatting/aggregation responsibility inside `MainForm.Log(...)` with this service and leave only UI rendering and dispatcher-safe binding in the form.
