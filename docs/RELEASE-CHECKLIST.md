# Release Checklist

Use this checklist before every official CK3MPS release.

## Version

1. Update `source/AppState.cs` `AppVersion`.
2. Update `CK3MPS.csproj` `Version`.
3. Add the new release section to `CHANGELOG.md`.
4. Prepare or update `docs/release-notes-vX.Y.md`.

## Content

1. Check `README.md`, `release/README.md`, `docs/RELEASE.md`, `docs/TESTING.md`, `docs/RELEASE_TEST_MATRIX.md`, `SUPPORT.md`, and `SECURITY.md`.
2. Refresh screenshots in `assets/screenshots` if the UI changed.
3. Make sure historical files only keep historical wording.

## Validation

Run:

```powershell
.\scripts\build.ps1 -UpdateReleaseArtifacts
.\scripts\test-all.ps1
.\scripts\validate-release.ps1
.\scripts\package-release.ps1
```

## Windows Release Smoke Matrix

1. Start the `Release Smoke Matrix` workflow for the exact commit intended for release.
2. Download the generated `release-smoke-report-seed` artifact.
3. Complete every mandatory row from `docs/RELEASE_TEST_MATRIX.md` with `Pass`, `Fail`, `Blocked`, or `N/A` plus evidence and notes.
4. Validate the completed report before publishing:

```powershell
.\scripts\validate-release-smoke-report.ps1 -ReportPath .artifacts\release-smoke-report-vX.Y.md -ExpectedVersion vX.Y -ExpectedCommitSha <release-commit-sha>
```

Do not publish if the tested commit differs from the release commit, a mandatory row is empty, a `Blocked` row has no reason/evidence, or any security/restore/updater/release-integrity row fails.

## Release Assets

Verify these files exist and match the target version:

```text
release\CK3MPS.exe
release\CK3MPS.exe.sha256
..\CK3MPS_exports\CK3MPS-vX.Y.zip
..\CK3MPS_exports\CK3MPS-vX.Y.zip.sha256
.artifacts\release-smoke-report-vX.Y.md
```

## GitHub Release

1. Confirm `main` is clean and pushed.
2. Create or update the GitHub release title in the `CK3MPS vX.Y` format.
3. Upload:
   - `CK3MPS.exe`
   - `CK3MPS.exe.sha256`
   - `CK3MPS-vX.Y.zip`
   - `CK3MPS-vX.Y.zip.sha256`
   - completed release smoke report and evidence links
4. Paste notes from `docs/release-notes-vX.Y.md`.
5. Open the published release once and verify the assets, smoke report, evidence links, and title.
