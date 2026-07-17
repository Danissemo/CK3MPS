# Windows Release Test Matrix

This matrix is the mandatory release gate for CK3MPS Windows builds. It turns the release smoke process into a reproducible artifact instead of an informal manual check.

Every official release must have a completed smoke report generated from `templates/release-smoke-report.md`, validated by `scripts/validate-release-smoke-report.ps1`, and uploaded or linked from the release evidence.

## Release gate rules

A release is **not ready** when any of these are true:

- a mandatory check is missing from the report;
- a mandatory check has an empty status;
- a mandatory check is `Blocked` without a reason and evidence link;
- any security, restore, transaction, updater, or release-integrity check is `Fail`;
- the tested commit SHA differs from the release commit SHA;
- the final decision is not `Ready`.

Use `scripts/new-release-smoke-report.ps1` to create a prefilled report and `scripts/validate-release-smoke-report.ps1` to enforce the gate.

## Status values

| Status | Meaning |
| --- | --- |
| `Pass` | Scenario was completed and evidence is linked. |
| `Fail` | Scenario failed. Mandatory security/restore/updater/release-integrity failures block release. |
| `Blocked` | Scenario could not be completed. Must include a concrete reason and evidence or tracking link. |
| `N/A` | Scenario does not apply to this release. Must include the reason. |

## Evidence requirements

Each report must include:

- release version;
- tested commit SHA;
- release commit SHA;
- report date;
- tester/environment;
- OS/build/VM or machine notes;
- scenario status, evidence, and notes;
- known limitations;
- final release decision.

Automated evidence should come from GitHub Actions artifacts, Windows runner logs, disposable VM logs, screenshots, generated reports, or deterministic script output. Manual evidence should include screenshots, videos, logs, machine notes, or links to issue/PR comments.

## Automated smoke coverage

These checks are expected to run on GitHub Actions Windows runners or disposable Windows VMs where possible.

| ID | Area | Scenario | Harness / evidence |
| --- | --- | --- | --- |
| AUTO-BUILD-01 | Release integrity | Build produces `bin\\CK3MPS.exe` and packaged release assets | `.github/workflows/build.yml`, `scripts/build.ps1`, package artifacts |
| AUTO-TEST-01 | Release integrity | Required tests and all `test-*.ps1` scripts pass in isolated processes | `scripts/test-all.ps1`, `bin/test-logs` artifact |
| AUTO-STATIC-01 | Security | Strict mutation review passes | `scripts/check-static-danger-strict.ps1` |
| AUTO-META-01 | Release integrity | Version, release notes, release docs, and package names are aligned | `scripts/validate-release.ps1` |
| AUTO-MATRIX-01 | Release matrix | Smoke report can be generated for the exact release version and commit | `scripts/new-release-smoke-report.ps1` artifact |
| AUTO-MATRIX-02 | Release matrix | Smoke report schema rejects missing mandatory fields | `scripts/test-release-smoke-report.ps1` |
| AUTO-MATRIX-03 | Release matrix | Smoke report schema rejects mismatched tested/release commits | `scripts/test-release-smoke-report.ps1` |
| AUTO-MATRIX-04 | Release matrix | Smoke report schema rejects failed mandatory security/restore/updater checks | `scripts/test-release-smoke-report.ps1` |
| AUTO-MATRIX-05 | Release matrix | Re-running report generation writes a new report path and does not mix evidence | `scripts/test-release-smoke-report.ps1` |
| AUTO-PORTABLE-01 | Install/run | Portable mode fixtures and interrupted migration recovery pass | `scripts/test-all.ps1` priority bucket |
| AUTO-RESTORE-01 | Restore/transactions | Restore transaction success and rollback fixtures pass | `scripts/test-all.ps1` priority bucket |
| AUTO-WORKFLOW-01 | Workflow/parity | Workflow and parity fixtures pass where deterministic | `scripts/test-all.ps1` priority bucket |

## Mandatory manual smoke checklist

The following matrix must be filled before each release. A scenario may be marked `N/A` only with a concrete reason.

### Operating systems and profiles

| ID | Scenario | Steps | Expected result | Evidence |
| --- | --- | --- | --- | --- |
| MAN-OS-01 | Windows 10 x64 clean user profile | Install or copy the release into a clean Windows 10 x64 user profile, run scan, open review, close app. | App starts, scans, writes reports only under the expected CK3MPS data root, no crash. | Screenshot + live log/report link. |
| MAN-OS-02 | Windows 11 x64 clean user profile | Repeat MAN-OS-01 on Windows 11 x64. | Same as MAN-OS-01. | Screenshot + live log/report link. |
| MAN-OS-03 | Existing user profile | Run on a profile with existing CK3, launcher, saves, reports, and CK3MPS state. | Existing user data is detected without destructive changes during scan. | Before/after notes + report. |

### Installation and launch

| ID | Scenario | Steps | Expected result | Evidence |
| --- | --- | --- | --- | --- |
| MAN-INSTALL-01 | Clean install | Run the current release on a machine without prior CK3MPS state. | Data roots are created only when needed and reports are deterministic. | Screenshot/report. |
| MAN-INSTALL-02 | Upgrade from previous stable release | Run previous stable, create state/report, then replace with current release and run scan/apply safe flow. | Upgrade preserves compatible state and reports clear migration/recovery information. | Previous/current logs. |
| MAN-INSTALL-03 | Portable mode | Enable or launch portable mode from writable app folder. | State is rooted under `CK3MPS_Data` next to executable. | Folder screenshot + report. |
| MAN-INSTALL-04 | Non-portable mode | Launch normal release from a writable user location. | State is rooted under Documents CK3MPS path. | Folder screenshot + report. |
| MAN-INSTALL-05 | Launch without admin | Start without elevation and run scan. | Scan works read-only; actions requiring elevation are blocked or clearly marked. | Screenshot/report. |
| MAN-INSTALL-06 | UAC elevation accepted | Start action requiring elevation and accept UAC. | Elevated operation continues, final verdict is correct. | Screenshot/log. |
| MAN-INSTALL-07 | UAC elevation denied | Start action requiring elevation and deny UAC. | Operation aborts safely with no partial untracked mutation. | Screenshot/log. |
| MAN-INSTALL-08 | Read-only or restricted folder | Launch from read-only/restricted folder. | App reports write limitation and does not crash or write outside expected boundaries. | Screenshot/log. |
| MAN-INSTALL-09 | Long path and non-ASCII username/path | Run under long/non-ASCII path or username. | Paths are displayed and handled correctly. | Screenshot/report. |

### Game environment

| ID | Scenario | Steps | Expected result | Evidence |
| --- | --- | --- | --- | --- |
| MAN-GAME-01 | Steam not installed | Run scan on machine without Steam. | Missing Steam is detected as a clear state, not a crash. | Report. |
| MAN-GAME-02 | Steam installed and closed | Run scan with Steam installed but closed. | Steam state is detected correctly. | Report. |
| MAN-GAME-03 | Steam running | Run scan with Steam running. | Steam state is detected correctly. | Report. |
| MAN-GAME-04 | CK3 not running | Run scan with CK3 closed. | CK3 closed state is detected correctly. | Report. |
| MAN-GAME-05 | CK3 running | Run scan with CK3 running. | Mutating actions are prevented or warned; final verdict reflects risk. | Screenshot/report. |
| MAN-GAME-06 | Paradox Launcher absent/damaged/running | Test absent, damaged, or running launcher state where available. | Launcher state is detected without unhandled exception. | Screenshot/report. |
| MAN-GAME-07 | Missing user folders | Temporarily move or use a profile without CK3 user folders. | Missing folders are created only when intended and reported clearly. | Before/after notes. |
| MAN-GAME-08 | Existing real user data | Run scan against real saves/mods/reports. | Scan remains read-only; apply only touches explicitly managed data. | Report + notes. |

### Network

| ID | Scenario | Steps | Expected result | Evidence |
| --- | --- | --- | --- | --- |
| MAN-NET-01 | One adapter | Run scan with one active adapter. | Adapter state is reported correctly. | Report. |
| MAN-NET-02 | Multiple adapters | Enable multiple adapters and run scan. | Adapter selection/risk hints are stable and non-spammy. | Report/screenshot. |
| MAN-NET-03 | VPN enabled | Enable VPN and run scan. | VPN risk is detected and explained. | Report. |
| MAN-NET-04 | Firewall allow | Trigger parity/network flow and allow firewall prompt. | App proceeds and records expected endpoint behavior. | Screenshot/log. |
| MAN-NET-05 | Firewall deny | Trigger parity/network flow and deny firewall prompt. | App fails safely with actionable message. | Screenshot/log. |
| MAN-NET-06 | DNS/network mutation failure | Inject or simulate failed DNS/network mutation. | Failure is reported and rollback/safety state is preserved. | Log/report. |
| MAN-NET-07 | Offline mode | Disconnect network and run scan. | Offline state is reported; no hang. | Screenshot/report. |
| MAN-NET-08 | Slow/unstable connection | Use throttled/unstable connection and run network-related flow. | UI remains responsive and final verdict is correct. | Screenshot/log. |
| MAN-NET-09 | Online Parity Room across two networks | Host and join from two different networks, not same LAN. | Room connects only with correct code/secret and handles failure clearly. | Two-machine screenshots/logs. |

### Restore and transactions

| ID | Scenario | Steps | Expected result | Evidence |
| --- | --- | --- | --- | --- |
| MAN-RESTORE-01 | Successful restore | Create app-owned state and restore it. | Restore succeeds and report/history are updated. | Log/report. |
| MAN-RESTORE-02 | Failure before commit | Inject failure before commit. | No untracked mutation; final verdict is not ready/fail as expected. | Fault log. |
| MAN-RESTORE-03 | Failure after partial commit | Inject failure after partial commit. | Rollback or recovery path is available and clearly reported. | Fault log. |
| MAN-RESTORE-04 | Rollback success | Force rollback after injected failure. | Rollback returns state to safe baseline. | Before/after notes. |
| MAN-RESTORE-05 | Rollback failure | Inject rollback failure. | Failure is visible; recovery evidence remains. | Fault log. |
| MAN-RESTORE-06 | App-owned restore point | Delete/restore only app-owned restore point. | App-owned marker is enforced. | Screenshot/log. |
| MAN-RESTORE-07 | Foreign restore point | Attempt foreign restore point operation. | Foreign restore point cannot be deleted/restored by CK3MPS destructive path. | Screenshot/log. |
| MAN-RESTORE-08 | Damaged manifest | Corrupt manifest and reopen app. | Manifest is treated as untrusted and blocked/recovered safely. | Log. |
| MAN-RESTORE-09 | Interrupted portable migration | Interrupt migration and reopen app. | Startup recovery handles journal/snapshot safely. | Log/report. |

### Updater and release integrity

| ID | Scenario | Steps | Expected result | Evidence |
| --- | --- | --- | --- | --- |
| MAN-UPDATER-01 | No updates | Run release check when current version is latest. | App reports no update or opens only official release page as designed. | Screenshot. |
| MAN-UPDATER-02 | Successful update path | Verify documented update/upgrade flow from previous stable to current release. | User can update without unsigned automatic execution. | Notes/screenshots. |
| MAN-UPDATER-03 | Checksum mismatch | Simulate mismatched checksum in package/evidence. | Release validation fails or user-facing check blocks use. | Log. |
| MAN-UPDATER-04 | Invalid signature | Simulate invalid signature/signature metadata where applicable. | Validation blocks release or reports unsupported signature state. | Log. |
| MAN-UPDATER-05 | Blocked executable | Mark executable as blocked/quarantined by Windows/AV where possible. | App or docs produce clear recovery guidance; no silent success. | Screenshot. |
| MAN-UPDATER-06 | Failed health check | Simulate health check failure after upgrade. | Upgrade is not considered ready. | Log/report. |
| MAN-UPDATER-07 | Rollback updater | Verify documented rollback from current to previous stable. | Rollback path is documented and does not mix state roots. | Notes/logs. |
| MAN-UPDATER-08 | Portable and non-portable upgrade | Upgrade both modes. | Both modes keep their own state roots and evidence. | Logs/reports. |

### UX and stability

| ID | Scenario | Steps | Expected result | Evidence |
| --- | --- | --- | --- | --- |
| MAN-UX-01 | Long scan | Run scan on profile with many saves/mods/reports. | UI remains responsive; progress/log is bounded. | Screenshot/log. |
| MAN-UX-02 | Cancel | Cancel a long operation. | Cancellation is safe and final verdict is correct. | Screenshot/log. |
| MAN-UX-03 | Re-run | Run scan/apply flow twice. | Second run is stable and does not duplicate evidence unexpectedly. | Logs. |
| MAN-UX-04 | Close window during operation | Close window during long operation. | App exits or blocks exit safely and recovery evidence remains. | Notes/logs. |
| MAN-UX-05 | No Live Log spam | Leave Main tab open during scan/workflow. | Live Log does not spam repeated identical info lines. | Screenshot/log. |
| MAN-UX-06 | Correct final verdict | Force pass, warning, and fail cases. | Final verdict matches actual failed checks and never says ready on failed mandatory checks. | Reports. |
| MAN-UX-07 | High DPI | Run under high-DPI display. | UI remains usable and readable. | Screenshot. |
| MAN-UX-08 | Display scaling | Run under 125%, 150%, and 200% scaling where possible. | UI remains usable and readable. | Screenshots. |
| MAN-UX-09 | Minimum window size | Resize to minimum size. | Content remains accessible without overlap. | Screenshot. |
| MAN-UX-10 | Slow machine | Run in a constrained VM or slow filesystem. | Operations remain bounded and messages are clear. | Log/screenshot. |

## Release workflow

1. Build and test the commit intended for release.
2. Generate the prefilled report:

   ```powershell
   .\scripts\new-release-smoke-report.ps1 -Version vX.Y -CommitSha <tested-sha> -ReleaseCommitSha <release-sha> -EnvironmentName "Windows 11 VM + manual matrix" -OutputPath .artifacts\release-smoke-report-vX.Y.md
   ```

3. Fill all mandatory manual rows with `Pass`, `Fail`, `Blocked`, or `N/A` plus evidence and notes.
4. Validate the completed report:

   ```powershell
   .\scripts\validate-release-smoke-report.ps1 -ReportPath .artifacts\release-smoke-report-vX.Y.md -ExpectedVersion vX.Y -ExpectedCommitSha <release-sha>
   ```

5. Upload the report and linked evidence as release artifacts.
6. Do not publish if validation fails.
