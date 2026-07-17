# CK3MPS Testing Guide

This guide describes the current validation flow for the hardened CK3MPS codebase.

## Requirements

Run repository tests on Windows with:

- Visual Studio Build Tools 2022 / MSBuild;
- .NET Framework 4.8 targeting pack;
- .NET Framework C# compiler under `Microsoft.NET\Framework*\v4.0.30319`;
- PowerShell or PowerShell Core;
- administrator rights for manual app smoke tests that apply Windows, registry, firewall, launcher, or restore-point changes.

CI uses `windows-latest`. Linux/macOS are not supported targets.

## Required Order

The integration and regression scripts now require a completed build before tests run. The expected local order is:

```powershell
.\scripts\build.ps1
.\scripts\test-all.ps1
```

`test-all.ps1` fails immediately if `bin\CK3MPS.exe` is missing. It also requires `scripts\test-required.ps1`, discovers the remaining `test-*.ps1` files, runs each in an isolated PowerShell process, fails on the first nonzero exit code, and writes per-script logs to `bin\test-logs`.

The orchestrator prioritizes:

1. `test-required.ps1`
2. portable migration transaction scripts, including `test-portable-migration-transactions.ps1`
3. restore transaction scripts, including `test-restore-transactions.ps1`
4. workflow/parity scripts, including `test-workflow-parity.ps1`
5. StepCatalog/catalog-named scripts if present
6. `test-transactional-hardening.ps1`
7. restore point ownership scripts, including `test-restore-point-ownership.ps1`
8. any remaining `test-*.ps1` scripts in stable name order

Use the orchestrator for release confidence. Run individual scripts only when narrowing a failure.

## Script Reference

### `scripts\build.ps1`

Builds the application and produces `bin\CK3MPS.exe`.

```powershell
.\scripts\build.ps1
```

Use `-UpdateReleaseArtifacts` when intentionally refreshing `release\CK3MPS.exe` and its checksum.

### `scripts\test-all.ps1`

Single required test orchestrator.

```powershell
.\scripts\build.ps1
.\scripts\test-all.ps1
```

It verifies the built executable, runs every tracked `test-*.ps1` runner in a separate PowerShell process, and writes diagnostics to `bin\test-logs`.

### `scripts\test-required.ps1`

Required integration wrapper. It verifies `bin\CK3MPS.exe`, `scripts\test.ps1`, and `tests\ReadOnlyScanHarness.cs`, creates a CI-safe generated harness copy under `bin\tests\required-suite`, and then runs the existing integration suite.

```powershell
.\scripts\build.ps1
.\scripts\test-required.ps1
```

### `scripts\test-portable-migration-transactions.ps1`

Compiles `tests\TransactionalMigrationTests.cs` with `source\TransactionalOperations.cs`, `Utilities.cs`, `RuntimeModeUtilities.cs`, and `StepCatalog.cs`, then runs the portable migration transaction tests.

```powershell
.\scripts\test-portable-migration-transactions.ps1
```

### `scripts\test-restore-transactions.ps1`

Builds CK3MPS, verifies `bin\CK3MPS.exe`, compiles `tests\RestoreTransactionHarness.cs`, and runs the restore transaction harness against the built executable.

```powershell
.\scripts\test-restore-transactions.ps1
```

### `scripts\test-restore-point-ownership.ps1`

Builds CK3MPS, verifies `bin\CK3MPS.exe`, compiles `tests\RestorePointOwnershipHarness.cs`, and runs the Delete Data / Windows restore-point ownership harness against the built executable.

```powershell
.\scripts\test-restore-point-ownership.ps1
```

### `scripts\test-workflow-parity.ps1`

Requires a built `bin\CK3MPS.exe`, compiles Workflow and Parity harnesses, and runs refresh-race/snapshot, parity security, slow-client, LAN, limit, and listener shutdown coverage.

```powershell
.\scripts\build.ps1
.\scripts\test-workflow-parity.ps1
```

### `scripts\test-transactional-hardening.ps1`

Compiles and runs the transactional migration hardening test executable. This overlaps with the portable migration transaction coverage and remains part of the full orchestrated suite.

```powershell
.\scripts\test-transactional-hardening.ps1
```

### Static, release, and packaging gates

```powershell
.\scripts\check-static-danger.ps1
.\scripts\check-static-danger-strict.ps1
.\scripts\check-version-consistency.ps1
.\scripts\validate-release.ps1
.\scripts\package-release.ps1
```

For release publishing, the workflow runs:

```powershell
.\scripts\check-version-consistency.ps1 -RequireReleaseTag
.\scripts\validate-release.ps1
.\scripts\package-release.ps1 -SkipBuild
```

`-RequireReleaseTag` requires the pushed tag name to exactly equal `AppVersion`. `-SkipBuild` packages the already-tested executable rather than rebuilding after tests.

## Coverage Added By Hardening

### Portable migration

Covered behavior includes:

- transaction journal creation in both state roots;
- `Prepared`, `Copied`, `Committing`, `Committed`, and `Cleanup` phases;
- staging and SHA-256 verification before commit;
- `settings.ini` committed last;
- rollback before commit;
- idempotent cleanup after commit;
- startup recovery from committed and uncommitted journals;
- version-1 journal recovery compatibility and version-2 checksum validation;
- injected failures such as before-copy, after-copy, before-commit, after-commit, and crash-style recovery points.

### Restore transactions

Covered behavior includes:

- successful file restore;
- successful directory replacement;
- identical-target no-op;
- staging failure;
- failure before rename/commit;
- post-commit failure with rollback;
- reverse rollback for already-applied file, directory, created-file, moved-file, moved-directory, registry, and manifest changes;
- `restore_manifest.tsv` snapshot restoration;
- confirmation-snapshot change detection;
- reparse-point rejection;
- user-data protection for moved paths;
- temporary transaction cleanup on success and preservation when rollback also fails.

### Windows restore point ownership

Covered behavior includes:

- app-owned restore points with marker, operation id, manifest row, schema version, identity fields, and digest;
- prefix-only legacy restore points staying read-only;
- manually created restore points with plausible CK3MPS text or marker staying read-only without a manifest row;
- missing, corrupt, tampered, and duplicated ownership manifest rows blocking deletion;
- description, creation time, and sequence changes after UI load blocking deletion;
- bulk deletion skipping unowned, missing, or changed restore points while keeping only currently verified app-owned items;
- Delete Data checkbox blocking for unowned restore points.

### Workflow and Parity

Covered behavior includes:

- cancellation of stale refreshes;
- generation and scenario checks before rendering;
- immutable per-refresh host/save/OOS/incident snapshot reuse;
- loopback and private-LAN listener binding without wildcard `IPAddress.Any`;
- rejection of non-loopback clients outside the selected LAN subnet;
- wrong room code and wrong shared secret failures;
- replay/nonce rejection;
- tampered transport rejection;
- payload-size limits;
- peer/client limits;
- slow-client handling;
- listener shutdown and port release.

### StepCatalog and mutation review

Covered behavior includes:

- stable 29-step catalog IDs;
- expected label validation;
- Recommended preset mapping validation;
- strict mutation review based on committed Git blob hashes rather than checkout line endings.

## Manual Smoke Test

Run this on a real Windows machine before a release candidate is considered ready:

1. Start `release\CK3MPS.exe` as administrator.
2. Confirm `Paths` shows valid game and settings folders.
3. Run `Scan` on `Recommended` and confirm it completes without changing user files.
4. Use `Export Scan Report`, choose a temporary destination, and confirm the report is written only after explicit action.
5. Open `Review` and verify only intended steps are listed.
6. Run `Apply Settings` and confirm a new restore run appears in `Restore`.
7. Inspect one `pdx_settings.txt` or `dlc_load.json` restore entry and verify `Before`, `Current now`, and `Diff`.
8. Use `Restore selected` on one reversible CK3 file entry and confirm the file returns to the recorded previous value.
9. Use `Restore default` on one CK3/launcher-owned file or registry entry and confirm the override is removed rather than restored to an old value.
10. Create a Windows restore point through CK3MPS, open Delete Data / restore points, and confirm only manifest-verified CK3MPS restore points are checkable/deletable.
11. Confirm a system/manual restore point, including one with a similar description, remains read-only and is skipped by bulk deletion.
12. Re-run `Scan` and confirm reports/readiness reflect the current state.
13. Launch CK3, wait for the main menu, exit, then run `Scan` again and confirm whether launcher/game recreated expected defaults or rewrote stability files.

## Manual LAN Parity Test

Automated tests cover the harnessable parity protocol and local binding behavior, but they do not replace a two-machine LAN check.

Before release confidence, verify on two physical Windows machines on the same private subnet:

1. Host a parity room on machine A.
2. Confirm the displayed host endpoint is a private LAN IPv4 address, not `0.0.0.0`.
3. Join from machine B using the displayed room code and session secret.
4. Confirm wrong code/secret attempts fail.
5. Confirm Windows Firewall prompts/rules are expected for the selected private IPv4 endpoint.
6. Repeat with VPN/virtual adapters disabled when diagnosing adapter-priority issues.

## After An Interrupted Migration Or Restore

### Portable migration interruption

1. Start CK3MPS again from the intended executable location.
2. Let startup recovery process `.ck3mps-state-migration`.
3. Preserve both state roots until recovery completes or the preserved recovery data is inspected.
4. Do not manually delete `.ck3mps-migration-stage-*` or `.backup` data before investigation.

### Restore interruption

1. Keep the `restore_transactions` directory if an error mentions preserved recovery data.
2. Inspect the Restore tab, app logs, and the relevant transaction folder.
3. Compare target files/directories/registry values with restore entries before retrying.
4. Remember that registry rollback depends on current Windows permissions and registry state.

### Restore point ownership interruption

1. Treat restore points without a valid `restore_point_ownership.tsv` row as read-only legacy/system points.
2. Do not manually edit `restore_point_ownership.tsv` or `restore_point_ownership.secret`; digest mismatches intentionally block deletion.
3. If ownership data is lost, use Windows System Restore UI/tools for manual operator decisions instead of CK3MPS Delete Data.

## Diagnosing Red CI

1. Open the failing workflow run and identify whether the failure is build, `test-all.ps1`, strict mutation review, version/release validation, or packaging.
2. For test failures, download or inspect the `test-script-diagnostics` artifact and the named `bin\test-logs\*.log` file.
3. If `test-all.ps1` fails before running tests, confirm `bin\CK3MPS.exe` was produced by the build step.
4. If strict mutation review fails, review every broad mutation allowlist file named by `check-static-danger-strict.ps1` and update the pinned blob only after deliberate security review.
5. If release validation fails, check `source\AppState.cs`, `CK3MPS.csproj`, release notes, release assets, and exact tag/AppVersion equality.
6. If parity tests fail intermittently, inspect listener shutdown/port release and slow-client logs first; CI cannot prove every real LAN condition.
7. If UI/integration tests fail, inspect generated harness logs under `bin\tests\required-suite` and remember that automatic UI race tests have limited coverage compared with manual WinForms use.
