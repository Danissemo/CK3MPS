# Restore ownership and delete safety

CK3MPS treats deletion of restore data as a security-sensitive mutation. A restore point, backup, transaction, or recovery directory is deletable only when the app can prove that CK3MPS created it and that the object has not changed between display and deletion.

## Windows restore points

Windows restore points are never considered CK3MPS-owned by name, prefix, date, CK3/Steam wording, or location alone.

A CK3MPS-created restore point must have all of the following:

- description beginning with `CK3MPS before changes `;
- a unique marker in the description, formatted as `[CK3MPS-RP:<32 hex operation id>]`;
- an app-generated operation id;
- a row in `restore_point_ownership.tsv` under the CK3MPS state root;
- ownership schema version `1`;
- matching sequence number, creation time, description, operation id, and marker;
- a SHA-256 digest over the ownership row fields signed with the local CK3MPS restore-point ownership secret.

Delete Data displays unverified restore points as read-only. They cannot be checked for deletion. Bulk deletion also re-lists restore points immediately before mutation and skips any item that is missing, changed, unowned, duplicated, or no longer digest-valid.

Protective skips are logged as `PROTECT ...` messages. They are expected safety behavior, not application errors.

## Legacy and foreign restore points

Legacy restore points created before schema `1` do not have a marker and manifest row. They stay visible as informational read-only items. CK3MPS does not delete them automatically and the normal UI does not provide a deletion path for them.

A manually-created restore point with a similar name, a copied prefix, or a forged-looking marker is still unowned unless the manifest row and digest validate. If the manifest is missing, corrupt, tampered, duplicated, or points to an identity that no longer matches the current restore point list, deletion is refused.

## Restore manifest and app-owned data

`restore_manifest.tsv` remains the source of truth for file, directory, registry, and recovery entries in the Restore tab. Restore operations already validate manifest rows, allowlisted paths, canonical paths, reparse-point restrictions, backup-root containment, duplicate ids, and confirmation snapshots before mutation.

The same rule applies to transaction and recovery data: CK3MPS may delete only data under an allowed CK3MPS root after canonical path normalization and reparse traversal rejection. A path that escapes the root, uses a reparse point, or lacks trustworthy manifest/marker evidence is treated as unowned and skipped.

## Regression coverage

`scripts\test-restore-point-ownership.ps1` compiles and runs `tests\RestorePointOwnershipHarness.cs`. The harness covers:

- valid CK3MPS restore point ownership;
- prefix-only legacy restore points;
- manually-created similar descriptions;
- missing manifest;
- corrupt manifest;
- tampered manifest row;
- duplicate ownership rows;
- restore point identity changed after UI load;
- invalid sequence numbers;
- bulk deletion skipping unowned or missing items;
- read-only UI check prevention for unowned points.
