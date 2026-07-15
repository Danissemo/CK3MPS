# Release Checklist

Use this checklist before every official CK3MPS release.

## Version

1. Update `source/AppState.cs` `AppVersion`.
2. Update `CK3MPS.csproj` `Version`.
3. Add the new release section to `CHANGELOG.md`.
4. Prepare or update `docs/release-notes-vX.Y.md`.

## Content

1. Check `README.md`, `release/README.md`, `docs/RELEASE.md`, `docs/TESTING.md`, `SUPPORT.md`, and `SECURITY.md`.
2. Refresh screenshots in `assets/screenshots` if the UI changed.
3. Make sure historical files only keep historical wording.

## Validation

Run:

```powershell
.\scripts\build.ps1 -UpdateReleaseArtifacts
.\scripts\test.ps1
.\scripts\validate-release.ps1
.\scripts\package-release.ps1
```

## Release Assets

Verify these files exist and match the target version:

```text
release\CK3MPS.exe
release\CK3MPS.exe.sha256
..\CK3MPS_exports\CK3MPS-vX.Y.zip
..\CK3MPS_exports\CK3MPS-vX.Y.zip.sha256
```

## GitHub Release

1. Confirm `main` is clean and pushed.
2. Create or update the GitHub release title in the `CK3MPS vX.Y` format.
3. Upload:
   - `CK3MPS.exe`
   - `CK3MPS.exe.sha256`
   - `CK3MPS-vX.Y.zip`
   - `CK3MPS-vX.Y.zip.sha256`
4. Paste notes from `docs/release-notes-vX.Y.md`.
5. Open the published release once and verify the assets and title.
