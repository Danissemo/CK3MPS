# Codebase Guide

This document explains how CK3MPS is structured, why the code is split the way it is, and how the main runtime flow works.

## Purpose

CK3MPS is a Windows desktop utility that prepares a cleaner, more predictable Crusader Kings III multiplayer environment.

The application has to coordinate several different domains in one run:

- CK3 user files in Documents
- Steam local/shared config
- Paradox Launcher files
- Windows networking and firewall
- Windows restore points
- Restore/rollback metadata
- OOS/readiness/support reports

Because these areas interact during one user action, the project keeps them in one process and one `partial MainForm`, but splits the source by feature ownership to keep editing practical.

## Why The Project Uses `partial MainForm`

The source is not split into a deep service/container architecture. Instead, most feature files are `partial MainForm`.

That choice trades abstract layering for a few pragmatic benefits:

- all tabs and actions can share the same state without plumbing many interfaces
- low-level Windows actions can be wired directly to UI and restore/report code
- feature edits stay local to a file even though the app is highly stateful
- refactors are less risky for a local admin tool with many side effects

The downside is that shared state is broad, which is why `AppState.cs` and `source/README.md` matter: they are the map to what exists and where to edit it.

## Top-Level Runtime Flow

## 1. Startup

`Start.cs` is the application entry point.

It:

- checks for administrator rights
- relaunches via UAC when needed
- starts `MainForm`

This up-front elevation is intentional because CK3MPS can touch firewall, registry, launcher files, and restore infrastructure. The tool wants one predictable privilege level instead of mixed behavior later.

## 2. Main form creation

`AppState.cs` defines shared fields and `MainWindow.cs` builds the visible UI.

During startup the app:

- creates controls and tabs
- loads app config and portable-mode state
- auto-detects CK3 / Steam / settings paths
- fills the checklist items
- wires `Scan`, `Review`, `Apply Settings`, reports, restore, and advanced actions

## 3. Scan

`Scan` is the read-only pass.

The UI button is wired in `MainWindow.cs`, while the actual item-by-item read-only logic lives in `Scan.cs`.

The scan:

- walks every checklist item
- writes live log output
- runs final readiness checks in read-only mode
- stores a same-session snapshot of the current selected state

That snapshot is important. It is what lets `Apply Settings` avoid repeating every expensive inspection step if the user already ran `Scan` in the current session with the same relevant inputs.

## 4. Review

`Review.cs` turns the latest scan state plus the current UI selections into a concrete plan.

It answers questions like:

- what core actions will run
- which selected items actually need a change
- which selected items are already in target state
- which items are report-only
- what files/outputs/restore safety apply

This layer exists because the tool is not a blind batch runner. It tries to be explicit about what will happen before writing anything.

## 5. Apply Settings

`MainWindow.cs` owns the high-level apply sequence.

The actual mutation work is spread across feature files:

- `Launchers.cs`
- `GameSettings.cs`
- `Cleanup.cs`
- `Network.cs`
- `Reports.cs`
- `Readiness.cs`
- `Restore.cs`
- `SystemRestore.cs`

Before mutating, the app reuses the latest same-session scan/review snapshot when possible. It also records restore information before destructive or mutating changes.

## 6. Finalization

After scan or apply, the app writes compact support artifacts:

- run history
- readiness output
- OOS summaries
- parity/support package files
- restore manifests and snapshots
- live log files

The project treats these outputs as part of the product, not just internal debugging noise.

## Shared State Model

`AppState.cs` is the shared state hub.

Important groups inside it:

- UI controls
- current discovered or overridden paths
- portable/non-portable state roots
- live log buffering state
- busy/progress flags
- scan/apply snapshot state
- restore UI state
- settings guard state

It also defines small nested data shapes such as:

- grouped checklist UI records
- pending log line buffers
- step planning snapshots
- session scan snapshot
- network route profile snapshot

These nested types exist because the app needs structured in-memory state, but the project intentionally avoids spreading many tiny standalone classes across the repo.

## File-By-File Responsibilities

## `Start.cs`

What:

- application entry point
- administrator elevation

Why:

- many actions require elevation, and mixed privilege state would make behavior less predictable

How:

- checks Windows principal role
- relaunches the executable with `runas`
- starts WinForms when elevation is available

## `AppState.cs`

What:

- long-lived state fields used by all partial form files

Why:

- feature files need shared access to controls, paths, logs, progress, review snapshots, and runtime flags

How:

- declares controls, strings, booleans, buffers, nested UI records, and constructor defaults

## `AppConfig.cs`

What:

- app configuration and path override persistence

Why:

- the tool needs to remember custom folders, portable mode, update preferences, and log verbosity between runs

How:

- reads/writes `settings.ini`
- handles legacy config/path override migration
- resolves non-portable vs portable config locations
- moves state when portable mode changes

## `MainWindow.cs`

What:

- UI construction, layout, and top-level run orchestration

Why:

- all tabs and action buttons originate here

How:

- builds `Main`, `Paths`, `Reports`, `Restore`, and `Advanced`
- fills checklist items
- starts `Scan`, `Review`, and `Apply Settings`
- handles deferred finalization after scan/apply

## `StepChecklist.cs`

What:

- checklist grouping and interaction behavior

Why:

- the app has many actions, so it needs grouped display, collapse state, and per-item help

How:

- builds group headers and rows
- manages expand/collapse state
- synchronizes group checkbox state with child item state
- manages tooltip behavior and virtual scrolling

## `Scan.cs`

What:

- read-only scan implementation

Why:

- users need a no-change diagnostic pass before applying changes

How:

- dispatches each checklist index to a read-only check
- logs findings
- runs readiness in read-only mode
- stores a fresh same-session scan snapshot

## `Review.cs`

What:

- detailed before-apply review plan generation and rendering

Why:

- apply should explain what changes are actually needed now, not only what is selected

How:

- builds review lines by tone/category
- separates core actions, changes, report-only actions, and already-satisfied items
- renders a rich review dialog
- computes per-step detailed preview text

## `Launchers.cs`

What:

- Steam and Paradox Launcher stabilization

Why:

- launcher state is a common source of multiplayer instability, mod noise, and override drift

How:

- backs up launcher configs
- normalizes Steam launch options
- disables risky launch/debug options
- disables Steam Cloud override when needed
- rebuilds launcher database by quarantining it
- enforces no-mod `dlc_load.json`

## `GameSettings.cs`

What:

- CK3 settings stabilization and runtime-profile verification

Why:

- CK3 graphics and settings drift can affect stability and reproducibility, and the app wants to detect rollback after launch

How:

- writes stable `pdx_settings.txt`
- applies graphics sub-profiles
- writes expected profile snapshots
- runs a timer-based settings guard
- checks runtime logs for renderer/profile drift
- writes stable game-rule profile output

## `Cleanup.cs`

What:

- CK3 user-state cleanup and suspicious-file quarantine

Why:

- stale reports, caches, descriptors, and suspicious loader files can pollute multiplayer troubleshooting

How:

- archives OOS/crash reports
- clears caches
- quarantines suspicious `.mod` and binary-like files
- clears selected player UI state
- writes cleanup markers and reports

## `Network.cs`

What:

- Windows network diagnostics and network mutation helpers

Why:

- CK3 multiplayer problems often mix app-level and route/DNS/firewall problems

How:

- builds route/adaptor profile snapshots
- checks VPN, PPPoE, mobile, IPv6-only, CGNAT, and DNS signals
- logs adaptive network plans
- flushes DNS
- ensures CK3 firewall rules
- checks overlays, background apps, services, and online reachability

## `Reports.cs`

What:

- support, OOS, save, parity, and Windows profile reporting

Why:

- the project wants compact outputs that explain what happened and what still looks risky

How:

- writes latest OOS summary
- writes OOS history timeline
- writes parity manifest
- writes OOS risk score report
- writes support/evidence package index
- writes save launch notes
- owns some Windows profile actions and snapshots used in readiness/support output

## `Readiness.cs`

What:

- final verdict generation plus many detection helpers

Why:

- all user-facing success/failure summaries need one ordered readiness model

How:

- runs final readiness checks
- writes stability, runtime, portable-transfer, pre-session, and verdict reports
- detects Steam roots, manifests, install path, active save data, versions, DLC/workshop fingerprints, etc.

This file is large because it is the convergence point for “are we actually ready?” logic.

## `Restore.cs`

What:

- rollback manifest, restore UI backing logic, and restore execution

Why:

- CK3MPS is intentionally reversible wherever possible

How:

- records file, directory, registry, and snapshot entries before mutation
- stores them in `restore_manifest.tsv`
- populates restore filters/sorting/details
- restores selected items or resets supported overrides to default behavior

## `SystemRestore.cs`

What:

- Windows restore-point integration

Why:

- some users want a system-level fallback outside CK3MPS’s own file/registry restore manifest

How:

- creates restore points
- checks restore-point infrastructure in read-only mode
- lists restore points via PowerShell
- deletes selected restore points
- attempts limited restore-infrastructure repair when needed

## `Updates.cs`

What:

- GitHub release checking and updater bootstrap

Why:

- the app can self-detect newer published releases and download them safely

How:

- queries the GitHub releases API
- selects the matching asset and checksum
- validates SHA256 before starting updater flow
- generates a PowerShell updater script

## `Helpers.cs`

What:

- large shared support layer

Why:

- many operational patterns repeat across the project: snapshots, file writes, path browsing, log formatting, presets, live log persistence, scan/apply reuse

How:

- contains utility methods that depend on `MainForm` state
- centralizes preset application and busy/progress handling
- centralizes live log buffering and file writes
- centralizes path selection and status indicators
- owns same-session scan snapshot invalidation/reuse helpers

## `Utilities.cs`

What:

- mostly pure static utility logic

Why:

- some rules should be testable without booting the whole WinForms app

How:

- path normalization and validation
- version comparison
- preset constants
- restore manifest serialization and ownership rules
- checksum parsing
- compact filename rules

## `RuntimeModeUtilities.cs`

What:

- tiny runtime-mode helpers

Why:

- portable-mode root selection and log suppression behavior are simple enough to stay in standalone statics

How:

- resolves state-root location
- decides which log lines should be suppressed for each verbosity mode

## Restore And Report Model

CK3MPS has two separate safety/explanation systems:

- restore data
- report data

Restore data exists so user changes can be rolled back.

Report data exists so the user can understand what the app saw, what it changed, and what still looks risky.

Those concerns intentionally live in different files because “can restore it” and “can explain it” are not the same problem.

## Portable Mode

Portable mode is not a separate app build.

It is a state-root switch:

- non-portable mode writes under the Documents stabilizer root
- portable mode writes under `CK3MPS_Data` next to the executable

That logic is coordinated through `AppConfig.cs`, `AppState.cs`, `RuntimeModeUtilities.cs`, and helper methods that always resolve the current stabilizer root before writing.

## Release And Repo-Maintenance Sources

The codebase itself also participates in release governance.

Important files:

- `source/AppState.cs`
  User-visible app version
- `CK3MPS.csproj`
  Package version
- `scripts/validate-release.ps1`
  Release metadata and file validation
- `scripts/check-version-consistency.ps1`
  Version consistency check
- `scripts/check-repo-clean.ps1`
  Repo hygiene check

If you change versioning, screenshots, release notes, or maintainer flow, you should check the scripts and docs under `scripts/` and `docs/` together.

## Where To Start Reading

If you are new to the code:

1. `Start.cs`
2. `AppState.cs`
3. `MainWindow.cs`
4. `Scan.cs`
5. `Review.cs`
6. `Restore.cs`
7. `Readiness.cs`

That order gives the clearest picture of startup, shared state, UI, read-only analysis, before-apply planning, rollback, and final verdict generation.
