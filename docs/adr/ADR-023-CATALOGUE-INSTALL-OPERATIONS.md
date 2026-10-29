# ADR-023 — Catalogue search and explicitly approved install operations

**Status:** Accepted

**Date:** 2026-10-29

**Owner acceptance:** 2026-10-29 — **“Accept the corrected ADRs and final choices”** explicitly accepts ADR-023–025 and ADR-026 Option C after both Opus 5.5 reviews and the disclosed final author N1–N6 clarifications. This includes the 30-minute frozen-review dispatch window, separate two-minute helper-request freshness, exclusive cross-session operation gate and new install/uninstall 10-minute deadline plus at most five seconds teardown attempt. Acceptance closes this design gate only; route/API/host/implementation evidence and separate action-time authority remain mandatory.

**Decision owners:** Product owner, implementation reviewer and security reviewer

**Related requirements:** `FR-CAT-01–06`, `FR-SRC-02/07–10`, `FR-UPD-15`, `FR-HIS-01–06`, `FR-DAT-06–07/12–15`, `TM-02/04/13`

**Related decisions:** Q1 catalogue/progress, Q5 lifecycle and final batch/gate/deadline choices are owner-Accepted in Task 47. Q10 ADR acceptance is closed; Q9 exact host authority remains separate.

## Context

[Specification §§9.14/11.5](../product/PC_STEWARD_PRODUCT_SPEC.md) accepts manual catalogue installation as gated 2.0 scope. The existing [ADR-003](ADR-003-WINGET-INTEGRATION-ROUTE.md) authority proves an upgrade for an installed product; it cannot establish absence or authorize installation. [ADR-017](ADR-017-IMMUTABLE-UPDATE-JOBS.md) and the current helper remain update-only. This accepted additive contract is paired with [ADR-025](ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md); effective implementation behavior changes only after the reviewed implementation/capability gates.

Task 45 began against Task 44's accepted working-tree requirements under the owner's instruction to start. The subsequent [project recovery](../review/TASK_45_PROJECT_RESTORE.md) closes build/test validation without accepting this ADR or implementing installation. No current WinGet install API, scope-enforcement capability or host installability is asserted by this ADR.

## Decision drivers

Search is discovery, never execution authority. Installation needs a fresh not-installed baseline and a separately reviewed exact operation. Unknown source, version, scope, architecture, channel compatibility or existing-instance ambiguity must block. User scope alone does not sandbox third-party installer effects.

## Options considered

| Option | Benefits | Costs and risks |
|---|---|---|
| A — Bounded COM discovery, separate typed install approval and adapter | Reuses source consent and domain separation; distinguishes discovery from action; enforces per-scope capability gates. | Requires a new absence/target authority and capability evidence; some results remain information-only. |
| B — Treat a search result as an update with an empty installed version | Small initial implementation. | Confuses update/install approval, weakens existing update invariants and can repair/upgrade an existing instance. Reject. |
| C — Open an arbitrary installer link or execute catalogue commands | Broad apparent coverage. | Violates source, URL, process and privilege boundaries. Reject. |

## Accepted decision

Accept A. The owner accepted the corrected contract; implementation still waits for the gates below.

### Discovery and authority

1. Query only enabled, explicitly consented, predefined approved WinGet catalogues through the supported COM adapter. Keep Microsoft Store information-only. Do not promote Chocolatey, mirrors, custom repositories, arbitrary URLs or fuzzy/display matches to install authority.
2. Search returns bounded data: name, declared catalogue publisher (not verified publisher identity), exact package ID, source and version, plus scope/architecture where actually known. Display missing fields as unavailable. Label tags as tags (D1); do not infer categories, sizes or icons or fetch remote icons as an unreviewed network path. Escape hostile metadata in the existing Task 35 components.
3. Apply the owner-accepted [Q1 catalogue policy](../requirements/V2_PLANNING_DECISIONS.md) for query/count/field/byte/deadline/cancellation/concurrency bounds. Overflow, duplicate or ambiguous exact candidates confer no authority. Existing update-query limits do not prove bounded catalogue search.
4. Selecting Install invokes a separate fresh structured exact-source/ID/version authority and a complete relevant local inventory. Absence requires both zero exact provider-ID registrations and no plausible identity match at any confidence in complete HKLM 64/32-bit, HKCU, current-user MSIX and supported WinGet views. Vendor-installed/unlinked, conflicting-version or mixed-architecture possible matches are ambiguity, never ignored because low-confidence matches are not actionable. Missing/denied/partial/stale views block. Enumerate covered users explicitly: v1 cannot claim visibility into other users' private profiles. If the selected route needs cross-user absence/impact proof that cannot be obtained unelevated and independently in the helper where required, that route stays blocked; no disclosure alone waives the evidence requirement. An existing instance routes back to inventory/update review, never an implicit repair or upgrade.
5. The adapter must prove the selected version, architecture, scope and applicable channel can be enforced without fallback. Unknown or unsupported combinations are disabled. V1 requires a known dependency set with no newly installed/changed dependencies: the set is empty or every dependency is already satisfied with verified compatible state in reviewed scope. Unknown or unsatisfied dependency state blocks; do not silently install dependencies or invent dependency queue items. Bind the exact agreement identity/version (or canonical agreement-text digest), displayed terms and explicit acceptance to review/canonical payload; changed agreements require new review and automatic acceptance options are absent. Known material installer effects and limitations must be reviewed; unresolved effects that could violate the contract block.

Task 56 must define/version the deterministic plausible-match predicate before implementation: concrete normalized source/native identity, registration/publisher/product/architecture signals, conflicting/missing evidence and exact fixtures for linked/unlinked vendor installs, shared names and x86/x64 pairs. No AI judgment, ad hoc UI guess or fuzzy match grants action authority; a possible match blocks install pending safe manual resolution. Reviewer approval of the predicate and its false-negative fixtures is a route prerequisite.

### Review, execution and verification

Review shows **Install**, exact source/ID/version/architecture/scope, expected privilege, enforceable mode if offered, freshness and known installer effects. A new immutable install payload binds those values, the absence-evidence identity and timestamp, applicable user, job/correlation ID and approval time under ADR-025. It contains no executable path, URL or arguments. Later evidence changes require new review; a queue row cannot retarget the job.

Immediately before each launch, revalidate the reviewed identity, source consent, absence, dependencies/agreements, capabilities and scope under ADR-025's responsibility matrix. Require a documented, tested provider/API no-upgrade/no-repair guard in addition to the final absence check; if the adapter cannot prevent operation substitution at invocation, keep that install route unavailable. Do not claim the app lease locks out external installers. User installs are launched unelevated and PC Steward never requests elevation or retries as machine scope for that item. A third-party installer can initiate its own UAC/interaction; PC Steward cannot promise to prevent that. Disclose this residual limitation at review and only expose a route/mode with evidence supporting its reviewed scope/effects. Changed/unreviewed scope, version, components or instance after execution is Scope mismatch/Unsafe or Unknown, never verified Installed or a recent-install entry; no automatic rollback. Machine installs require separate scope-bound confirmation and protected-helper independent target/capability checks. An old protected update deployment does not gain install support from this proposal.

Run reviewed items sequentially. Each has independent approval/evidence and a final result; cancellation stops the next unstarted item. Provider progress is only as precise as observable evidence. Do not force-close applications, promise rollback, automatically uninstall on failure, or retry an uncertain installation after a crash.

Before provider invocation or helper/UAC launch, persist ADR-025's unique DispatchClaimed state under the cross-process operation lease. Missing acknowledgement/final result after that point is Uncertain, even if invocation might never have occurred; restart never labels it not started or resends. A later explicitly requested read-only verification may prove installed state, never that PC Steward caused it by itself. Verified PC Steward attribution/recent-install inclusion additionally requires durable job-bound provider/result evidence establishing this invocation; otherwise show **Installed; PC Steward attribution unknown** and exclude it from Recent installs. A pending-restart item with that invocation evidence may become verified later only with complete reviewed postconditions/no drift. Reconciliation never invokes installation again; a new action needs new review. Recent installs means verified installed by PC Steward at the recorded time, not that it remains installed now.

Post-inventory must find the expected version in the reviewed scope/architecture/user with an unambiguous stable instance association. Provider exit zero is insufficient. Report verified installed, failed/still absent, reliable restart pending, or unknown/partial as evidence warrants. A restart result is not a verified installation or a recent-install entry until the expected installed state is established. Verification does not claim to prove every installer side effect.

### Accepted persistence ownership — Task 47 Q5

Keep favourites as reversible local catalogue-ID/source preferences, separate from inventory/curated membership. Recent installs derive only from verified PC Steward install outcomes, never clicks, queued attempts, restart-only or pre-existing apps. The owner accepted the concrete [Task 47 Q5 policy](../requirements/V2_PLANNING_DECISIONS.md): preserved favourites/history on routine scan/download/log/source clears; explicit selected operation-history clear removes its derived recent view, preserves favourites/accounting/keys and preserves frozen diagnostic captured-history copies with ADR-027 R1 disclosure; Factory reset removes all app-owned categories. Existing update-history retention remains unchanged. The linked policy defines capacities/clear labels and no new history retention default is borrowed from diagnostics. History rows are self-contained with no scan-owned foreign-key cascade; migrations preserve legacy jobs/decisions and never restore execution authority. FailedUpdate AI explanations remain update-only; failed install/removal explanation support is not promoted by operation history.

## Consequences

Installation remains a narrow manual capability with honest unsupported results. Separate absence/target evidence and a new job variant cost more than reusing upgrade models, but prevent operation substitution. WinGet/installer behavior may make some or all combinations non-actionable until proved. No production route, deployment, signing or distribution gate is closed here.

## Follow-up controls and tests

- Tasks 47/55: owner-approved Q1/Q5 bounds and lifecycle, approved catalogue route and source-consent matrix; hostile/oversize/fuzzy/duplicate metadata, unavailable fields, tags-only and Store information-only fixtures.
- Tasks 56–58: fresh complete absence versus denied/stale/mixed inventory; install/update substitution; source/version/scope/architecture drift; user-to-machine fallback rejection; immutable mode; sequential cancellation/partial outcomes and verified recent-install derivation. Review actual provider capabilities using current primary documentation at implementation time.
- Tasks 57–59 and ADR-025: independent helper validation, protocol downgrade/replay, deployment allowlist and security review, then canonical build/full suite. Only separately approved exact-artifact Windows host evidence can establish each supported user/machine route.
- D3 `7zip.7zip` is a candidate only. Before any real install, obtain a fresh absence baseline, enforceable exact version/scope/architecture, reviewed integrations/associations and separate action-time approval. An existing relied-on installation is not a disposable fixture; removal needs another exact approval. No VM requirement is introduced against the owner's host-testing direction.

## Reconsider when

Supported WinGet APIs cannot enforce the reviewed target or scope, dependencies/effects cannot stay within approval, absence cannot be established, or source/capability behavior changes. Keep the route blocked; broadening installer authority requires another owner decision.
