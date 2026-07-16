# Source Guide

This folder contains the full WinForms application source for CK3MPS.

The code is intentionally split by feature area, but it still compiles into one large `partial MainForm` class plus a few static helper classes. That design keeps changes practical for a local Windows tool that touches CK3 files, Steam, Paradox Launcher, firewall, registry, reports, workflow/parity state, and restore data in one process. The tradeoff is broad shared state; use this file and `docs/CODEBASE.md` as the map before editing.

For a fuller architectural and recovery walkthrough, see [docs/CODEBASE.md](../docs/CODEBASE.md). For validation commands, see [docs/TESTING.md](../docs/TESTING.md).

## File Map

- `Start.cs`
  Application entry point. Requests administrator rights up front and starts the main form.
- `AppState.cs`
  Shared application state: version, controls, paths, caches, flags, in-memory scan/review snapshot types, workflow state, restore state, and startup defaults.
- `AppConfig.cs`
  Loads and saves app-level settings such as path overrides, portable mode, update-check preference, and log verbosity. Coordinates portable/non-portable state-root migration through the transaction helper.
- `MainWindow.cs`
  Builds the UI, lays out all tabs, defines the checklist items, and owns the high-level `Scan`, `Review`, and `Apply Settings` flow entry points.
- `StepChecklist.cs`
  Builds grouped checklist UI, expand/collapse behavior, scrolling, and per-step tooltip handling.
- `Scan.cs`
  Runs the read-only checklist pass. This is the source of truth for how the app inspects current CK3 / launcher / Windows state without changing user files.
- `Review.cs`
  Builds the detailed review plan from the latest same-session scan snapshot and renders the review dialog text shown before apply.
- `Launchers.cs`
  Steam and Paradox Launcher stabilization: launch options, cloud flag handling, launcher database rebuild, and no-mod profile generation.
- `GameSettings.cs`
  CK3 graphics and `pdx_settings.txt` stabilization, settings guard, expected-profile snapshots, and runtime-profile verification logic.
- `Cleanup.cs`
  CK3 user-state cleanup, cache cleanup, report archiving, suspicious file quarantine, and folder-cleanup reporting.
- `Network.cs`
  Windows network inspection and mutation: DNS flush, route analysis, firewall rule management, overlay/VPN checks, and online reachability checks.
- `Reports.cs`
  OOS analysis, save-hygiene notes, parity output, OOS evidence pack, adaptive Windows stability profile actions, and runtime hygiene snapshots.
- `Readiness.cs`
  Final readiness model and output generation. Also owns many detection helpers for CK3 install, Steam data, versions, saves, and stability checks.
- `Restore.cs`
  Restore manifest model, backup recording, restore UI data, default-restore rules, and legacy item restore behavior.
- `SystemRestore.cs`
  Windows restore-point integration: create, list, refresh, delete, and infrastructure checks/repair.
- `Updates.cs`
  GitHub release lookup and safe hand-off to the official releases page. Automatic in-place installation is intentionally disabled.
- `SaveAnalysis.cs`
  Bounded save parsing, rule checks, host-save scoring, and safe-copy preparation.
- `OosDeepAnalysis.cs`
  Deep OOS evidence parsing, contamination scoring, recovery recommendations, and incident history support.
- `Workflow.cs`
  Scenario UI, host/save/OOS workflow, parity comparison, and authenticated parity-room transport surface.
- `TransactionalOperations.cs`
  `TransactionalStateMigration`: crash-recoverable portable-mode migration with staging, two-root journals, checksum/hash validation, pre-commit rollback, post-commit cleanup, and startup recovery.
- `RestoreTransactions.cs`
  Batch restore transactions: app-owned transaction directory, reverse snapshots, same-parent directory replacement, registry/file/directory rollback, and `restore_manifest.tsv` restoration.
- `WorkflowAnalysisCoordinator.cs`
  Cancelable workflow refreshes, generation/scenario checks, immutable per-refresh analysis snapshots, and LAN-bound parity listener implementation.
- `StepCatalog.cs`
  Stable named IDs and validation for the 29 checklist actions plus the Recommended preset mapping.
- `Helpers.cs`
  Shared operational helpers used across the app: file writes, snapshots, presets, logs, path selection, live log persistence, and scan/apply session state.
- `Utilities.cs`
  Pure-ish utility helpers for path normalization, version comparison, preset constants, restore manifest serialization, app-owned ownership rules, and checksum parsing.
- `RuntimeModeUtilities.cs`
  Small helpers for portable/non-portable state root resolution and log filtering behavior.

## How The Code Is Organized

- UI construction lives mainly in `MainWindow.cs` and `StepChecklist.cs`.
- App-level state is centralized in `AppState.cs`.
- Read-only inspection is centered in `Scan.cs`.
- The before-apply plan is centered in `Review.cs`.
- Actual changes are spread across `Launchers.cs`, `GameSettings.cs`, `Cleanup.cs`, `Network.cs`, `Reports.cs`, `Readiness.cs`, `Restore.cs`, `SystemRestore.cs`, and workflow/transaction helpers.
- Cross-cutting support code lives in `Helpers.cs`, `Utilities.cs`, and `RuntimeModeUtilities.cs`.

## What To Edit For Common Tasks

- Change tab layout, button placement, labels, or control wiring:
  `MainWindow.cs`
- Change checklist grouping, collapse behavior, or tooltip behavior:
  `StepChecklist.cs`
- Change checklist IDs, label stability, or Recommended preset membership:
  `StepCatalog.cs`, then update tests and docs in the same change.
- Change what `Scan` checks or how read-only diagnostics are written:
  `Scan.cs`
- Change review-plan text, ordering, or review dialog rendering:
  `Review.cs`
- Change Steam or Paradox Launcher stabilization:
  `Launchers.cs`
- Change CK3 graphics, `pdx_settings.txt`, or settings guard behavior:
  `GameSettings.cs`
- Change cleanup/quarantine behavior:
  `Cleanup.cs`
- Change Windows network and firewall behavior:
  `Network.cs`
- Change OOS/report/parity/support package output:
  `Reports.cs`
- Change readiness verdict logic:
  `Readiness.cs`
- Change restore UI or restore entry parsing:
  `Restore.cs`
- Change batch restore transaction behavior:
  `RestoreTransactions.cs`
- Change portable-mode migration and startup recovery:
  `TransactionalOperations.cs` and `AppConfig.cs`
- Change workflow refresh cancellation, snapshot reuse, or LAN listener binding:
  `WorkflowAnalysisCoordinator.cs` and `Workflow.cs`
- Change Windows restore-point integration:
  `SystemRestore.cs`
- Change update checks or release-page navigation:
  `Updates.cs`
- Change presets, logging, path browsing, live log persistence, or scan/apply reuse:
  `Helpers.cs`
- Change pure utility rules or serialization helpers:
  `Utilities.cs`, `RuntimeModeUtilities.cs`

## Design Notes

- The code uses one `partial MainForm` so feature files can share state directly without a large dependency-injection layer. This is a conscious limitation, not a claim that the architecture is fully modular.
- `Scan` and `Apply Settings` are linked by a same-session snapshot so apply can reuse fresh state instead of repeating every expensive check.
- Restore recording is first-class: file, directory, registry, and snapshot changes are tracked before destructive or mutating actions.
- Batch restore transactions add reverse snapshots and manifest rollback around selected restore entries, but registry and filesystem recovery still depend on current Windows state and permissions.
- Portable mode is kept at the config/runtime layer so the same feature code can write to either the Documents state root or `CK3MPS_Data` next to the executable.
- Portable migration uses journaled staging and recovery, but cross-volume filesystem behavior and external edits can still require manual inspection.
- Workflow refresh output should come from one immutable analysis snapshot for a refresh. Do not recompute host/save/OOS/incident inputs independently for separate rendered lines.
- The parity room supports LAN binding while preserving security checks. Do not replace the selected loopback/private-LAN listener model with wildcard binding.
- `Updates.cs` only opens official GitHub release pages after user action. Do not document or reintroduce automatic unsigned updater execution without a separate security design.

## Editing Rules

- Keep generated files out of `source/`.
- Keep user-facing text compact and literal.
- Prefer changing the owning feature file instead of scattering related behavior across unrelated files.
- If a change affects recovery behavior, update `docs/CODEBASE.md`, `docs/TESTING.md`, and relevant harness coverage together.
- If a change affects release wording or maintainer flow, also check `docs/`, `README.md`, and the release-validation scripts.
- Before integration confidence, run `scripts\build.ps1` followed by `scripts\test-all.ps1` on Windows.
