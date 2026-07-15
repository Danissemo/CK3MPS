# Repo Conventions

## Source Of Truth

- App UI version: `source/AppState.cs`
- Package version: `CK3MPS.csproj`
- Release notes: `docs/release-notes-vX.Y.md`
- Release process: `docs/RELEASE-CHECKLIST.md`

## Commit Scope

- Commit repository, release, documentation, and source changes intentionally.
- Do not commit machine-specific CK3 data, reports, saves, caches, or temporary export folders.
- Do not commit local screenshots with ad-hoc or version-suffixed names.

## Naming

- Stable GitHub release title format: `CK3MPS vX.Y`
- Stable release asset format: `CK3MPS-vX.Y.zip`
- Current workflow wording: `Scan`, `Review`, `Apply Settings`

## Release Files

- Official release executable lives in `release\CK3MPS.exe`
- Local working builds live in `bin\`
- Exported packages live outside the repository in `CK3MPS_exports`
