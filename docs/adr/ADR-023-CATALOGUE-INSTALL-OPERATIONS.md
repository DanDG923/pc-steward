# ADR-023 — Catalogue search and explicitly approved install operations

**Status:** Proposed

**Date:** 2026-10-29

**Decision owners:** Product owner, implementation reviewer and security reviewer

**Related requirements:** `FR-CAT-01–06`, `FR-SRC-02/07–10`, `FR-UPD-15`, `FR-HIS-01–06`, `FR-DAT-06–07/12–15`

**Related open decisions:** Task 43 Q1 (search bounds), Q5 (local-state lifecycle), Q9 (exact host authority), Q10 (ADR acceptance)

## Context

[Specification §§9.14/11.5](../product/PC_STEWARD_PRODUCT_SPEC.md) accepts manual catalogue installation as gated 2.0 scope. The existing [ADR-003](ADR-003-WINGET-INTEGRATION-ROUTE.md) authority proves an upgrade for an installed product; it cannot establish absence or authorize installation. [ADR-017](ADR-017-IMMUTABLE-UPDATE-JOBS.md) and the current helper remain update-only. This proposal is paired with [ADR-025](ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md); neither changes those effective contracts until owner acceptance and reviewed implementation.

Task 45 began against Task 44's accepted working-tree requirements under the owner's instruction to start. The subsequent [project recovery](../review/TASK_45_PROJECT_RESTORE.md) closes build/test validation without accepting this ADR or implementing installation. No current WinGet install API, scope-enforcement capability or host installability is asserted by this ADR.

## Decision drivers

Search is discovery, never execution authority. Installation needs a fresh not-installed baseline and a separately reviewed exact operation. Unknown source, version, scope, architecture, channel compatibility or existing-instance ambiguity must block. User scope alone does not sandbox third-party installer effects.

## Options considered

| Option | Benefits | Costs and risks |
|---|---|---|
| A — Bounded COM discovery, separate typed install approval and adapter | Reuses source consent and domain separation; distinguishes discovery from action; enforces per-scope capability gates. | Requires a new absence/target authority and capability evidence; some results remain information-only. |
| B — Treat a search result as an update with an empty installed version | Small initial implementation. | Confuses update/install approval, weakens existing update invariants and can repair/upgrade an existing instance. Reject. |
| C — Open an arbitrary installer link or execute catalogue commands | Broad apparent coverage. | Violates source, URL, process and privilege boundaries. Reject. |

## Proposed decision

Recommend A. The owner must accept the contract; implementation still waits for the gates below.

### Discovery and authority

1. Query only enabled, explicitly consented, predefined approved WinGet catalogues through the supported COM adapter. Keep Microsoft Store information-only. Do not promote Chocolatey, mirrors, custom repositories, arbitrary URLs or fuzzy/display matches to install authority.
2. Search returns bounded data: name, publisher, exact package ID, source and version, plus scope/architecture where actually known. Display missing fields as unavailable. Label tags as tags (D1); do not infer categories, sizes or icons or fetch remote icons as an unreviewed network path. Escape hostile metadata in the existing Task 35 components.
3. Q1 must fix query/result count, input/field lengths, total response bytes, timeout, cancellation and concurrency bounds in the phase brief before implementation. Overflow, duplicate or ambiguous exact candidates confer no authority. The update authority's existing bounds are not evidence that catalogue search is bounded.
4. Selecting Install invokes a separate fresh structured exact-source/ID/version authority and a complete relevant local inventory. Establish that no matching installed instance is present in any relevant scope/architecture/user view available to the supported route. Denied, partial, stale or conflicting inventory is unknown, not absence. An existing instance routes back to inventory/update review, never an implicit repair or upgrade.
5. The adapter must prove the selected version, architecture, scope and applicable channel can be enforced without fallback. Unknown or unsupported combinations are disabled with an explanation. Installer type, dependencies, agreements and material effects must be surfaced when known; unresolved effects that could violate the reviewed contract block rather than receive silent acceptance.

### Review, execution and verification

Review shows **Install**, exact source/ID/version/architecture/scope, expected privilege, enforceable mode if offered, freshness and known installer effects. A new immutable install payload binds those values, the absence-evidence identity and timestamp, applicable user, job/correlation ID and approval time under ADR-025. It contains no executable path, URL or arguments. Later evidence changes require new review; a queue row cannot retarget the job.

Immediately before each launch, revalidate the reviewed identity, source consent, absence, capabilities and scope. User installs run unelevated; unexpected elevation is blocked/reported, never retried as machine scope. Machine installs require their own scope-bound confirmation and the protected one-shot helper's independent operation/identity validation. An old protected update deployment does not gain install support from this proposal.

Run reviewed items sequentially. Each has independent approval/evidence and a final result; cancellation stops the next unstarted item. Provider progress is only as precise as observable evidence. Do not force-close applications, promise rollback, automatically uninstall on failure, or retry an uncertain installation after a crash.

Post-inventory must find the expected version in the reviewed scope/architecture/user with an unambiguous stable instance association. Provider exit zero is insufficient. Report verified installed, failed/still absent, reliable restart pending, or unknown/partial as evidence warrants. A restart result is not a verified installation or a recent-install entry until the expected installed state is established. Verification does not claim to prove every installer side effect.

### Proposed persistence ownership — Q5 acceptance required

Keep favourites as reversible local catalogue-ID/source preferences, separate from installed inventory and curated membership. Recent installs are a view of verified PC Steward install outcomes, never search clicks, queued attempts or pre-existing applications. Recommend preserving favourites and operation history across Clear scan data and Clear downloaded files; any dedicated history clear removes its derived recent view, and Factory reset removes both app-owned categories. Existing update history retains its current lifecycle. The owner must settle retention and dedicated-clear semantics in Q5 before schema work; this recommendation establishes no retention default. ADR-015 migrations must preserve existing jobs/decisions and never turn restored rows into execution authority. Task 46 coordinates the shared lifecycle manifest without conflating these records with 30-day diagnostic snapshots.

## Consequences

Installation remains a narrow manual capability with honest unsupported results. Separate absence/target evidence and a new job variant cost more than reusing upgrade models, but prevent operation substitution. WinGet/installer behavior may make some or all combinations non-actionable until proved. No production route, deployment, signing or distribution gate is closed here.

## Follow-up controls and tests

- Tasks 47/55: owner-approved Q1/Q5 bounds and lifecycle, approved catalogue route and source-consent matrix; hostile/oversize/fuzzy/duplicate metadata, unavailable fields, tags-only and Store information-only fixtures.
- Tasks 56–58: fresh complete absence versus denied/stale/mixed inventory; install/update substitution; source/version/scope/architecture drift; user-to-machine fallback rejection; immutable mode; sequential cancellation/partial outcomes and verified recent-install derivation. Review actual provider capabilities using current primary documentation at implementation time.
- Tasks 57–59 and ADR-025: independent helper validation, protocol downgrade/replay, deployment allowlist and security review, then canonical build/full suite. Only separately approved exact-artifact Windows host evidence can establish each supported user/machine route.
- D3 `7zip.7zip` is a candidate only. Before any real install, obtain a fresh absence baseline, enforceable exact version/scope/architecture, reviewed integrations/associations and separate action-time approval. An existing relied-on installation is not a disposable fixture; removal needs another exact approval. No VM requirement is introduced against the owner's host-testing direction.

## Reconsider when

Supported WinGet APIs cannot enforce the reviewed target or scope, dependencies/effects cannot stay within approval, absence cannot be established, or source/capability behavior changes. Keep the route blocked; broadening installer authority requires another owner decision.
