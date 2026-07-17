# Safe automatic updates

CK3MPS checks the stable GitHub Releases channel in the background when startup checks are enabled. Prereleases and drafts are ignored by default. The Advanced page also provides **Check updates**.

## Update flow

1. The main application queries the allowlisted `Danissemo/CK3MPS` HTTPS Releases API.
2. It requires the exact assets `CK3MPS-<version>.zip`, its `.sha256`, and `.manifest.json`.
3. The package is downloaded into an app-owned staging directory under `%LOCALAPPDATA%\CK3MPS\updates`.
4. The downloaded package SHA-256 is checked before installation is offered.
5. On **Restart now**, CK3MPS copies the current executable into staging and starts that copy as a separate updater process. The main process exits before any installed file is replaced.
6. The updater revalidates the request, canonical paths, manifest, package hash, every file hash, and the Authenticode chain. The signer subject must exactly match `CN=Danissemo`.
7. Only manifest-declared application files are replaced. Paths that target state directories such as `data`, `logs`, `quarantine`, or `CK3MPS_Data` are rejected.
8. Existing files are preserved in a same-volume recovery directory before replacement.
9. The new executable is started in health-check mode. Recovery data remains until that process writes the expected health token.
10. After a successful health check, recovery files are cleaned and the normal application starts. A failed health check triggers rollback.

Choosing **Restart later** keeps the verified package for the current session. Press **Check updates** again to install it. Old abandoned staging directories are removed after seven days.

## Security boundaries

The updater rejects:

- non-HTTPS or non-allowlisted release URLs;
- draft and prerelease releases in the normal update flow;
- asset names that do not exactly match the selected version;
- malformed checksum metadata;
- package or file SHA-256 mismatches;
- unsupported or repository-mismatched manifests;
- unsigned executables, invalid Authenticode chains, or a non-allowlisted publisher;
- downgrade requests unless an explicit advanced downgrade request is introduced;
- absolute paths, traversal components, reparse-point staging roots, and files outside staging;
- manifest paths that overlap known user-state directories;
- installation when staging and rollback space is insufficient.

The package signature is verified again in the separate updater process. The browser download result alone is never trusted.

## Portable and non-portable data

The update package contains application files only. User configuration, reports, quarantine data, restore transactions, portable-mode state, and CK3 data are not release-package targets. Portable/non-portable state migration remains a separate transactional operation and is not performed by the updater.

## Recovery after interruption

Replacement uses per-file same-volume backups. If replacement or startup verification fails, the updater attempts rollback in reverse order. Recovery data is not deleted before a successful health check.

If Windows or the machine is interrupted during replacement:

1. Do not delete `.ck3mps-update-*` directories beside the installation directory.
2. Start the staged `CK3MPS.Updater.exe` with its existing `update-request.json` when available, or restore the files from the transaction's `recovery` directory.
3. Keep the `%LOCALAPPDATA%\CK3MPS\updates` staging directory until the prior executable is restored and starts normally.
4. Review Windows file locks, antivirus quarantine, disk space, and Authenticode status before retrying.

A cross-volume rename is not treated as atomic. New app files are first prepared below the installation parent so replacement and rollback stay on the installation volume. Registry and user-state migrations are outside the updater transaction.

## Release requirements

The release workflow requires repository secrets `CK3MPS_SIGNING_CERT_BASE64` and `CK3MPS_SIGNING_CERT_PASSWORD`. It signs `bin\CK3MPS.exe`, validates the signer and signed health-check mode, then publishes:

- the signed executable;
- the versioned ZIP package;
- SHA-256 metadata;
- the JSON package manifest with repository, version, publisher, package hash, and per-file hashes.

Release publication fails when signing, signature validation, tests, static mutation review, version/tag validation, packaging, or manifest generation fails.

## Test coverage

`scripts/test-safe-updater.ps1` is discovered by `scripts/test-all.ps1`. It checks health mode, checksum rejection, downgrade rejection, repository allowlisting, prerelease filtering, endpoint validation, deferred restart wiring, and presence of the signature/hash/rollback/path/free-space primitives. Signed-package installation, locked-file replacement, interruption timing, and rollback must also be exercised on Windows release candidates because CI cannot safely emulate every antivirus, filesystem, and UI race.
