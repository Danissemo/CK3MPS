# Strict Fix Result Contract

Fix operations must not report success just because their mutation code completed without throwing. A fix is successful only after the current state is rescanned and the operation-specific target postconditions are verified.

## Required flow

```text
Prepare
-> Validate preconditions
-> Apply
-> Rescan current state
-> Validate operation-specific postconditions
-> Produce final result
```

## Result states

`FixOperationResult` uses the following final states:

- `Succeeded` — mutation completed, verification ran, target postconditions passed, and final readiness is `READY`.
- `PartiallySucceeded` — target postconditions passed, but unrelated readiness checks still block global `READY`.
- `Failed` — mutation did not run, mutation failed, verification failed, or target postconditions still fail.
- `Unsupported` — the operation cannot run safely in the current environment.
- `Cancelled` — the operation was cancelled before verified success.

## Required result fields

Each result carries:

- operation id;
- changed elements;
- preconditions and failed preconditions;
- postconditions and failed postconditions;
- target check ids and remaining target failed check ids;
- readiness before and readiness after;
- rollback status;
- user message;
- diagnostic details;
- mutation and verification success flags.

## Logging rules

- `RESULT| SUCCESS` is allowed only when `FixOperationResult.IsFinalSuccess` is true.
- A mutation with remaining target failed checks logs `RESULT| FAILED`.
- A failed rescan logs `RESULT| FAILED`, even when mutation completed.
- Target postconditions passing while unrelated readiness checks remain failed logs a partial result, not a false success.
- `NOT READY` must never be displayed as a fully successful fix result.

## Regression coverage

`scripts/test-fix-result-contract.ps1` validates the contract shape and the minimum result rules for:

- apply without exception while target checks remain failed;
- rescan failure;
- target checks fixed while unrelated checks remain failed;
- rollback status visibility;
- cancellation;
- unsupported operation;
- final success requiring verification and clean target postconditions.
