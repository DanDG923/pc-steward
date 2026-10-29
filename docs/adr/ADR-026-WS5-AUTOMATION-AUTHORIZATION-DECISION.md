# ADR-026 — WS5 durable automation authorization decision

**Status:** Accepted — Option C (Defer)

**Date:** 2026-10-29

**Owner acceptance:** 2026-10-29 — **“Accept the corrected ADRs and final choices”** explicitly accepts Option C (Defer) following both Opus 5.5 reviews and the disclosed final corrections. FR-UPD-12 stays Later; per-run approval and scan-only scheduling remain. No durable unattended authorization, signed-out/S4U/password task or implementation is approved.

**Decision owners:** Product owner, implementation reviewer and security reviewer

**Related requirements:** `FR-UPD-01/12`, `FR-SCH-01–05`, `FR-RSC-04`, `NFR-PERF-07`, `AC-NFR-11`, `FR-DAT-12–15`, `TM-03–04/07/13`

**Related open decision:** Owner choice: accept a future automation design, reject WS5, or defer it

## Context

The [accepted roadmap WS5](../product/PC_STEWARD_2_ROADMAP.md) asks for a decision on allowlisted user-scope automatic updates. [Specification §§4.3/9.7/11.5](../product/PC_STEWARD_PRODUCT_SPEC.md) retains `FR-UPD-12` as Later, per-run confirmation and scan-only scheduling pending that decision. [ADR-017](ADR-017-IMMUTABLE-UPDATE-JOBS.md) approves one reviewed job; [ADR-020](ADR-020-SCHEDULING-WITHOUT-A-RESIDENT-PROCESS.md) structurally excludes execution from ScheduledHost. Durable consent would change authorization lifetime even without elevation. Subsequent [project recovery](../review/TASK_45_PROJECT_RESTORE.md) closes the planning tasks' build/test gate without deciding WS5.

Current source separation is visible in [ScheduledHost's project references](../../src/PCSteward.ScheduledHost/PCSteward.ScheduledHost.csproj); it is not proof of a future automated-execution architecture. Existing curated membership, AutomaticUpdates settings, schedule/source consent, old approved jobs or SQLite rows must not be treated as sufficient durable execution authority.

## Decision drivers

Preserve review, least privilege, revocation, bounded resource use and honest installer limits. A package described as user-scope can still modify user files/settings, start processes, register startup behavior or attempt UAC. Non-elevated execution does not confine third-party code to the intended update or make unattended interaction safe.

## Options considered

| Option | Consequence | Assessment |
|---|---|---|
| A — Adopt a future durable authorization architecture | Requires explicit requirement/ADR-015/017/020 amendments, new consent integrity and executor design, security review and exact-artifact host evidence. No immediate code or schedule authority. | Potential future convenience; material unresolved consent/revocation/installer risks. |
| B — Reject WS5 | Keep per-run review and scan-only scheduling as the intended product direction; owner must record rejection in the roadmap/spec decision process. | Smallest security surface; permanently gives up this convenience unless reopened. |
| C — Defer WS5 | Keep existing behavior and Later status; revisit only with a concrete safe authorization and capability proposal. | Recommended while operation/security work and the controls below remain unproven. |

## Accepted decision

**Owner decision: C — Defer, Accepted.** The owner explicitly accepted deferral in Task 47; it is not inferred from starting Task 45 or the general roadmap. This records deferral and does not enable automation. Reopening A requires a revised concrete design and explicit acceptance of its requirement/ADR amendments before implementation; choosing B would require a new recorded owner rejection.

Under accepted C (Defer), `FR-UPD-01` still requires per-run approval; `FR-UPD-12` remains Later; `FR-SCH-03` and ADR-020 remain scan-only. Do not add execution references, helper access, a new execution scheduler, resident coordinator, credential, task registration or code in this documentation task. Existing manual updates remain independently available.

Option A is a request to reopen design, not an adoptable implementation contract in this ADR. This record can presently accept C (Defer) or B (Reject); adopting automation needs a revised concrete owner-accepted design first. No signed-out, S4U or password-based task is promoted, including if later user-scope automation is reconsidered.

## Required contract if adoption is reconsidered

| Area | Required future decision and proof before implementation |
|---|---|
| Durable grant | A separate explicit review binds only Update, exact approved provider/source/native package identity and installed-instance association, requesting user, user scope, architecture/channel, grant ID/version and expiry/revocation policy. Install, uninstall, machine scope and cross-user operations are excluded. Membership/source/schedule consent is insufficient. |
| Optional version ceiling | Review shows an exact enforceable ceiling or explicitly discloses no ceiling. Define comparison/channel/major-version semantics; incomparable versions block. An omitted ceiling is an intentional reviewed choice, never an unbounded default from a missing/corrupt field. Each execution still binds a fresh exact target version. |
| Integrity and trust limits | SQLite/digests alone cannot authenticate durable consent. Specify the actual protected authorization store and threat model, including same-user tampering, key access, record replay, downgrade and backup restoration. DPAPI/HMAC labels alone do not prove same-user forgery or rollback resistance. If integrity cannot meet the accepted threat model, defer; never introduce a privileged service as a workaround. |
| Freshness | Re-resolve trusted exact source/identity/version, applicable user, installed instance, enforceable user scope, supported mode and compatibility before each action. Source revocation, stale/partial evidence, scope/channel drift or unknown installer effects block. No table/fuzzy/AI/legacy-job authority. |
| Revocation and reset | Serialize revocation against dispatch using grant generation and an atomic consumption check. Define the dispatch point and disclose that revocation stops pending work but cannot undo effects already underway. Verify revocation at launch, after any waits and on restart. Reset/removal invalidates all grants and execution tasks; restoring an old database/backup cannot resurrect consent. Residual invalidation failure is visible and blocks execution. |
| Windows and overlap | Owner-reviewed maintenance windows/time zone/DST/missed-run rules; one compatible operation at a time; no wake/catch-up outside consent or execution while interactive review/reset is active. Do not turn ScheduledHost into an executor implicitly; any separate non-resident standard-user host/task needs explicit architecture and requirement amendments. |
| Retry/back-off | Bounded attempts/back-off/timeouts and per-package stop conditions; no automatic replay after uncertain invocation. Reconcile post-state before any new action, prevent duplicate launches across hosts/crashes and surface repeated failure. Numeric policy needs owner acceptance, not ad hoc implementation defaults. |
| Notifications and audit | Show grant scope/ceiling, pending/in-progress/result and actionable blocked/revoked states; controls to disable execution separately from scans/notifications. Persist bounded provenance and per-attempt outcome; expose uncertainty and reliable restart pending without false completion. Notifications do not create consent. |
| Installer limits | Prove noninteractive user-scope enforcement and reject UAC, machine fallback, unknown interaction or in-use risks. Define bounded cancellation/process-tree handling without killing the target app or implying rollback. Explain third-party installer effects that user scope does not prevent; if these exceed owner consent, require interactive review. |
| Lifecycle and deployment | Coordinate ADR-015/017/020 and fixed lifecycle manifests; exact operation/capability manifests, protected-binary checks where applicable, current-user task removal, no password or resident/elevated component. No signer/distribution exception is extended. |

## Consequences

Deferral keeps current safety and performance contracts and delays unattended convenience. This decision cannot be resolved by merely adding an AutomaticUpdates switch or signing a consent row. The future adoption burden includes honest limits of same-user integrity and third-party installers, not just functional scheduling tests.

## Follow-up controls and tests

If the owner chooses C, record that acceptance and leave implementation deferred. If B, reconcile the roadmap decision without deleting historical requirements. If A is requested, first produce and accept the concrete controls above and narrowly amend the cited requirements/ADRs; Task 47 may then bound a separate implementation/evidence sequence. The current outline contains no implicit automation implementation authorization.

Any adopted design must test grant/source/package/user/scope/version substitution, storage tampering and restored backups, expired/revoked grants, reset/revocation dispatch races, uncertain crash windows, parallel hosts, DST/window edges, bounded retry/notifications, UAC/interaction rejection and cancellation teardown. Require security review and separately authorized exact-artifact host evidence; mocks alone cannot establish unattended installer safety. Task 45 performs no automation experiment or real schedule mutation.

## Reconsider when

The owner wants to reopen WS5 and an enforceable authorization, revocation, installer and non-resident execution design can be reviewed against the retained safety boundaries. Lack of such evidence keeps automation Later.
