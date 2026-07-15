# Changelog

## Unreleased

- Reworked the main workflow around `Scan`, `Review`, and `Apply Settings`, with session reuse so a fresh scan can feed apply without repeating the same heavy checks.
- Reduced unnecessary work by skipping already-applied settings, avoiding restore/history writes when nothing actually changed, and keeping life logs/report writes focused on real actions.
- Improved firewall, runtime verification, support package, and parity-manifest behavior so repeated runs do not keep rewriting unchanged state.
- Reworked `Main`, `Reports`, `Restore`, and `Advanced` layouts for clearer reading, better log/report sizing, grouped cleanup tools, and a more informative review/preview flow.
- Added faster hover-only help on checklist question marks, startup-collapsed checklist groups, restore bulk-selection, restore-point cleanup controls, report cleanup, and targeted cleanup actions for logs/quarantine.
- Improved CK3 graphics profile handling, including clearer in-app placement and balanced-profile coverage for additional graphics options.

## v0.2

- Added path validation indicators and manual game/settings folder selection with clearer status text.
- Added live log persistence to `LiveLogs` and improved color-coded log readability.
- Added GitHub release update detection plus checksum-verified in-app updater flow.
- Added restore tab sorting, checkbox-based bulk actions, simpler restore UX, and bulk delete.
- Added per-item restore details, safer default-restore rules, and non-destructive rollback coverage for CK3, launcher, and registry changes.
- Added portable mode that moves the full CK3MPS state root next to the executable in `CK3MPS_Data`.
- Added real three-level log verbosity behavior for `Quiet`, `Normal`, and `Verbose`.
- Improved release packaging, checksums, and GitHub-facing documentation/screenshots for the v0.2 release.

## v0.1-beta

- First beta release.
- Added CK3 multiplayer stabilization checklist.
- Added presets for Recommended, Maximum, clean profile, network, and diagnostics.
- Added CK3 settings stabilization and rollback guard.
- Added Steam and Paradox Launcher stabilization.
- Added adaptive network diagnostics for Ethernet, Wi-Fi, VPN, PPPoE, mobile routes, DNS and firewall.
- Added CK3 cleanup for cache, reports, user state, mods, saves, and launcher-generated files.
- Added OOS reports, MP parity, risk summary, and prevention protocol.
- Added minimal git-ready project structure with `source`, `assets`, `scripts`, and `release`.
- Added runnable `release/CK3MPS.exe`.
