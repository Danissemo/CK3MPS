# CI Main Protection

This repository treats `main` as releasable only when the complete required CI set is green for the exact commit.

## Required branch protection checks

Configure branch protection for `main` with these required status checks. Job names are intentionally stable:

```text
Windows build
Full deterministic test suite
Read-only scan harness
Restore transaction tests
Restore ownership/security tests
Workflow/readiness tests
Parity security tests
Version consistency
Release/package verification
Mutation allowlist/repository integrity checks
CI required checks complete
```

Require the branch to be up to date before merging. Do not require optional/manual workflows such as `Refresh brand assets`.

## CI trigger policy

`Build` runs on every pull request targeting `main`, every push to `main`, and manual dispatch. It has no path filters, so source, test, workflow, release, script, and documentation changes all receive a visible check set instead of silently producing commits with no checks.

Pull request runs use concurrency cancellation so obsolete PR runs can be stopped. Main runs are not canceled by newer pushes because each merged commit needs its own visible status history.

## Permission policy

The required CI workflow uses repository read permissions only. It does not receive write permissions and does not publish releases.

Write permission is limited to the `Publish verified GitHub release` job inside the `Release` workflow. Pull requests, including fork pull requests, only run the read-only `Build` workflow and do not receive release secrets or a write token.

## Release policy

Release publication is gated by the successful `Build` workflow for the exact `main` commit:

1. `Build` compiles `bin\CK3MPS.exe`.
2. Required tests and security checks run against that tested executable.
3. `Release/package verification` packages the already-tested executable with `scripts\package-release.ps1 -SkipBuild`.
4. The package job verifies generated SHA-256 checksum files against the exact artifacts.
5. `Release` runs only after a successful `Build` workflow on `main`, or by manual dispatch with an explicit successful Build run id and tested commit SHA.
6. `Release` checks out the tested commit, downloads the tested artifacts from that CI run, verifies that `AppVersion`, release notes, tag name, and tag target commit all match, then publishes those artifacts.
7. Existing release assets are never silently overwritten. If the release already contains any required asset name, the release gate fails.

Tags alone do not publish a release. A tag must point to a green, tested `main` commit before the release workflow will publish.

## Failure diagnostics

Required jobs use explicit timeouts and upload diagnostics with `if: always()` where logs or generated artifacts can exist. For red CI, inspect the artifact matching the failed job first:

- `build-diagnostics`
- `full-test-diagnostics`
- `read-only-scan-diagnostics`
- `restore-transaction-diagnostics`
- `restore-security-diagnostics`
- `workflow-readiness-diagnostics`
- `parity-security-diagnostics`
- `release-package-diagnostics`
- `release-gate-diagnostics`
