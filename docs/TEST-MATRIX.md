# Test Matrix

Use this matrix when validating a release candidate on a real Windows machine.

## Runtime Modes

- Non-portable mode
- Portable mode

## Environment States

- Clean CK3 profile
- Existing used CK3 profile
- Custom game path override
- Custom Documents/settings path override

## Privilege Modes

- Run as administrator
- Run without administrator rights and confirm guarded actions are blocked or explained

## Workflow Coverage

- `Scan` only
- `Scan -> Review -> Apply Settings`
- `Scan -> Review` with no apply
- Repeat `Scan` / `Apply Settings` when the target state is already applied

## Restore Coverage

- Restore one CK3 file
- Restore one launcher-owned file
- Restore default on a supported override
- Delete selected restore entries

## Reports Coverage

- Refresh history
- Open reports
- Export support package
- Clear reports

## Advanced Coverage

- Check updates
- Portable mode toggle
- Delete selected restore points
- Delete other logs
- Delete quarantine files

## Post-Launch Coverage

- Launch CK3 once after apply
- Exit CK3
- Re-run `Scan`
- Confirm readiness and report outputs still look correct
