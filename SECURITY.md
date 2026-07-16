# Security Policy

CK3MPS touches local game, launcher and Windows settings, so security reports are taken seriously.

## Threat Model

CK3MPS is a local Windows utility that may run with administrator rights. That means security issues are not limited to remote code execution. We also care about:

- Unsafe file operations outside the intended CK3 and stabilizer roots.
- Malicious or malformed save files, OOS reports, launcher files, restore manifests and imported text data.
- Overly broad network exposure or unauthenticated local tooling.
- Unexpected privileged command execution during update or maintenance flows.

Current guardrails in the project include bounded reads for large inputs, loopback-only parity hosting, atomic writes for local metadata, restore-manifest validation, quarantine-based save removal, and disabled automatic unsigned update installation.

## Supported Versions

| Version | Supported |
| --- | --- |
| v0.3 | Yes |
| v0.2 | No |
| v0.1 beta | No |

## Reporting

Open a private security advisory on GitHub if available, or create an issue without posting private files, tokens, save data or personal network details.

Useful details:

- CK3MPS version.
- Windows version.
- What action was selected.
- What changed unexpectedly.
- Minimal steps to reproduce.

Do not attach raw personal save files, launcher databases, OOS dumps, registry exports or screenshots that expose usernames, filesystem paths, secrets or network details unless maintainers explicitly ask for a sanitized sample.
