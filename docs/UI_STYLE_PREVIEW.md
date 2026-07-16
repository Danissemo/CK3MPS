# UI style preview

This branch contains a standalone, non-functional preview executable for visual approval.

## Design intent

The preview is not a redesigned layout. It mirrors the production CK3MPS interface:

- the same 980 x 760 default window;
- the same root title, subtitle, tabs and status line;
- the same control coordinates, dimensions, spacing and responsive layout formulas;
- the same Main two-column layout and grouped checklist;
- the same Paths, Reports, Restore and Advanced structures;
- the existing Workflow tab remains the visual reference.

Only the visual skin changes: page and surface colors, flat button styling, field borders, typography, primary-action emphasis and restrained danger styling.

## Isolation

`CK3MPS-UI-Preview.exe` is a separate project and executable. It:

- opens normally without command-line parameters;
- uses an `asInvoker` manifest and does not request UAC;
- does not compile or construct the production `MainForm`;
- uses sample data only;
- does not scan the PC or read/write CK3 files, saves, settings, registry, firewall, network, reports or quarantine data;
- leaves the production `CK3MPS.exe` and its existing interface unchanged.

## Build

```powershell
msbuild CK3MPS.UiPreview.csproj /m /p:Configuration=Release /p:Platform=AnyCPU
.\preview\CK3MPS-UI-Preview.exe
```

This branch is for visual approval only. No production UI implementation is included.