# Release Notes Template

Use this layout for GitHub Releases.

## Highlights

- CK3 multiplayer stabilization checklist and presets.
- Windows, Steam, Paradox Launcher and CK3 profile checks.
- Adaptive network diagnostics for common Windows connection types.
- OOS evidence archive, MP parity notes and readiness report.
- Runnable Windows executable included.

## Included Files

```text
CK3MPS.exe
CK3MPS-v0.1-beta.zip
```

## Recommended Usage

1. Close CK3 and Paradox Launcher.
2. Run `CK3MPS.exe` as administrator.
3. Apply `Recommended` or `Maximum`.
4. Run `Check Only`.
5. Start CK3 and verify readiness again if needed.

## Known Limitations

- CK3, Steam and Paradox Launcher can regenerate cache folders after launch. That is expected.
- Package publishing is mainly for GitHub visibility and archival. Regular users should download the Release executable.
- Private repository/package access requires GitHub authentication.

## Checksums

Generate before publishing:

```powershell
Get-FileHash release\CK3MPS.exe -Algorithm SHA256
Get-FileHash ..\CK3MPS_exports\CK3MPS-v0.1-beta.zip -Algorithm SHA256
```
