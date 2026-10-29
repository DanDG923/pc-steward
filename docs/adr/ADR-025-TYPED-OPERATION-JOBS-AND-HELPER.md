# ADR-025 — Typed operation jobs, helper protocol and update evidence

**Status:** Accepted

**Date:** 2026-10-29

**Owner acceptance:** 2026-10-29 — **“Accept the corrected ADRs and final choices”** accepts the full corrected contract and final N1–N6 author clarifications after both requested Opus 5.5 reviews. Specifically accepted: 30-minute frozen-review-to-item-dispatch ceiling; separate two-minute just-in-time helper-request freshness; common exclusive current-user gate across processes/logon sessions; new install/uninstall 10-minute overall deadline plus at most five seconds teardown attempt, with unresolved-work barrier. Existing Update deadlines are unchanged. Final author clarifications were disclosed and not independently re-reviewed; acceptance is the owner's decision. No implementation/deployment/live-action gate closes here.

**Decision owners:** Product owner, implementation reviewer and security reviewer

**Related requirements:** `FR-UPD-01–11/14–15`, `AC-UPD-01–11`, `FR-CAT-03–06`, `FR-UNI-01–04`, `FR-UI-01–07`, `FR-HIS-01–06`, `TM-03–04/07/13`

**Related decisions:** Task 47 owner-accepted Q1 catalogue/progress, Q2 existing deselection/pin/ignore, Q3 warning coverage, Q5 history and final review-lifetime/gate/deadline refinements. Q10 ADR acceptance is closed; Q4 route proof and Q9 exact host authority remain open.

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

## Accepted decision

Accept A. The following are accepted additive amendments governing future 2.0 extensions. Existing baseline ADRs still describe current implementation until detailed alignment accompanies the relevant implementation task. Implementation/security and applicable exact-host evidence remain necessary; this acceptance changes no source or deployed behavior.

### Canonical operation contract

Use a closed discriminated payload under an explicitly versioned envelope. Bind protocol/schema version, operation, job/correlation ID, approval time, exact provider/source/native identity, scope/architecture/channel as applicable, requesting user and evidence identity/freshness. Include enforceable installer mode in the reviewed canonical payload where supported; otherwise expose the mode as unsupported, with no silent fallback. Keep operation-specific required fields separate:

| Operation variant | Required meaning | Forbidden substitution |
|---|---|---|
| Update | Exact installed instance/product association, installed version and strictly eligible target version, mapping/compatibility evidence. | Missing installed version cannot mean Install; no change to existing provider/source compatibility rules. |
| Install | ADR-023 exact version and enforceable scope/architecture with fresh complete absence evidence. | An already installed instance cannot be upgraded, repaired or replaced under install approval. |
| Uninstall | ADR-024 exact installed registration/version/user and complete targeting/exclusion evidence. | No candidate-version default, all-user widening, alternate registration or bulk target. |

No variant contains executable path, command text, installer URL, working directory, environment, output destination or free-form flags. Optional fields cannot change the operation or supply missing required evidence. Digests cover the discriminator, protocol/schema, mode and complete canonical payload. Unknown enums, duplicate/missing/inapplicable fields, overlong values and noncanonical encodings reject. A recomputed digest on a tampered row still grants no approval or target authority.

Provider options form a compiled per-operation allowlist: exact predefined source, exact native identity/version, enforceable scope/architecture, reviewed supported mode and independently verified operation-specific safety controls only. Install additionally binds complete absence/dependency evidence, agreement identity/version/text digest and explicit acceptance plus an evidenced no-upgrade/no-repair guard. Uninstall has exact registration/exclusion evidence and no candidate-version fields. No force, hash/signature bypass, security/policy override, custom arguments, custom install location, unknown-version acceptance, dependency-ignore, implicit agreement acceptance or arbitrary environment/working directory is representable, including typed equivalents and Chocolatey force options. Concrete supported API option mapping/strict serialized fields must be reviewed before implementation; an unknown required option blocks that combination.

Interactive mode is exposed only if evidence shows its supported route cannot widen the reviewed scope/target/effects through UI choices. Merely detecting drift afterwards is not enforcement. If that proof is absent, interactive is unavailable; silent mode is also not assumed safe without capability proof. Independently verify post-state in either mode; observed target/scope/version/component drift is Unsafe/Unknown, never verified success or a recent-install entry. PC Steward cannot prevent an independently launched third-party UAC or all installer side effects; disclose residual limitations without using them to waive route eligibility.

Preserve the live in-memory user-review binding across the application/executor boundary. Persisted jobs/events support display and recovery, not unattended authorization. Reject an invocation whose operation, job, evidence or mode differs from the confirmed review even if all fields are individually well formed. Restore legacy jobs as update-labelled history; never infer Install/Uninstall/mode from missing columns. Migration, upgrade, crash, close or restart never resumes approved-but-unstarted jobs automatically.

**Accepted N1 clarification:** one explicit review freezes an entire update/install batch. Its maximum review-to-item-dispatch lifetime is **30 elapsed minutes** from that review, including scan/lease waits, in-use pauses and earlier items; the same ceiling applies to a single manual operation. Revalidate each item's complete evidence/mode/source/consent immediately before dispatch without asking for another confirmation solely because a prior item exceeded two minutes. At review expiry, let an already dispatched item finish within its separate execution deadline, but require a fresh review for remaining items; never silently renew/add/retarget them. A newly detected in-use warning still needs explicit acknowledgment. The owner accepted this value in the final Task 47 decision; it is not retroactively attributed to the earlier defaults selection.

The existing **two-minute helper-request lifetime** is separate: create a fresh request/nonce just in time after the live batch review and fresh item validation, bind its reviewed operation/job/user/approval identity, and require helper admission/pre-invocation freshness under ADR-018. It never measures how long the item waited in the batch and does not renew an expired batch review. The helper validates the bound review timestamp/30-minute ceiling too; the request carries timestamps, not a UI-consent safety assertion. Clock anomaly or loss of live review binding invalidates dispatch. Test 4-minute first installers, long scan waits, exact 30-minute/one-tick-over boundaries, delayed UAC and per-item evidence/late-warning changes.

### Privilege and protocol boundary

Retain ADR-018's fixed protected desktop/helper identities, ancestry/binary ACL and reparse checks, trusted same-signer verification immediately before launch, random per-request user/SYSTEM pipe, both endpoint PID/token/session checks, nonce, expiry, single-request lifetime and teardown. Bootstrap arguments carry only protocol/PID/pipe/nonce. The helper never opens SQLite or accepts a desktop-supplied assertion such as `isSafe=true` in place of independently resolved typed operation evidence.

Require an explicitly new protocol version for changed payload semantics; allocate its exact value and bounded schema in reviewed implementation. Each desktop/helper pair accepts exactly one protocol version, pinned in the ADR-022 exact artifact manifest; a new helper never also accepts protocol 1. Existing unchanged protocol-1 deployments remain update-only until separately approved replacement, without gaining new capabilities. Both endpoints prove matching capability before dispatch; mismatched/partial deployments reject without provider launch. No downgrade, reinterpretation or retry via old UpdatePackage. Apply the owner-accepted [Q1 progress/frame/approval bounds](../requirements/V2_PLANNING_DECISIONS.md); progress cannot create requests or expand privilege.

Trusted operation-specific code reconstructs only fixed supported provider/API calls after the responsibilities below succeed. Source consent and live UI review belong to the desktop trust boundary; do not claim the helper reads or independently authenticates user intent from SQLite. The helper validates authenticated requesting-process/user/protocol/nonce/freshness binding and independently proves the safety of the exact requested operation. A request baseline is comparison data, never a safety assertion. Preserve constrained Chocolatey client/feed checks; no Chocolatey install/uninstall or Store execution. No user-to-machine fallback, nonce reuse or second item on one helper request.

| Responsibility | Desktop / application / runner | Elevated helper for new-protocol operations |
|---|---|---|
| User intent / source consent | Check current enabled-source consent, live in-memory exact review binding, per-item approval age/cancellation and durable dispatch claim. Bind complete canonical request to that review. | Validate authenticated desktop/user/session identity, canonical fields/digest, nonce/one-shot freshness and supported predefined source. No SQLite access, isSafe flag, or claim of independently proving UI consent. |
| Update | Fresh trusted exact source/installed-to-candidate association, strictly eligible version, scope/architecture/channel/mode and compatibility. | Fresh independently resolved same installed instance/source/candidate eligibility and enforceable mode/scope/architecture, plus fixed provider/client/feed checks. If these cannot be independently obtained, new-protocol machine Update is unavailable. Existing protocol-1 behavior is not silently represented as this stronger capability. |
| Install | Complete relevant-view absence, no plausible linked/unlinked match, exact candidate/dependency/agreement/capability evidence and scope/mode; fresh validation at dispatch. | Requery its supported fresh relevant installed views and exact trusted candidate, dependencies/agreements/no-upgrade guard/scope/mode itself. Bind evidence to the requesting user's supported view; elevated HKCU/ambient-user enumeration is insufficient. Compare covered-view identity and baseline, reject missing/extra/conflicting registrations. If requesting-user/cross-user completeness cannot be proved, block route. |
| Uninstall | Fresh exact registration/current-user association, complete exclusions/dependencies, other-instance baseline and manual impact warning. | Resolve current selected registration and full covered instance set, exclusions/dependencies/self-provider runtime deny-set and exact scope/user itself; compare complete before/other-instance baseline as untrusted data. Unknown or unenforceable selection blocks. |
| Results | Requery operation-specific complete postcondition, preserve baseline other instances and record outcome/history; no success from exit alone. | Return only typed bounded correlated provider/result evidence. Desktop performs its own post-verification; neither side treats another side's success boolean as postcondition proof. |

The new protocol deliberately strengthens Update helper validation; regression fixtures and exact-host evidence are required before replacement. Unproven new-protocol routes are visibly disabled, never silently routed through the old protocol. It is acceptable to retain the unchanged old deployment until separate replacement authority; no new operation is retrofitted into it.

### Progress, outcomes, cancellation and recovery

Use bounded typed events tied to job/operation and ordered by the trusted state machine: review, queued/preparing, observable execution, verification, final result. Provider-reported subphases or percentages need actual correlated provider evidence; an opaque installer stays indeterminate. No timer-generated download percentages or completion from process exit. Sanitize text and limit output before logs/UI; progress data cannot select a command, target or mode.

Propagate reliable provider restart evidence through runner, helper result, application history and UI without inventing it from arbitrary output text. Targeted operation-specific post-inventory is still required for verified Updated/Installed/Removed; reliably supported Restart pending is a distinct result. Failure, UAC denial, cancellation, timeout, provider crash and unknown verification remain distinct and cannot overwrite a prior partial effect with Success.

For G4, freeze the user-reviewed eligible update selection; never add newly discovered items to Update All. Revalidate every item immediately before sequential launch. A stale/newly incompatible item blocks with a reason, never retargets. Cancellation prevents the next unstarted item and retains all per-item outcomes; no transactional rollback promise. Uninstall remains single-target. Apply owner-accepted Q2 existing pin/ignore/deselection semantics; no new major/minor pin or skip-version feature.

Retain existing in-use warnings and repeat detection under the owner-accepted Q3 bounded heuristic/coverage disclosure. Offer cancel/manual closure/explicit continuation without target force-close. A signed installer may have effects beyond app observation. On lost acknowledgement/crash after possible invocation, mark uncertainty, verify state and require fresh review for another action; never replay automatically. Serialize dependent actions/clear/reset through the operation lease; check cancellation and consume one-shot request at the execution boundary. This is crash-safe refusal/reconciliation, not exactly-once third-party effects.

### Durable dispatch and cross-process lease

Before any user provider invocation or helper/UAC launch, the desktop must commit a unique `DispatchClaimed` compare-and-set transition with complete immutable reviewed identity and pre-state under the same current-user cross-process data-operation/dispatch gate used by ADR-029. Reserve terminal-history capacity first under Q5. Commit/durability failure prevents launch. Only the still-live approved request holding that gate may invoke once. The elevated helper remains database-free and consumes its own one-shot nonce; a persisted claim never independently authorizes launching it after restart.

After any crash/exit, claimed-without-final is `Uncertain` whether launch happened or not; unclaimed queued rows remain explicitly unstarted/cancelled/expired and never auto-resume. On an explicitly requested read-only reconciliation, query exact targeted state; do not infer invocation did not happen from missing provider IDs or a failed acknowledgement. Any new execution needs fresh review. Pending restart can become verified later only from complete reviewed postconditions, never an automatic replay.

Document one async-safe gate/lock order before implementation: acquire shared current-user data-operation gate, make bounded SQLite transaction, commit/release transaction before provider work, hold operation ownership through invocation and bounded result/verification commit, then release. Serialize dependent scan/execution overlap per FR-RSC-02, clear/reset and AI dispatch using the same gate; cancellation cannot claim success while work still owns it. Never hold the existing thread-affine reset mutex over arbitrary `await`; align its adapter/ownership with ADR-029 rather than layering a deadlocking second lock. Helper/provider cancellation/teardown and abandoned-gate recovery require explicit tests. Disk failure after invocation is an uncertain persistence/result failure, never not-started or verified success.

Here **shared** means one common gate, not shared-reader admission for mutating operations. Accepted v1 compatibility/timeout policy:

| Work | Concurrency / wait policy |
|---|---|
| Full inventory/source scan, diagnostic collection, update/install/uninstall, AI reservation/send/settle, clear/reset | Mutually exclusive per current user across app processes and logon sessions; no second mutating workflow while one holds ownership. In-operation verification/transactions reuse that ownership, never recursively acquire it. |
| Existing read-only UI over committed state | May remain usable concurrently; no execution/clear/approval renewal or background send. |
| A reviewed item waiting on an active ScheduledHost scan or another operation | Cancellable wait bounded by its original 30-minute review expiry; revalidate everything after acquisition. Expired item needs fresh review. A scheduled scan does not renew an interactive review. |
| New install/uninstall route | Accepted 10-minute monotonic item deadline including helper connection/UAC/provider/verification; at timeout attempt bounded cancellation/owned-work teardown, with at most 5 seconds for the teardown attempt. Existing Update-specific deadlines are not extended by this decision. |
| Teardown cannot establish all relevant owned provider/installer work stopped | Release live OS synchronization ownership only after bounded teardown/recovery bookkeeping, retaining a durable unresolved-operation barrier which refuses conflicting scan/execute/clear/reset across processes/restarts until explicitly requested safe reconciliation proves no dependent work remains. Never claim the barrier kills external/detached work or enables automatic retries. UI remains responsive and discloses blocked operations. |

The existing `Local\PCSteward.DataOperation.v1` mutex is session-local and thread-affine; it is not evidence of this stronger gate. Owning implementation must prove namespace/identity/ACL/abandonment/async ownership across same-user sessions, bounded waits and crash recovery before support. Unknown residual work or unproven synchronization keeps that route unavailable; no resident/elevated service or hidden reset-surviving state is added. The owner explicitly accepted the 10-minute deadline/teardown/common-gate policy together with the 30-minute review choice in the final Task 47 decision.

### Amendment and deployment map

| Existing ADR | Accepted additive amendment, requiring implementation/evidence alignment | Boundary retained |
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
