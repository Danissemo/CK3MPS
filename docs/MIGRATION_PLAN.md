# Runtime Modernization Plan

## Goal

Move CK3MPS from a single .NET Framework 4.8 Windows build to a supported modern .NET Windows build without a rewrite, UI replacement, user-data format change, or loss of the working legacy release line.

## Baseline audit

| Area | Current state | Migration category |
|---|---|---|
| Application | One non-SDK `CK3MPS.csproj`, WinForms, `net48` | Conditional implementation |
| Package dependencies | Framework references only; no external NuGet packages in the application project | Transfer unchanged |
| UI | WinForms implemented across `partial MainForm` files | Keep on first modern target |
| Shared logic | Logging/event policy is UI-independent after task 08; other logic remains mixed with UI/runtime state | Extract incrementally |
| Windows APIs | Registry, UAC/elevation, firewall/network commands, restore points, process launch, Windows paths | Interface plus Windows implementation |
| Persistence | `settings.ini`, portable/non-portable roots, restore manifests, migration journals, reports | Compatibility contract; do not change implicitly |
| Build/release | Windows MSBuild, PowerShell scripts, GitHub Actions packaging | Dual runtime line during transition |
| Tests | PowerShell-driven characterization and failure-injection executables | Reuse for legacy; add modern runtime probes |

## Compatibility inventory

### Transfer without behavioral changes

- event classification and aggregation;
- deterministic models and value transformations that use only BCL APIs available to `netstandard2.0`;
- version comparison, checksum, parsing, and readiness rules after they are detached from `MainForm` and direct filesystem access.

### Require abstraction

- filesystem and directory mutation;
- process execution and shell navigation;
- clock and environment access;
- network transport and HTTP release lookup;
- configuration and state-root discovery.

### Require conditional Windows implementations

- WinForms UI and dispatcher behavior;
- Registry access;
- administrator/UAC relaunch;
- firewall and adapter integration;
- System Restore;
- Windows-specific installation and executable-path behavior.

### Replace only when proven incompatible

No working integration is replaced in this phase. A replacement requires characterization coverage, an upgrade/rollback story, and a demonstrated incompatibility on the modern target.

## Phases

### Phase 1 — audited dual-build baseline

- keep `CK3MPS.csproj` as the release baseline;
- add `CK3MPS.SharedCore.csproj` targeting `netstandard2.0`;
- add `CK3MPS.Modern.csproj` targeting `net8.0-windows` with WinForms retained;
- compile and run logging/event characterization tests on modern .NET;
- build legacy and modern targets independently in CI;
- do not publish the modern binary as the primary release artifact.

Exit criteria: both targets compile in CI and the first shared-core tests pass on both runtime lines.

### Phase 2 — expand Shared Core

Extract one bounded context at a time, following the task-08 service sequence. Each extraction must:

1. capture legacy behavior with characterization tests;
2. remove WinForms and direct Windows calls from the shared implementation;
3. introduce a narrow interface for external effects;
4. run identical fixtures against legacy and modern implementations;
5. leave user-visible behavior and persisted formats unchanged.

Preferred shared target is `netstandard2.0` while the legacy line remains supported. Use separate legacy/modern implementation projects where an API cannot be represented safely in the shared target.

### Phase 3 — modern Windows client validation

- compose extracted services in the modern WinForms client;
- keep Windows implementations behind interfaces;
- validate clean install and upgrade install on Windows 10 and Windows 11;
- compare readiness, workflow, parity, restore, and updater behavior against legacy using the same fixtures and test machines;
- produce a separately named/package-distinguished modern artifact.

### Phase 4 — release transition

- publish legacy and modern packages in parallel;
- record runtime/package identity in release metadata without changing user data formats;
- validate upgrade and rollback in both directions;
- switch the primary release only after the Windows release matrix passes and feature parity is accepted;
- remove legacy support only through a separate, explicit decision.

## Persisted-data compatibility contract

The following formats are frozen during runtime migration unless a separately reviewed versioned migration is added:

- `settings.ini` and `AppConfig` semantics;
- portable `CK3MPS_Data` and non-portable state roots;
- `.ck3mps-state-migration` journals, staging, and backup conventions;
- `restore_manifest.tsv` and restore transaction records;
- generated readiness/workflow/parity result semantics;
- release/update metadata consumed by `Updates.cs`.

Modern code must read all supported legacy data. Legacy rollback must remain able to read data written by the modern build. New optional fields must be ignored safely by the older line or be written only after the legacy line has corresponding compatibility support.

## Test matrix

| Check | Legacy `net48` | Modern .NET |
|---|---:|---:|
| Compile Windows client | Required | Required |
| Shared-core characterization tests | Required | Required |
| Configuration fixtures | Required | Required |
| Portable migration and journal recovery | Required | Required |
| Restore manifest/transaction fixtures | Required | Required |
| Readiness/workflow parity fixtures | Required | Required |
| Updater package discrimination | Required | Required |
| Clean install / upgrade / rollback | Windows matrix | Windows matrix |
| Registry, UAC, firewall, System Restore smoke | Windows 10/11 | Windows 10/11 |

## Risks and controls

- **Runtime behavioral differences:** compare outputs from identical fixtures and retain the legacy executable as oracle during transition.
- **Windows API differences:** keep calls behind interfaces and test on real Windows 10/11 hosts.
- **Serialization drift:** freeze formats and add byte/text fixture comparisons before changing serializers.
- **Packaging confusion:** use distinct artifact names and runtime metadata; never replace the legacy package silently.
- **Large-bang refactor:** migrate one bounded context per PR and prohibit unrelated features in migration PRs.
- **False parity from CI:** require manual installation, elevation, firewall, System Restore, and LAN smoke checks before release promotion.

## Rollback plan

1. Keep the latest verified legacy package and checksums available.
2. Do not migrate persisted data destructively; modern writes must remain legacy-readable.
3. On modern-client failure, close the modern build and reinstall/run the legacy package against the same state root.
4. Let existing journal/restore recovery complete before changing versions when a transaction is active.
5. Preserve logs, journals, restore transactions, and both state roots when rollback reports a compatibility failure.
6. Revert the modern release channel independently; do not revert or delete user data automatically.

## Current implementation status

This phase introduces the project boundaries and CI compile/test probes. Only the logging/event bounded context is in Shared Core initially. The existing .NET Framework project remains the release baseline and no user-data or UI behavior is changed.
