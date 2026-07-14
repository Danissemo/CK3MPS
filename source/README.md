# Source Guide

The code is split by what each part does so the project is easy to inspect without knowing the whole application first.

- `Start.cs` - starts the app and asks Windows for administrator rights.
- `AppState.cs` - stores app paths, version, shared fields and startup setup.
- `MainWindow.cs` - builds the visible window, buttons, presets and progress bar.
- `CheckOnly.cs` - runs checks without changing settings.
- `Launchers.cs` - handles Steam and Paradox Launcher setup.
- `GameSettings.cs` - applies CK3 settings and protects them from rollback.
- `Cleanup.cs` - cleans CK3 documents, cache, reports, mods and suspicious files.
- `Network.cs` - checks DNS, firewall, adapters, VPN, PPPoE and mobile routes.
- `Reports.cs` - writes OOS reports, save notes, parity and risk summaries.
- `Readiness.cs` - creates the final ordered READY / NOT READY result.
- `Helpers.cs` - common file, process, registry, hash, log and path helpers.

The code still uses one WinForms `partial MainForm` class. The split is only for readability and low-risk editing.

## Editing Notes

- Keep UI changes in `MainWindow.cs`.
- Keep CK3 profile changes in `GameSettings.cs`.
- Keep Windows network changes in `Network.cs`.
- Keep report text compact and readable.
- Keep generated files out of `source/`.
