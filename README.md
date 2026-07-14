# CK3MPS

CK3MPS is a Windows utility for CK3 multiplayer stabilization.

It checks and applies stability-oriented settings for:

- Windows networking and firewall
- Steam and Paradox Launcher
- CK3 settings, saves, cache, reports, mods and OOS evidence

## Structure

```text
source/   code and code notes
assets/   icon and manifest
scripts/  build and package scripts
release/  runnable exe and release notes
```

## Build

Requirements:

- Visual Studio Build Tools 2022 with MSBuild and .NET Framework 4.8 targeting pack
- .NET SDK 8 or newer

```powershell
.\scripts\build.ps1
```

The runnable exe is copied to:

```text
release\CK3MPS.exe
```

## Package

```powershell
.\scripts\package-release.ps1
```

The zip is written outside the repository to the sibling `CK3MPS_exports` folder.

