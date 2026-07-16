## Highlights

- Portable mode migration is now journaled and recoverable after interrupted state-root moves.
- Restore operations now run as transactional batches with reverse snapshots and manifest rollback.
- Workflow analysis is cancelable and snapshot-consistent, with bounded save and OOS processing.
- Parity rooms support loopback or a selected private LAN IPv4 endpoint with authenticated encrypted messages, replay protection, payload limits, peer/client limits, and rate limiting.
- Checklist actions use stable StepCatalog identifiers across presets, tests, and documentation.
- CI and release publishing use deterministic test orchestration, strict mutation review, exact tag/version validation, and packaging from tested artifacts.

## Included Files

```text
CK3MPS.exe
CK3MPS.exe.sha256
CK3MPS-v0.31.zip
CK3MPS-v0.31.zip.sha256
```

## Recommended Usage

1. Close CK3 and Paradox Launcher.
2. Start CK3MPS as administrator.
3. Run Scan, review the plan, then Apply Settings.
4. Review Workflow, Restore, Reports, and readiness output before multiplayer.

## Notes

- Reopen CK3MPS after an interrupted migration or restore so recovery can inspect preserved journals and snapshots.
- Test LAN parity on the real Windows machines and network because firewall, router, VPN, and adapter configuration vary.
