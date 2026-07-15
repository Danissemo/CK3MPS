# Maintenance

This guide is for repository and release maintenance work around CK3MPS.

## Common Tasks

## Bump Version

1. Update `source/AppState.cs` `AppVersion`.
2. Update `CK3MPS.csproj` `Version`.
3. Add or update `docs/release-notes-vX.Y.md`.
4. Add the matching `## vX.Y` section to `CHANGELOG.md`.
5. Run:

```powershell
.\scripts\check-version-consistency.ps1
.\scripts\validate-release.ps1
```

## Refresh Screenshots

1. Build a fresh `release\CK3MPS.exe`.
2. Re-capture the UI screenshots listed in `docs/SCREENSHOTS.md`.
3. Replace the files in `assets/screenshots`.
4. Run `.\scripts\check-repo-clean.ps1` to catch stale screenshot names.

## Build Official Release Assets

```powershell
.\scripts\build.ps1 -UpdateReleaseArtifacts
.\scripts\test.ps1
.\scripts\validate-release.ps1
.\scripts\package-release.ps1
.\scripts\package-github.ps1
```

## Publish GitHub Release

1. Confirm `main` is clean and pushed.
2. Confirm the release tag and title use the `CK3MPS vX.Y` format.
3. Upload:
   - `release\CK3MPS.exe`
   - `release\CK3MPS.exe.sha256`
   - `..\CK3MPS_exports\CK3MPS-vX.Y.zip`
   - `..\CK3MPS_exports\CK3MPS-vX.Y.zip.sha256`
4. Paste the body from `docs/release-notes-vX.Y.md`.
5. Open the published release page once and verify the assets and notes.

## Clean Repo Gate

Run before a release-oriented commit:

```powershell
.\scripts\check-repo-clean.ps1
git status --short
```
