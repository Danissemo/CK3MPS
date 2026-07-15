# Architecture

CK3MPS is a WinForms desktop utility centered around one partial `MainForm` split across feature files.

## Main Flow

1. `Scan` reads CK3, launcher, Windows, restore, and report state without mutating user data.
2. `Review` turns the current selection plus the latest same-session scan into a concrete execution plan.
3. `Apply Settings` reuses that scan snapshot, applies only required changes, then writes restore/report outputs.

## Main Areas

- `Start.cs`
  Starts the application and requests elevation when needed.
- `AppState.cs`
  Holds shared fields, version information, state roots, and partial-form state.
- `MainWindow.cs`
  Owns tab layout, button wiring, and high-level flow entry points.
- `StepChecklist.cs`
  Builds grouped checklist UI, expand/collapse behavior, and tooltip wiring.
- `Scan.cs`
  Runs the read-only checklist pass and scan finalization flow.
- `Review.cs`
  Builds the detailed plan text and review dialog from the latest scan snapshot.
- `GameSettings.cs`, `Launchers.cs`, `Network.cs`, `Cleanup.cs`
  Apply or inspect concrete CK3 / launcher / Windows changes.
- `Restore.cs`
  Records reversible changes and powers restore/default-restore actions.
- `Reports.cs`, `Readiness.cs`
  Produce compact reports, OOS evidence, parity output, and final readiness state.
- `Helpers.cs`, `Utilities.cs`, `RuntimeModeUtilities.cs`
  Shared support code for files, hashes, logging, paths, versions, and runtime mode.
- `Updates.cs`
  Handles GitHub release checks and safe update download flow.

## Release Sources Of Truth

- App UI version: `source/AppState.cs`
- Package version: `CK3MPS.csproj`
- Release notes: `docs/release-notes-v0.3.md`
- Release process: `docs/RELEASE-CHECKLIST.md`
- Release validation: `scripts/validate-release.ps1`
