## Highlights

- Path validation is now explicit: the app shows when both the CK3 game folder and the Documents settings/saves folder were found and are valid.
- Restore is substantially stronger: sorting, checkbox-based bulk restore/delete, simpler details, safer default-restore rules, and better rollback coverage.
- Live logging is clearer and persistent: color-coded output, horizontal readability improvements, and automatic log files in `LiveLogs`.
- GitHub release updates are now supported in-app with checksum verification before replacement.
- Portable mode is now real: when enabled, the full CK3MPS working state moves next to the executable in `CK3MPS_Data`.

## Included Files

```text
CK3MPS.exe
CK3MPS.exe.sha256
CK3MPS-v0.2.zip
CK3MPS-v0.2.zip.sha256
```

## Recommended Usage

1. Close CK3 and Paradox Launcher.
2. Start `CK3MPS.exe` as administrator.
3. Keep `Recommended` unless you explicitly want the broader `Maximum` preset.
4. Click `Stabilize CK3`.
5. Run `Check only`.
6. Review `Restore`, `Reports`, and the generated readiness output before multiplayer.

## Notes

- CK3, Steam, and Paradox Launcher may regenerate caches or launcher-owned files after launch. That is expected.
- `Portable mode` moves CK3MPS state, logs, restore history, and quarantine data into `CK3MPS_Data` next to the executable.
- The in-app updater expects the GitHub Release zip asset and its matching `.sha256` checksum file.
