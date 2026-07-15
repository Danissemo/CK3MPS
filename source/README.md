# Source Guide

This folder contains the full WinForms application source for CK3MPS.

The code is intentionally split by feature area, but it still compiles into one `partial MainForm` class plus a few static helper classes. That design keeps changes low-risk for a local Windows tool that touches CK3 files, Steam, Paradox Launcher, firewall, registry, reports, and restore data in one process.

For a fuller architectural walkthrough, see [docs/CODEBASE.md](../docs/CODEBASE.md).

## File Map

- `Start.cs`
  Application entry point. Requests administrator rights up front and starts the main form.
- `AppState.cs`
  Shared application state: version, controls, paths, caches, flags, in-memory scan/review snapshot types, and startup defaults.
- `AppConfig.cs`
  Loads and saves app-level settings such as path overrides, portable mode, update-check preference, and log verbosity.
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
  Restore manifest model, backup recording, restore UI data, default-restore rules, and item-by-item rollback behavior.
- `SystemRestore.cs`
  Windows restore-point integration: create, list, refresh, delete, and infrastructure checks/repair.
- `Updates.cs`
  GitHub release lookup, checksum-aware update download, and external updater-script generation.
- `Helpers.cs`
  Shared operational helpers used across the app: file writes, snapshots, presets, logs, path selection, live log persistence, and scan/apply session state.
- `Utilities.cs`
  Pure-ish utility helpers for path normalization, version comparison, preset constants, restore manifest serialization, and checksum parsing.
- `RuntimeModeUtilities.cs`
  Small helpers for portable/non-portable state root resolution and log filtering behavior.

## How The Code Is Organized

- UI construction lives mainly in `MainWindow.cs` and `StepChecklist.cs`.
- App-level state is centralized in `AppState.cs`.
- Read-only inspection is centered in `Scan.cs`.
- The before-apply plan is centered in `Review.cs`.
- Actual changes are spread across `Launchers.cs`, `GameSettings.cs`, `Cleanup.cs`, `Network.cs`, `Reports.cs`, `Readiness.cs`, `Restore.cs`, and `SystemRestore.cs`.
- Cross-cutting support code lives in `Helpers.cs`, `Utilities.cs`, and `RuntimeModeUtilities.cs`.

## What To Edit For Common Tasks

- Change tab layout, button placement, labels, or control wiring:
  `MainWindow.cs`
- Change checklist grouping, collapse behavior, or tooltip behavior:
  `StepChecklist.cs`
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
- Change rollback behavior or restore UI details:
  `Restore.cs`
- Change Windows restore-point integration:
  `SystemRestore.cs`
- Change update checks or updater downloads:
  `Updates.cs`
- Change presets, logging, path browsing, live log persistence, or scan/apply reuse:
  `Helpers.cs`
- Change pure utility rules or serialization helpers:
  `Utilities.cs`, `RuntimeModeUtilities.cs`

## Design Notes

- The code uses one `partial MainForm` so feature files can share state directly without a large dependency-injection layer.
- `Scan` and `Apply Settings` are intentionally linked by a same-session snapshot so apply can reuse fresh state instead of repeating every expensive check.
- Restore recording is first-class: file, directory, registry, and snapshot changes are tracked before destructive or mutating actions.
- Reports are treated as support artifacts, not just logs. Multiple files in `Reports.cs` and `Readiness.cs` exist specifically to explain state after the app finishes.
- Portable mode is kept at the config/runtime layer so the same feature code can write to either the Documents state root or `CK3MPS_Data` next to the executable.

## Editing Rules

- Keep generated files out of `source/`.
- Keep user-facing text compact and literal.
- Prefer changing the owning feature file instead of scattering related behavior across unrelated files.
- If a change affects release wording or maintainer flow, also check `docs/`, `README.md`, and the release-validation scripts.
