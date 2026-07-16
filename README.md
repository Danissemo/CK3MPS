# CK3MPS

![CK3MPS social preview](assets/social-preview.png)

**Crusader Kings III Multiplayer Stabilizer for Windows.**

[![Release](https://img.shields.io/github/v/release/Danissemo/CK3MPS?include_prereleases&label=release)](https://github.com/Danissemo/CK3MPS/releases)
[![Build](https://github.com/Danissemo/CK3MPS/actions/workflows/build.yml/badge.svg)](https://github.com/Danissemo/CK3MPS/actions/workflows/build.yml)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4)](#requirements)
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4)](#build)
[![License](https://img.shields.io/github/license/Danissemo/CK3MPS)](LICENSE)

CK3MPS is a small Windows utility for preparing a cleaner, more predictable Crusader Kings III multiplayer environment. It focuses on reducing common OOS and connection-risk causes around CK3 user files, launcher state, Windows networking, Steam, Paradox Launcher, mods, cache, reports, and runtime readiness.

[Download latest release](https://github.com/Danissemo/CK3MPS/releases/latest) |
[View package](https://github.com/Danissemo/CK3MPS/pkgs/nuget/CK3MPS) |
[Read changelog](CHANGELOG.md)

## Screenshots

| Main window | Restore |
| --- | --- |
| ![Main window](assets/screenshots/main-window.png) | ![Restore](assets/screenshots/restore.png) |

| Scan | Generated report |
| --- | --- |
| ![Scan](assets/screenshots/scan.png) | ![Report](assets/screenshots/report.png) |

## What It Does

- Checks CK3 folders, running processes, saves, mods, reports, cache, launcher state, Steam state, Windows networking, firewall, adapter hints, VPN, PPPoE, DNS and route risks.
- Applies selected stability actions with visible progress and grouped checklist items.
- Offers practical presets: Recommended, Maximum, clean profile, network-focused and diagnostic-only flows.
- Archives OOS and crash evidence without filling the CK3 game folder with stabilizer logs.
- Writes compact reports, quarantine data, restore history and live logs to `Documents\Paradox Interactive\CK3MPS`, or to `CK3MPS_Data` next to the executable when **Portable mode** is enabled.
- Keeps the release folder simple: the downloadable app is `release\CK3MPS.exe`.

## Safety

CK3MPS is built for local Windows machines and should be run intentionally.

- Run it as administrator when applying stabilization.
- Run **Scan** when you only want diagnostics and review without applying changes. Scan mode is read-only and does not create or migrate stabilizer state.
- Keep your CK3 saves. Managed workflow saves are quarantined for recovery instead of being deleted.
- Close CK3 before applying game or launcher settings.
- Treat imported reports, saves, launcher state and restore manifests as untrusted input. Review before applying changes on shared or unusual setups.
- Review any warning in the final readiness report before starting multiplayer.

## Data and Network Boundaries

- CK3MPS writes reports, history, quarantine data and restore metadata only under `Documents\Paradox Interactive\CK3MPS`, or under `CK3MPS_Data` next to the executable in portable mode.
- Workflow save actions are limited to managed `.ck3` files under the CK3 user save roots. Selected saves are duplicated atomically and deleted saves are moved into quarantine history.
- Large save and OOS inputs are read with explicit size limits. When a text source exceeds the configured bound, CK3MPS truncates the read and marks the result.
- The parity room host listens on `127.0.0.1` only. Joining requires both the room code and the session secret, and payload sizes are bounded.
- Release checks can detect newer versions, but the app only opens the official GitHub release page. Automatic unsigned updater execution is intentionally disabled.

## Requirements

- Windows 10 or Windows 11
- Crusader Kings III installed through Steam or a compatible Paradox Launcher setup
- .NET Framework 4.8 runtime
- Administrator rights for Windows/network/launcher changes

## Recommended Use

1. Close CK3 and Paradox Launcher.
2. Run `CK3MPS.exe` as administrator.
3. Select **Recommended** for a balanced MP setup, or **Maximum** for the broadest stabilization pass.
4. Run **Scan**.
5. Open **Review** and inspect the planned actions.
6. Run **Apply Settings**.
7. Start CK3 and re-scan if you want to confirm settings persisted after launch.

## Presets

| Preset | Use case |
| --- | --- |
| Recommended | Balanced multiplayer stabilization without unnecessary aggressive cleanup. |
| Maximum | All available checks and stabilization actions. |
| Clean profile only | Focuses on CK3 user state, cache, reports and launcher-generated noise. |
| Network only | Focuses on Windows network diagnostics and adapter-specific stability hints. |
| Diagnostic only | Produces checks and reports without applying changes. |

## Repository Structure

```text
source/   readable C# source files split by feature area
assets/   icon, manifest, screenshots and repository artwork
docs/     release, package and project notes
scripts/  build, release packaging and GitHub package helpers
release/  runnable CK3MPS.exe and user release README
```

## Build

Requirements:

- Visual Studio Build Tools 2022 with MSBuild
- .NET Framework 4.8 targeting pack

```powershell
.\scripts\build.ps1 -UpdateReleaseArtifacts
```

The runnable executable is copied to:

```text
release\CK3MPS.exe
```

## Package

Create the local release zip:

```powershell
.\scripts\package-release.ps1
```

Create the GitHub NuGet package locally:

```powershell
.\scripts\package-github.ps1
```

The package is written outside the repository to `CK3MPS_exports`.

## Workflow

1. Run `Scan` to read the current CK3 / launcher / Windows state without changing files.
2. Open `Review` to inspect the exact actions and reports that would run now.
3. Run `Apply Settings` only after the same-session scan looks correct.
4. If you remove a managed workflow save by mistake, recover it from the stabilizer quarantine history instead of the original save folder.

## Release Maintenance

- Use [docs/RELEASE-CHECKLIST.md](docs/RELEASE-CHECKLIST.md) before every official release.
- Run `.\scripts\validate-release.ps1` before packaging or publishing.

## Links

- [Latest release](https://github.com/Danissemo/CK3MPS/releases/latest)
- [GitHub Packages](https://github.com/Danissemo/CK3MPS/pkgs/nuget/CK3MPS)
- [Security policy](SECURITY.md)
- [Support](SUPPORT.md)
