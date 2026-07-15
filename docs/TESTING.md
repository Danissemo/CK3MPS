# CK3MPS Smoke Test

## Safe Local Test Order

1. Start `release\CK3MPS.exe` as administrator.
2. Confirm `Paths` shows valid game and settings folders.
3. Run `Scan` on `Recommended` and confirm it completes without changing user files.
4. Open `Review` and verify only intended steps are listed.
5. Run `Apply Settings` and confirm a new restore run appears in `Restore`.
6. Inspect one `pdx_settings.txt` or `dlc_load.json` restore entry and verify `Before`, `Current now`, and `Diff`.
7. Use `Restore selected` on one reversible CK3 file entry and confirm the file returns to the recorded previous value.
8. Use `Restore default` on one CK3/launcher-owned file or registry entry and confirm the override is removed rather than restored to an old value.
9. Re-run `Scan` and confirm reports/readiness reflect the current state.
10. Launch CK3, wait for the main menu, exit, then run `Scan` again and confirm whether launcher/game recreated expected defaults or rewrote stability files.

## Steam-Specific Checks

1. Verify `localconfig.vdf` still contains only safe CK3 launch options after stabilize.
2. Verify `Restore default` on the Steam localconfig entry removes the CK3 `LaunchOptions` override instead of deleting the full Steam config file.
3. Verify `Restore default` on the Steam sharedconfig entry removes the CK3 `cloudenabled` override instead of deleting the full Steam config file.

## Release Gate

- `scripts\build.ps1`
- `scripts\test.ps1`
- `scripts\validate-release.ps1`
- `scripts\package-release.ps1`
- Launch smoke test completed once on a real machine
