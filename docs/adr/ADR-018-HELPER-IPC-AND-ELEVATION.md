# ADR-018 — Helper IPC and elevation

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Security reviewer and implementation reviewer
**Related requirements:** `FR-UPD-04`, `AC-UPD-03–05`, `TM-04`

## Context

Machine-scope updates may require UAC, but the desktop must never become generally elevated or expose a reusable privileged endpoint.

## Options considered

### Option A — One-shot signed helper over an identity-checked per-request named pipe

Elevation is delayed until execution and the helper has one narrow provider capability.

### Option B — Elevated service, broker or desktop process

This simplifies reuse but creates ambient privileged state and a broader attack surface prohibited by the MVP.

## Decision

### 2026-07-30 standalone-package revision

The accepted private-beta package is standard-user-only and does not compose or ship this helper. `UpdateExecutionCapabilities.UserScopeOnly` stops machine-scope preparation before structured execution authority or job creation; the standalone `ApprovedUpdateExecutor` has no elevated transport and independently rejects any machine-scope job. The desktop project has no `PCSteward.ElevatedHelper` reference. The protected-root helper design below remains accepted only for a future separately reviewed installed deployment; it is dormant source, not a capability of the standalone package.

Accept Option A. After confirmation, the standard-user desktop resolves only the fixed `%ProgramFiles%\PC Steward` desktop/helper pair. Before creating elevated code, it rejects missing or reparse-path binaries, any current-token write/delete/DACL/owner authority over either binary or the installed-path ancestry, an untrusted signature, or a signer mismatch. It repeats this validation immediately before launch, then creates a random per-request named-pipe endpoint restricted to the current user and SYSTEM and launches the helper through the Windows `runas` verb. Launch parameters contain only protocol/bootstrap data: protocol version, requesting process ID, pipe locator and nonce; never provider operation data.

The helper connects once, verifies the pipe server process ID, fixed signed desktop identity, requesting user/session, protocol and nonce, then accepts one bounded canonical request matching ADR-017. The desktop verifies the connected helper process, fixed signed helper identity and session again after connection; post-connect validation is defence in depth and never substitutes for the pre-launch gate. The helper rejects unknown/duplicate/overlong fields, replay, expiry, paths, URLs, environment, working directory, output destinations and free-form flags. It reconstructs the allowlisted WinGet operation from trusted code, returns a bounded structured result and exits. UAC denial, cancellation, timeout and crash are distinct. No helper secret or executable request is read from SQLite.

Option B is rejected by the no-resident-elevated-process requirement and because persistence would enlarge the privilege boundary.

## Consequences

### Positive

More privilege has less capability, and each approval creates a fresh authenticated one-request channel.

### Negative / trade-offs

Windows process identity, signing, pipe ACL and teardown behavior require isolated-environment security evidence.

### Follow-up controls and tests

Task 22 must prove installed-root and binary ACLs, reparse and replacement rejection before launch, server/client PID and token/session checks, pre/post-launch signature/path validation, nonce/replay/expiry, mutation, UAC denial, process-tree cancellation, single-request lifetime and teardown. Task 25 treats any spoofing, pre-validation launch, arbitrary input or surviving helper as a release blocker.

## Task 90 final evidence

An approved disposable host fixture exercised the actual default `WindowsElevatedHelperTransport`, `InstalledHelperLaunchTrust.Validate`, named-pipe protocol and helper peer verification from the exact fixed path. The signed, same-signer, non-reparse, Users-RX protected layout reached UAC only at execution; the helper accepted one immutable machine job, used a friend-only no-op provider delegate, returned `Completed`/exit `0`, and exited. Tampered-signature, trusted-wrong-signer, Users-modifiable, helper-symbolic-link and privileged between-validation replacement layouts all returned the expected production diagnostic with the helper-start marker absent.

Windows' default volume-root ACL grants Authenticated Users `FILE_ADD_SUBDIRECTORY` without delete-child/delete/DACL/owner. The first clean fixture therefore exposed an over-broad ancestor probe. The corrected policy retains full create/write/delete/DACL/owner denial on the installed root and binaries, and checks deletion/replacement/DACL/owner rights on higher ancestors; harmless sibling creation at a higher ancestor is not path-replacement authority. The final positive and replacement cases validate both checks around that boundary. `TASK_90_P3R15_HOST_EVIDENCE.json` records exact owner, SDDL, `icacls`, attributes, signatures, signer equality, outcomes and zero-residual teardown.

## Reconsider when

Machine-scope execution is restored, the selected Windows APIs cannot prove both endpoint identities across UAC, a future installer cannot provide a fixed non-caller-writable binary root, or security tests expose a replacement/replay/spoofing path. No caller-writable helper or persistent-service fallback is allowed without a new product and threat-model decision.

## 2026-10-08 v0.3 revision — one helper session per approved batch (D-007)

**Related requirements:** `FR-BAT-04/05/06`, `FR-AUT-08/09`, `AC-V3-04/05/09`

The protected owner-only installation (ADR-022) composes this helper. Protocol **v2** replaces the one-job request for batches:

- **Request.** The desktop sends one batch request: protocol version 2, nonce, issued time, batch ID and digest, and up to 50 machine-scope items. Each item uses the v1 field allowlist plus `operation`. Messages are framed per item so each frame stays within a fixed size bound.
- **Helper behaviour.** The pre-launch, peer-identity and nonce checks are unchanged. The helper:
  - verifies the batch digest and expiry;
  - revalidates each item;
  - rebuilds each provider argv in code;
  - runs items sequentially;
  - streams bounded `ItemStarted`/`ItemResult` frames;
  - accepts `CancelRemaining` only between items;
  - enforces an overall deadline (the sum of per-item timeouts, hard-capped);
  - exits.

  A frame cannot add, reorder or replace an item.
- **Prompts.** One approval means one UAC prompt and one helper process. A second batch starts a new helper.
- **Other operations.** The helper also accepts two fixed operations for ADR-025: `WriteAutomationPolicy` (a bounded policy document validated against the bundled catalog) and `RemoveAutomation`. They are the only way to change the Stage B store or task.
- **Stage B host.** The Stage B elevated host is a separate entry point started by Task Scheduler. It is not a reusable endpoint. It accepts no IPC, reads no per-user database, applies the same item validation and provider construction, and exits.

Follow-up tests (Tasks 49, 55, 58): batch substitution, reordering, appending, replay, expiry, oversize frames, cancel races, caller mismatch, policy-document tampering, and Stage B isolation from the per-user database.
