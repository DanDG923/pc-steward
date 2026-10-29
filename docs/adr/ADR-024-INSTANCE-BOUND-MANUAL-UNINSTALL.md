# ADR-024 — Instance-bound manually approved uninstall

**Status:** Accepted

**Date:** 2026-10-29

**Owner acceptance:** 2026-10-29 — **“Accept the corrected ADRs and final choices”** accepts the corrected single-instance contract and disclosed final N1–N6 choices following both Opus 5.5 reviews. This includes ADR-025's 30-minute review dispatch ceiling, separate two-minute helper-request freshness, exclusive cross-session gate and new install/uninstall 10-minute deadline plus at most five seconds teardown attempt. Q4 route/exclusion/disposable-fixture proof and exact live removal authority remain prerequisites; no route is declared supported here.

**Decision owners:** Product owner, implementation reviewer and security reviewer

**Related requirements:** `FR-UNI-01–04`, `FR-DAT-06–07/12–15`, `FR-HIS-01–06`, `TM-03–04/07/13`

**Related decisions:** Q3 warning coverage, Q5 operation-history defaults and final review/gate/deadline choices are owner-Accepted in Task 47; Q10 ADR acceptance is closed. Q4 enforceable targeting/exclusions/fixture and Q9 exact host authority remain open.

## Context

[Specification §§9.15/11.5](../product/PC_STEWARD_PRODUCT_SPEC.md) permits one manually confirmed removal of a freshly resolved installed instance. One catalogue ID may correspond to multiple users, scopes, architectures or registrations. Existing inventory display and update mapping are insufficient removal authority. A registry uninstall string is untrusted data, and exact package ID matching alone cannot prove which instance a provider removes.

This accepted contract complements [ADR-025](ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md). Its narrow additive extensions to ADR-003/017/018/022 require implementation/evidence alignment; their baseline statuses and current update execution stay unchanged. Earlier [project recovery](../review/TASK_45_PROJECT_RESTORE.md) closed the planning tasks' build/test gate without proving a removal capability. No Windows removal API or package is claimed safe on this host from static inspection.

## Decision drivers

Wrong-instance removal can destroy working software or affect another user. Unknown safety, dependencies, targeting or privilege must block. Preserve other registrations and user documents; no bulk/automatic removal, registry-command execution, force close, leftover cleanup or rollback guarantee is permitted.

## Options considered

| Option | Benefits | Costs and risks |
|---|---|---|
| A — Exact provider-enforceable instance removal with explicit exclusion proof | Aligns review, operation and postcondition; permits independently gated routes. | Coverage can be small; supported API and host evidence are prerequisites. |
| B — Exact WinGet catalogue ID is sufficient | Simple UI and broad apparent coverage. | Product identity is not installed-instance authority; duplicate registrations can be removed incorrectly. Reject. |
| C — Run ARP uninstall strings, scripts or generic cleanup | Reaches otherwise unsupported software. | Untrusted command execution and uncontrolled deletion violate product boundaries. Reject. |
| D — Defer every uninstall route | Retains the present safe boundary. | No WS4 feature; required fallback whenever A cannot be proved for a route. |

## Accepted decision

Accept A with D as the mandatory fallback for each unsupported route. Neither a provider name nor an exact ID enables Uninstall by itself.

### Route eligibility

| Route | Required proof before review becomes actionable | Fail-closed cases |
|---|---|---|
| WinGet instance-bound removal | A supported adapter resolves a fresh unique installed registration and can enforce that same provider identity, instance discriminator, scope, architecture and applicable user without widening selection. | ID-only CLI selection, unknown/duplicate scope or registrations, table/display authority, inability to bind the selected instance. |
| Current-user MSIX registration removal | A supported adapter binds the exact installed package registration to the requesting user and proves it is removable, non-protected, non-system, non-framework and not a dependency; dependency/provisioning classification is complete. | All-user/provisioned packages, frameworks, dependencies, protected/system packages, unknown dependency/safety status or a route affecting other users. |
| Other provider/ARP/custom script/leftovers | None in this scope. | Always unavailable. Chocolatey update approval does not confer uninstall authority. |

These are required contracts, not claims that current WinGet or MSIX APIs provide all the proof. Q4 must identify the actual supported calls/typed targeting fields and exclusion evidence from current primary documentation, fixtures and separately authorized host observations. If a necessary field cannot be enforced, keep that route blocked/deferred; do not replace it with a name, product code heuristic, scope guess or registry command. MSIX may use its supported removal API for a current-user registration; the WinGet-only update-provider rule is not a ban on this explicitly specified manual removal route.

Additionally exclude PC Steward itself, Microsoft.DesktopAppInstaller/WinGet, the Windows App SDK/runtime dependencies used by the desktop, the constrained Chocolatey client and other runtime/provider components PC Steward needs for execution or verification. Use a compiled exact provider/native-identity exclusion set plus evidence-backed dependency classification; display-name matching is not proof. Unknown identity/dependency classification blocks. The implementation must enumerate its exact deployed dependency identities before a route is supported; a finite name list alone is insufficient.

### Review and execution

Resolve the selected instance again before review. Bind operation **Uninstall**, provider/source where applicable, exact registration/package identity, installed version, scope, architecture, applicable user, exclusion-evidence identity/freshness, approval time and job/correlation ID. The typed uninstall payload has no install candidate version, arbitrary path, URL or arguments. Review identifies one instance and expected privilege, warns about detected use and known data/settings impacts, and offers cancel or manual closure with explicit continuation. Unknown process-detection coverage is disclosed; no guarantee that the app is unused, no forced target kill and no use of continuation to waive targeting/exclusion checks.

Immediately before invocation, the desktop rechecks live review/source consent and the runner (and, for helper routes, the helper) freshly resolves operation-specific targeting/exclusion evidence under ADR-025's responsibility matrix. A helper queries the current instance set itself and compares the request baseline as untrusted data, never proof. Drift, loss of visibility, ambiguity or contradictory dependency blocks. A machine-scope WinGet route uses only the reviewed uninstall discriminator in the protected one-shot helper. Current-user MSIX runs unelevated with no all-user/UAC fallback. For user-scope WinGet, PC Steward never requests elevation; a third-party uninstaller can initiate UAC and effects outside app control. Disclose that risk, expose only evidenced scope/modes, and label observed scope/instance drift Unsafe/Unknown rather than Removed. Scans cannot create approval. No mode is a free-form flag. Explicitly warn that removing a current-user MSIX registration may delete that package's app data/settings; this does not authorize PC Steward to delete documents or leftovers.

Only one manual target is approved. Cross-operation replay or reusing the request for another instance fails under ADR-025. After a crash or uncertain provider invocation, inspect state and require new review for another attempt; do not replay removal automatically. Cancellation prevents any unstarted action but cannot promise undo of installer effects already performed.

Commit a unique DispatchClaimed state before invocation/helper-UAC launch under ADR-025's lease. After any exit/crash, claimed-without-final is Uncertain, never not started; read-only re-inventory is separate from a new removal action. Uninstall is one manual target, never a queue. Apply ADR-025's accepted 30-minute review-to-dispatch ceiling and unchanged two-minute helper-request admission lifetime separately; waiting on a scan/in-use warning does not silently renew either. Fresh targeted evidence and live review binding are required at dispatch; expiry needs new review.

### Verification and lifecycle

Re-inventory all covered relevant registration views and compare against the pre-action baseline, including provider identity and possible linked/unlinked instance matches. Report **Removed** only when the selected registration/identity is absent in a successful complete targeted query, no ambiguous surviving match can be that target and all other baseline instances remain. Missing user/scope/architecture visibility, failed/denied/partial queries are **Unknown**, never absence. A remaining target is **Still present** unless reliable provider evidence supports **Restart pending**; a restart flag alone is not verified absence. Changed/missing other instances produces explicit partial/unsafe outcome, never blanket Success. No automatic reinstall, rollback or deletion of documents/caches/leftovers follows.

Append self-contained sanitized operation-labelled evidence to a distinct uninstall history category, without foreign keys that let scan clear erase it. Apply the owner-accepted [Task 47 Q5 policy](../requirements/V2_PLANNING_DECISIONS.md), including capacity, explicit selected operation-history clear, frozen diagnostic-copy preservation/disclosure and full reset ownership. Removal history does not authorize deletion of unrelated history/preferences/files/OS records. FailedUpdate AI provenance remains update-only; no install/removal AI explanation scope is added.

## Consequences

The selected-instance contract may exclude packages a normal package manager could remove. This is deliberate: capability absence is a visible product outcome. A before/after comparison can detect collateral registration changes but cannot undo or prove every third-party uninstaller effect, so review and host-fixture selection remain essential.

## Follow-up controls and tests

- Tasks 47/60: owner-reviewed Q4 targeting/exclusion evidence matrix and viable disposable fixture; define each route independently. Unresolved route feasibility blocks that route, not the existing updater.
- Tasks 60–62: duplicate/mixed-user/mixed-scope/mixed-architecture instances; stale/empty/denied inventory; exact-ID but unenforceable selection; protected/provisioned/framework/dependency MSIX; unknown classification; dependency race; hostile ARP strings; wrong user, replay and update/install/uninstall substitution. Confirm no force-close or leftover-deletion path.
- Tasks 61–63: UAC denial, helper spoofing, changed pre-execution registration, cancellation/crash, exit-zero/target-still-present, failed inventory, other-instance disappearance and reliable versus fabricated restart evidence. Security review plus canonical build/full suite precede exact-artifact host evidence.
- `7zip.7zip` remains only D3's selected candidate. A later approved installation must first be verified as the disposable test instance; removal requires fresh exact owner approval. Never remove an existing working installation to manufacture a fixture. No host package query, install or removal is authorized here.

## Reconsider when

Provider/API changes permit or invalidate enforceable instance targeting; exclusion evidence is incomplete; or host evidence exposes collateral removal. Keep the affected route unavailable until a new reviewed decision resolves it.
