# ADR-025 — Typed operation jobs, helper protocol and update evidence

**Status:** Proposed

**Date:** 2026-10-29

**Decision owners:** Product owner, implementation reviewer and security reviewer

**Related requirements:** `FR-UPD-01–11/14–15`, `AC-UPD-01–11`, `FR-CAT-03–06`, `FR-UNI-01–04`, `FR-UI-01–07`, `FR-HIS-01–06`, `TM-03–04/07/13`

**Related open decisions:** Task 43 Q1 (new bounds), Q2 (skip semantics), Q3 (in-use coverage), Q4 (removal proof), Q5 (history lifecycle), Q9/Q10 (host/ADR authority)

## Context and observed baseline

[UpdateJobs.cs](../../src/PCSteward.Domain/UpdateJobs.cs) defines an update selection with installed/candidate versions and a canonical digest. [ElevatedHelperProtocol.cs](../../src/PCSteward.Execution/ElevatedHelperProtocol.cs) currently admits protocol `1`, operation `UpdatePackage`, a 16 KiB message bound and a two-minute request lifetime. [ApprovedUpdateExecutor.cs](../../src/PCSteward.Execution/ApprovedUpdateExecutor.cs) and the runner accept immutable update evidence. These static observations are not fresh build, runtime or security acceptance evidence.

[ADR-017](ADR-017-IMMUTABLE-UPDATE-JOBS.md) says digests detect mutation but confer no trust; [ADR-018](ADR-018-HELPER-IPC-AND-ELEVATION.md) requires authenticated one-shot IPC and protected binary identities. Extending only an enum or nullable version field risks interpreting install or removal approval as an update. `FR-UPD-14–15` also requires truthful progress and enforceable immutable mode without a general command channel. The accepted Task 44 text supplies requirements; subsequent [project recovery](../review/TASK_45_PROJECT_RESTORE.md) closes build/test validation without accepting or implementing these proposed contracts.

## Decision drivers

Preserve existing update safety and constrained Chocolatey update support. Add explicit operation authority, strict parsing, no ambient elevation or SQLite authority, compatibility rejection, conservative recovery and independent verification. Keep UI, application orchestration, domain evidence, providers, persistence and privileged execution separate.

## Options considered

| Option | Benefits | Costs and risks |
|---|---|---|
| A — Versioned closed set of typed operation variants and strict dispatch | Operation-specific required fields; independent review and execution checks; unknown capabilities fail closed. | Protocol/migration/deployment compatibility and separate negative matrices require work. |
| B — Reuse UpdatePackage with optional installed/target fields and arbitrary action flags | Fewer new types. | Ambiguous interpretation, missing-field defaults and substitution/downgrade risks. Reject. |
| C — General privileged command broker or resident helper | Convenient expansion. | Paths/flags/URLs or ambient capability violate the trust boundary. Reject. |

## Proposed decision

Recommend A. The following are **proposed amendments**, not edits to or acceptance of existing ADRs. Owner acceptance, an implementation/security gate and applicable host evidence remain necessary.

### Canonical operation contract

Use a closed discriminated payload under an explicitly versioned envelope. Bind protocol/schema version, operation, job/correlation ID, approval time, exact provider/source/native identity, scope/architecture/channel as applicable, requesting user and evidence identity/freshness. Include enforceable installer mode in the reviewed canonical payload where supported; otherwise expose the mode as unsupported, with no silent fallback. Keep operation-specific required fields separate:

| Operation variant | Required meaning | Forbidden substitution |
|---|---|---|
| Update | Exact installed instance/product association, installed version and strictly eligible target version, mapping/compatibility evidence. | Missing installed version cannot mean Install; no change to existing provider/source compatibility rules. |
| Install | ADR-023 exact version and enforceable scope/architecture with fresh complete absence evidence. | An already installed instance cannot be upgraded, repaired or replaced under install approval. |
| Uninstall | ADR-024 exact installed registration/version/user and complete targeting/exclusion evidence. | No candidate-version default, all-user widening, alternate registration or bulk target. |

No variant contains executable path, command text, installer URL, working directory, environment, output destination or free-form flags. Optional fields cannot change the operation or supply missing required evidence. Digests cover the discriminator, protocol/schema, mode and complete canonical payload. Unknown enums, duplicate/missing/inapplicable fields, overlong values and noncanonical encodings reject. A recomputed digest on a tampered row still grants no approval or target authority.

Preserve the live in-memory user-review binding across the application/executor boundary. Persisted jobs/events support display and recovery, not unattended authorization. Reject an invocation whose operation, job, evidence or mode differs from the confirmed review even if all fields are individually well formed. Restore legacy jobs as update-labelled history; do not replay them or infer Install/Uninstall/mode from missing columns. Migration/upgrade must not resume approved-but-unstarted jobs automatically; obtain fresh evidence and confirmation.

### Privilege and protocol boundary

Retain ADR-018's fixed protected desktop/helper identities, ancestry/binary ACL and reparse checks, trusted same-signer verification immediately before launch, random per-request user/SYSTEM pipe, both endpoint PID/token/session checks, nonce, expiry, single-request lifetime and teardown. Bootstrap arguments carry only protocol/PID/pipe/nonce. The helper never opens SQLite or accepts a desktop-supplied assertion such as `isSafe=true` in place of independently resolved typed operation evidence.

Require an explicitly new protocol version for changed payload semantics; allocate its exact value and bounded schema in the reviewed implementation. Existing protocol 1 remains update-only. Both endpoints must prove matching capability before dispatch; old/new or partial-deployment mismatches reject without launching a provider. No downgrade, reinterpretation or retry via the old UpdatePackage operation. Preserve current message/output/lifetime bounds unless a separately reviewed bound is justified; new progress frames need total-byte/event-rate/queue/time bounds under Q1 and cannot create requests or expand privilege.

Trusted operation-specific code reconstructs the fixed supported provider/API call only after independent fresh validation of source consent, exact target, scope, capability, mode and operation-specific absence/exclusion evidence. Preserve the validated Chocolatey client/feed boundary for existing updates; do not add Chocolatey install/uninstall or Store execution. User operations cannot fall back to machine scope; elevated operations require that exact reviewed scope and protected deployment. A helper cannot reuse one nonce/approval for another operation or item. Missing fresh validation in the elevated context blocks the affected route.

### Progress, outcomes, cancellation and recovery

Use bounded typed events tied to job/operation and ordered by the trusted state machine: review, queued/preparing, observable execution, verification, final result. Provider-reported subphases or percentages need actual correlated provider evidence; an opaque installer stays indeterminate. No timer-generated download percentages or completion from process exit. Sanitize text and limit output before logs/UI; progress data cannot select a command, target or mode.

Propagate reliable provider restart evidence through runner, helper result, application history and UI without inventing it from arbitrary output text. Targeted operation-specific post-inventory is still required for verified Updated/Installed/Removed; reliably supported Restart pending is a distinct result. Failure, UAC denial, cancellation, timeout, provider crash and unknown verification remain distinct and cannot overwrite a prior partial effect with Success.

For G4, freeze the user-reviewed eligible update selection; never add newly discovered items to Update All. Revalidate every item immediately before its sequential launch. A stale or newly incompatible item blocks that item and records why; do not silently retarget it. Cancellation prevents the next unstarted item and retains all per-item outcomes. No transactional rollback promise. Uninstall remains a single manual instance, not a batch operation. Q2 skip semantics stays open; no new major/minor pin or skip-version feature is introduced.

Retain existing in-use warnings and repeat detection before execution; Q3 must define/disclose supported detection coverage. Offer cancel/manual closure/explicit continuation without target force-close. A signed third-party installer may have effects beyond PC Steward's observation. On lost acknowledgement or crash after possible invocation, mark uncertainty, verify state and require fresh review for another action; never replay automatically. Serialize dependent actions and clear/reset through an operation lease; check cancellation and consume the one-shot request at the execution boundary to prevent duplicate launches. This is crash-safe refusal/reconciliation, not a claim of exactly-once third-party effects.

### Amendment and deployment map

| Existing ADR | Proposed narrow amendment if accepted and implemented | Boundary retained |
|---|---|---|
| ADR-003 (Provisional) | Separate bounded catalogue discovery/install authority and instance-removal capability, with supported API evidence per ADR-023/024. | Status remains Provisional; table output is display-only, no arbitrary sources or command authority. |
| ADR-015 (Accepted) | Persistence-owned versioned operation variants, append-only events, legacy update history migration and Q5 lifecycle. | Untrusted SQLite, transactional migration/recovery, no helper database access; existing decisions/history preserved. |
| ADR-017 (Accepted) | Typed operation discriminator, operation-specific evidence, immutable supported mode, correlated progress/restart results and frozen update batches. | Explicit per-run review, fresh revalidation, sequential execution and verification; no automatic execution. |
| ADR-018 (Accepted) | Strict new-protocol operation-specific validators and bounded typed results/progress. | Protected on-demand one-shot helper, endpoint identity and replay/substitution rejection; no service or generic broker. |
| ADR-022 (Owner-only) | Exact future payload manifest must identify supported operations/protocol; independent validator and security tests cover both endpoints. | Existing deployment/update approval is not install/removal approval; no redeploy/sign/certificate action, recipient or distribution authority follows. |

## Consequences

Strict compatibility may temporarily disable execution after a mismatched deployment. It is safer than accepting old semantics. Additional variants/verification paths require substantial negative testing; existing updates must remain correct while install/removal stay unavailable until individually evidenced. Operation protocol approval does not establish safe API targeting or accept the WS5 automation proposal.

## Follow-up controls and tests

Tasks 47/49–54/56–63 must cover every operation against each runner/helper capability, both positive cases and substitution/replay/downgrade negatives. Mutate operation, mode, user, source, package/instance, versions, scope, architecture, freshness, approval and digest; include recomputed tampered digests and stale/denied evidence. Test strict field/message/event bounds, old protocol rejection, missing/duplicate frames, timeout, spoofed endpoint, writable/reparse/wrong-signer binaries, prelaunch replacement, no provider launch before validation and zero residual helper/process trees.

Prove migration of existing update history with no implicit approval, crash windows before/after invocation and result persistence, cancellation at item boundaries, opaque provider progress, unknown modes, restart propagation and verification failures. Re-run original update/Chocolatey safety fixtures, canonical build and full suite before completion. An independent security review and separately authorized exact-artifact Windows host tests must establish UAC, actual scope/mode enforcement and installed-state outcomes; documentation or mocks cannot substitute. Update effective ADRs only through the owner's accepted amendment process.

## Reconsider when

A route cannot independently validate the reviewed operation, process/pipe identity or supported mode; provider/API behavior changes; or operation-specific bounds cannot fit the safe protocol. Keep that capability blocked pending a revised owner-reviewed contract.
