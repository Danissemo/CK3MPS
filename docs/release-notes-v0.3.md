## Highlights

- Main workflow is clearer: `Scan` collects the current state first, `Review` shows the exact planned actions, and `Apply Settings` reuses that same session scan instead of repeating the whole pass.
- Repeated runs are cleaner: CK3MPS now skips settings that are already in the target state and avoids writing restore/history data when nothing actually changed.
- The interface is easier to read: the main checklist, graphics profile, live log, restore actions, reports, and advanced cleanup tools were reorganized for faster use.
- Cleanup and recovery controls are broader: bulk restore selection, report cleanup, targeted restore-point deletion, log cleanup, and quarantine cleanup are available directly in the app.
- Checklist help is faster and less intrusive: tooltips now react only on hover over the question-mark box and appear/disappear much more quickly.

## Included Files

```text
CK3MPS.exe
CK3MPS.exe.sha256
CK3MPS-v0.3.zip
CK3MPS-v0.3.zip.sha256
```

## Recommended Usage

1. Close CK3 and Paradox Launcher.
2. Start `CK3MPS.exe` as administrator.
3. Keep `Recommended` unless you explicitly need a broader preset.
4. Run `Scan`.
5. Open `Review`.
6. Run `Apply Settings`.
7. Check `Restore`, `Reports`, and readiness output before multiplayer.

## Notes

- `Apply Settings` is intentionally locked until a successful same-session `Scan` is available.
- Scan/apply behavior now tries to avoid rewriting unchanged state, but launcher/game-owned files can still be recreated by CK3, Steam, or the Paradox Launcher after launch.
- `Portable mode` still keeps app state in `CK3MPS_Data` next to the executable when enabled.
- Do not publish `bin` builds as release artifacts; only official release packaging should update `release`.
