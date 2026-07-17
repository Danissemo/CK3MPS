# Codebase Guide

This document describes the current CK3MPS architecture after the hardening work integrated through the `agent/fix-hardening-gaps` line. It is intentionally factual: it names the files, classes, transaction data, and recovery behavior that exist in the repository now.

## Purpose

CK3MPS is a Windows desktop utility that prepares a cleaner, more predictable Crusader Kings III multiplayer environment. It coordinates CK3 user files, Steam local/shared config, Paradox Launcher files, Windows networking and firewall, Windows restore points, restore/rollback metadata, OOS/readiness/support reports, workflow save analysis, and parity-room exchange in one WinForms process.

## Platform And Runtime Boundaries

- Windows-only desktop application.
- .NET Framework 4.8 project.
- Most feature code is still one large `partial MainForm`. This is practical for shared UI/runtime state, but it remains a real maintenance cost.
- Some behaviors depend on Windows state and rights. Registry rollback can fail if permissions, hives, or values changed outside CK3MPS.
- Directory replacement uses same-parent rename operations where possible; CK3MPS does not claim cross-volume atomicity.
- Automated parity and UI-race tests cannot reproduce every real router, firewall, VPN, multi-NIC, slow UI, or two-machine LAN condition. Manual LAN validation is still required before relying on LAN parity-room behavior for release confidence.

## Main Runtime Flow

### Startup

`Start.cs` is the application entry point. It checks administrator rights, relaunches with UAC when needed, and starts `MainForm`. Startup also gives `AppConfig.cs` a chance to load the current stabilizer root and to recover any interrupted portable/non-portable state-root migration through `TransactionalStateMigration.Recover`.

### Main form creation

`AppState.cs` holds shared fields and nested state records. `MainWindow.cs` builds tabs, controls, checklist rows, presets, and the high-level `Scan`, `Review`, and `Apply Settings` entry points.

### Read-only scan and review

`Scan.cs` runs the read-only checklist pass. It logs findings, runs readiness checks in read-only mode, and stores a same-session snapshot. `Review.cs` turns that snapshot plus the current UI selections into the before-apply plan.

### Apply and finalization

Mutation work is distributed across `Launchers.cs`, `GameSettings.cs`, `Cleanup.cs`, `Network.cs`, `Reports.cs`, `Readiness.cs`, `Restore.cs`, `SystemRestore.cs`, `Workflow.cs`, `WorkflowUnifiedFix.cs`, `TransactionalOperations.cs`, and `RestoreTransactions.cs`. Reports, live logs, restore manifests, and parity/support outputs are product artifacts, not incidental debug files.

## Source File Responsibilities

- `Start.cs` — process startup and administrator elevation.
- `AppState.cs` — shared UI/runtime state, version, path fields, scan/review snapshots, workflow fields, restore fields, and defaults.
- `AppConfig.cs` — `settings.ini`, path overrides, portable-mode flag, log verbosity, and portable/non-portable state-root moves.
- `MainWindow.cs` — visible UI layout and high-level flow orchestration.
- `StepChecklist.cs` — grouped checklist UI, expand/collapse behavior, virtual scrolling, and tooltips.
- `StepCatalog.cs` — named checklist IDs, 29 expected labels, and stable Recommended preset validation.
- `Scan.cs` — read-only inspection.
- `Review.cs` — before-apply plan text and review dialog.
- `Launchers.cs` — Steam and Paradox Launcher stabilization.
- `GameSettings.cs` — CK3 settings profiles, runtime profile checks, and settings guard.
- `Cleanup.cs` — cache/report cleanup and suspicious descriptor/binary quarantine.
- `Network.cs` — Windows networking checks and firewall/DNS mutations.
- `Reports.cs` — OOS, parity, save, support, evidence, and profile reporting.
- `Readiness.cs` — final readiness verdict and detection helpers.
- `Restore.cs` — restore manifest model, restore UI, restore-default rules, and legacy restore entry handling.
- `RestoreTransactions.cs` — batch restore transaction execution, reverse snapshots, manifest rollback, same-parent directory replacement, and rollback diagnostics.
- `SystemRestore.cs` — Windows restore-point integration.
- `Updates.cs` — GitHub release lookup and user-initiated navigation to the official release page. It does not perform automatic unsigned in-place updating.
- `Workflow.cs` — scenario UI, host/save/OOS workflow, parity comparison, and parity-room transport surface.
- `WorkflowUnifiedFix.cs` — single `Fix save + host` UI surface and coordinator: immutable preflight snapshot, supported save fix, exact host mutation, repeated postcondition checks, unified status, and `RESULT|` log line.
- `WorkflowAnalysisCoordinator.cs` — cancelable workflow refreshes, generation/scenario checks, immutable per-refresh analysis snapshots, and LAN-bound parity listener implementation.
- `TransactionalOperations.cs` — `TransactionalStateMigration`, portable-mode migration journaling, staging, commit, rollback, and startup recovery.
- `Helpers.cs` — shared operational helpers, preset application, log buffering, file writes, path selection, and scan/apply snapshot reuse.
- `Utilities.cs` — path normalization, version comparison, preset constants, restore manifest serialization, ownership rules, and checksum helpers.
- `RuntimeModeUtilities.cs` — portable/non-portable root helpers and log filtering behavior.

## Portable Mode And Transactional State Migration

Portable mode is a state-root switch, not a separate build:

- non-portable data lives under the Documents stabilizer root;
- portable data lives under `CK3MPS_Data` next to the executable.

`TransactionalStateMigration` in `TransactionalOperations.cs` performs recoverable migration between those roots. It uses:

- journal file `.ck3mps-state-migration`;
- staging directories named `.ck3mps-migration-stage-<transaction>`;
- `.backup` for replaced target data;
- version-2 journals with checksum/hash information while retaining compatibility with older recovery data;
- SHA-256 verification before exposing staged content;
- `settings.ini` commit last, so the `portableMode` flag moves at the transaction boundary.

The persisted phases are `Prepared`, `Copied`, `Committing`, `Committed`, and `Cleanup`.

Pre-commit failures in `Prepared`, `Copied`, or `Committing` are treated as uncommitted and CK3MPS attempts to roll back staged/created/replaced target data. If rollback cannot complete, journal data is left so startup recovery can retry or report the issue. Post-commit failures in `Committed` or `Cleanup` are treated as committed: startup recovery verifies committed targets, moves to cleanup when needed, completes cleanup idempotently, and then removes both journal copies.

Failure-injection tests use `CK3MPS_TEST_MIGRATION_FAULT`, including crash-style phases, to cover before-copy, after-copy, before-commit, after-commit, committed cleanup, corruption, journal compatibility, and recovery paths. These are tests of implemented recovery paths, not a promise that every possible OS/storage failure can be repaired automatically.

## Restore Transactions

Restore execution is batch-oriented through `ExecuteRestoreBatch` in `RestoreTransactions.cs`. A batch creates app-owned transaction data under the stabilizer root:

```text
<stabilizerRoot>\restore_transactions\<timestamp>_<guid>\
```

For each restore entry, CK3MPS prepares a `RestoreRollbackRecord`, captures reverse snapshots, stages backups, checks for reparse points, and then commits. File restores use verified temporary files. Directory replacement uses a sibling staged directory and same-parent rename/swap logic rather than recursive in-place overwrite.

The transaction also snapshots `restore_manifest.tsv` before applying changes. If any later item fails, CK3MPS walks already-prepared/applied records in reverse order and restores files, directories, created targets, registry values, and the manifest snapshot. If rollback succeeds, transaction data is removed. If rollback also reports errors, CK3MPS preserves the transaction directory and reports that recovery data path.

Important behavior:

- identical targets are no-ops;
- `moved_file` and `moved_directory` refuse to overwrite user data that appeared at the original path;
- targets, backups, staging trees, and cleanup paths reject reparse points;
- post-commit user changes are protected by confirmation snapshots where implemented;
- registry rollback is best-effort and depends on current Windows registry state and rights.

## Workflow Refresh, Unified Fix, And Parity Room

Workflow refreshes are coordinated by `WorkflowAnalysisCoordinator.cs`:

- `BeginWorkflowRefreshCancellation` cancels the previous refresh and records the owner generation/scenario;
- `CancelWorkflowScenarioRefresh` increments `workflowLoadGeneration` and cancels pending work;
- `WorkflowRefreshStillCurrent` requires the same cancellation token, generation, and scenario before rendering or applying results;
- `CaptureWorkflowAnalysisSnapshot` creates one immutable host/save/OOS/incident snapshot for the refresh;
- `CurrentWorkflowAnalysis` reuses the per-refresh snapshot while nested workflow code builds steps, verdict, summary, and recommendation text.

`WorkflowUnifiedFix.cs` replaces the visible separate `Fix host` and `Fix save` controls with one `Fix save + host` control after the workflow UI is built. The unified workflow captures a fresh preflight snapshot, shows found host/save findings, planned fixes, unsupported cases, and backup/restore data before mutation. It only mutates a save when the selected save is readable, version-compatible, existing, and has an exact supported finding; it only mutates host settings when host suitability has an exact finding. Save verification failure stops the workflow before host mutation. The final report uses `Succeeded`, `PartiallySucceeded`, `Failed`, or `Unsupported`, lists fixed and remaining problems, and writes one `RESULT| Fix save + host ...` live-log line.

The parity room keeps its security checks when LAN support is enabled. The transport deliberately binds loopback plus one selected private LAN IPv4 endpoint instead of `IPAddress.Any`, rejects routed clients outside the selected subnet before payload processing, and preserves room-code/session-secret authentication, encryption, signature/HMAC validation, replay/nonce checks, payload limits, peer/client limits, rate limiting, slow-client handling, and listener shutdown/port release behavior.

Automated parity tests cover loopback/LAN binding, wrong room code, wrong shared secret, nonce replay, tampered transport, payload size, peer/client limits, slow clients, and listener shutdown. A real two-machine LAN smoke test is still required because CI cannot model the user's router, firewall prompts, VPNs, or adapter priority.

## StepCatalog And Stable IDs

`StepCatalog` defines named constants for all 29 checklist actions and validates the exact label order plus the Recommended preset mapping. Do not reorder, remove, or repurpose a step ID casually. If a step meaning changes, update the catalog, tests, presets, review text, and documentation in the same change so old indexes do not silently point at new behavior.

## App-Owned Transaction Data

CK3MPS-owned transaction/recovery data includes:

- `.ck3mps-state-migration` journal copies in both state roots;
- `.ck3mps-migration-stage-*` staging directories;
- migration `.backup` data;
- `restore_transactions\*` transaction directories;
- `restore_manifest.tsv` and its transaction snapshot;
- generated test-only transaction data under `bin\tests` when scripts run locally.

User CK3 saves, launcher files, Steam configs, and registry values are not app-owned merely because CK3MPS can inspect or restore them.

## Updates And Releases

`Updates.cs` checks GitHub releases and opens the official release page after explicit user action. The current code does not run an automatic updater, does not download and execute replacement binaries, and should not be documented as a built-in automatic update installer.

Release governance is implemented in scripts and workflows rather than application runtime code. `check-version-consistency.ps1 -RequireReleaseTag` enforces exact release tag equality with `AppVersion` during release publishing. `package-release.ps1 -SkipBuild` packages the already-tested build output instead of rebuilding after the validation pipeline.

## Operational Recovery Guidance

After an interrupted portable migration:

1. Start CK3MPS again from the intended executable location.
2. Let startup recovery process the migration journal.
3. Do not manually delete `.ck3mps-state-migration`, `.ck3mps-migration-stage-*`, or `.backup` until recovery has either completed or the preserved data has been inspected.
4. If recovery reports a path/permission/hash issue, preserve both state roots before manual cleanup.

After an interrupted restore batch:

1. Do not immediately delete `restore_transactions`.
2. Reopen CK3MPS and inspect the Restore tab and logs.
3. If the error says recovery data was preserved, keep that transaction folder and compare the target files/registry values against the recorded restore entries before retrying.
4. Registry recovery may require elevated rights and may still depend on current Windows state.

## Reading Order

For architecture orientation, read:

1. `Start.cs`
2. `AppState.cs`
3. `MainWindow.cs`
4. `Scan.cs`
5. `Review.cs`
6. `TransactionalOperations.cs`
7. `Restore.cs`
8. `RestoreTransactions.cs`
9. `WorkflowAnalysisCoordinator.cs`
10. `Workflow.cs`
11. `WorkflowUnifiedFix.cs`
12. `Readiness.cs`
