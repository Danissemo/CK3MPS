# CK3MPS Release Smoke Report

<!--
Generate a fresh copy with scripts/new-release-smoke-report.ps1.
Do not publish a release until scripts/validate-release-smoke-report.ps1 passes on the completed report.
-->

## Metadata

| Field | Value |
| --- | --- |
| Version | {{VERSION}} |
| Tested commit SHA | {{TESTED_COMMIT_SHA}} |
| Release commit SHA | {{RELEASE_COMMIT_SHA}} |
| Report date | {{REPORT_DATE}} |
| Environment | {{ENVIRONMENT}} |
| Tester | {{TESTER}} |
| Evidence root | {{EVIDENCE_ROOT}} |

## Automated checks

| ID | Mandatory | Category | Status | Evidence | Notes |
| --- | --- | --- | --- | --- | --- |
{{AUTOMATED_ROWS}}

## Manual smoke checks

| ID | Mandatory | Category | Status | Evidence | Notes |
| --- | --- | --- | --- | --- | --- |
{{MANUAL_ROWS}}

## Known limitations

- {{KNOWN_LIMITATIONS}}

## Final release decision

| Field | Value |
| --- | --- |
| Decision | {{FINAL_DECISION}} |
| Decision owner | {{DECISION_OWNER}} |
| Decision notes | {{DECISION_NOTES}} |
