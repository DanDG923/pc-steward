# Task 03 — Privilege boundary and safe deletion spike

**Status:** PASS WITH VM FOLLOW-UP on 2026-07-28; ADR-013 remains Proposed.
**Host boundary:** The spike ran as the current standard process in a newly generated temporary root. It did not request UAC, start WinGet, delete real application data, install a service, persist a helper, or touch a provider cache.

## Executed evidence

Run:

`pwsh -NoProfile -File spikes/security-boundaries/tests/SecurityBoundarySpike.Tests.ps1`

Result:

`PASS: 42 assertions; 1 VM/manual skips`

The runner created `pc-steward-security-spike-<random>` under the OS temporary directory, placed both the simulated owned root and outside sentinels inside that container, and removed the container in `finally`. No test path came from application metadata.

## Safe-deletion findings

| Case | Observed result |
|---|---|
| Relative, sibling/alternate-root, `..` metadata traversal, root-prefix confusion, UNC/device-style and alternate-data-stream paths | Denied before enumeration or deletion. |
| Fixed reset scope | Ten compiled categories only: database, scan data, source metadata, logs, update history, downloads, settings, consent records, user decisions and current-user scheduled-task state. The caller supplies an enum, never a path. |
| Junction to an outside sentinel | Rejected as a reparse component; cleanup returned Partial and the outside sentinel survived. |
| Symbolic link | Creation was denied by this host with `Administrator privilege required for this operation`; the exact VM case below remains mandatory. |
| Mount point and other reparse tags | The implementation rejects every existing component carrying `FILE_ATTRIBUTE_REPARSE_POINT`; a real volume-mount fixture remains mandatory in a disposable VM. |
| Locked file mixed with a deletable sibling | The sibling was deleted, two bounded attempts were made, the locked file remained, and the report returned Partial with relative residual `logs\locked.log` / `LockedOrInUse`. |
| Retry and idempotence | Repeating after releasing the lock succeeded; a third call returned Success with `AlreadyAbsent=true`. |
| Final-component path swap | A test hook replaced an already validated directory with a junction before handle acquisition. Immediate revalidation rejected it and preserved the outside sentinel. |

The final delete opens the exact object with `FILE_FLAG_OPEN_REPARSE_POINT`, requests delete access, excludes delete sharing, and marks that handle for deletion. This prevents a final object from being renamed or replaced after the handle is acquired. Enumeration is child-first; a concurrently added child makes the parent deletion fail and therefore cannot become false Success.

## TOCTOU limitation

Public path-based `CreateFileW` still resolves ancestor components between immediate validation and handle acquisition. This spike proves fail-closed handling of the exercised final-component swap, but it does not prove that a hostile same-user process cannot win an ancestor-component race. Task 24 must either use a reviewed handle-relative/non-following primitive for the full walk, or demonstrate an equivalently strong design; Task 25 must treat any unresolved escape as a release blocker.

## No-op elevated-helper protocol

The test-only protocol accepts exactly ten string fields: protocol version, immutable job ID, fixed operation, provider, source, reviewed package ID, target version, nonce, issued time and HMAC tag.

- Only `UpdatePackage` / `WinGet` / `winget` and a package ID from the immutable reviewed-job allowlist can reach acceptance.
- A 256-bit test key authenticates a length-prefixed canonical message; job ID and nonce are independently bound and the tag comparison is fixed-time.
- Lifetime is two minutes. Expired, denied and cancelled are distinct from `AcceptedNoOp`.
- Unknown or duplicate fields are rejected, including a tested `workingDirectory` field.
- The shape has no executable path, command string, arguments, working directory, environment, URL or output path.
- Acceptance is a no-op. No executable or provider was started and no IPC endpoint, service or elevated process was created.

This is protocol-shape evidence only. Task 22 must prove restrictive endpoint ACLs, caller/session binding, key establishment, helper signing, one-request lifetime, UAC denial and process teardown on Windows 11.

## Exact disposable-VM follow-up

Use a clean Windows 11 x64 VM snapshot and a standard-user session. Do not use real PC Steward or profile-data paths.

1. Create a uniquely named temporary container with `owned` and `outside` children and an outside sentinel.
2. With Developer Mode enabled in that disposable VM, create both a file symbolic link and directory symbolic link under `owned\downloads` targeting the outside sentinel/directory.
3. Run the test runner and require Partial, a reparse residual, and an unchanged outside sentinel for each link.
4. For a volume mount, attach a non-system disposable scratch VHD, create an empty directory under `owned\downloads`, mount only the scratch volume at that directory, and place a sentinel on the scratch volume.
5. Run the same validation/deletion call and require Partial plus an unchanged scratch-volume sentinel.
6. Detach the mount with `mountvol <test-mount-directory> /D`, detach the scratch VHD, remove the temporary container, and revert the VM snapshot.
7. Repeat an ancestor-component swap loop while cleanup runs; require zero outside deletions and preserve every denial/partial result for Task 25 review.

Do not mount the system volume, reuse an existing directory, or run these cases outside a disposable VM.

## Decision impact

- OD-13: Option A remains preferred, with Option B as the locked-handle fallback. The generic deletion result model is viable, but production selection cannot be locked until the VM reparse cases and full-walk TOCTOU design pass.
- Elevated helper: the minimal authenticated one-shot request shape is viable as a Task 22 starting point; transport, signing and UAC lifecycle remain unproven.
- OD-11: locked residuals and idempotence are supported as deletion primitives only. Quota, active leases, retention order and provider ownership remain Task 04/24/29 evidence.
