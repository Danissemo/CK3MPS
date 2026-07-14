# Contributing

CK3MPS is a Windows utility, so changes should stay practical, reversible and easy to verify on a real machine.

## Development Rules

- Keep source files in `source/`.
- Keep generated release files in `release/`.
- Keep documentation small and direct.
- Do not commit local CK3 logs, reports, saves, cache, or machine-specific data.
- Prefer focused changes over broad refactors.

## Build

```powershell
.\scripts\build.ps1
```

## Validate

Before opening a pull request:

```powershell
.\scripts\build.ps1
git status --short
```

For behavior changes, run the app on a Windows machine and include what was tested.
