# Phase 2 review — repeated gate after Task 90

**Phase:** Phase 2 — Update intelligence
**Base commit:** `ea292b1033db5481656a7d49b64cf19772bfedcd`
**Head commit:** `561143944178ca36581a52b3b437c1869f324a9b`
**Reviewer model/effort:** GPT-5.6 Sol / High
**Date:** 2026-07-28

## Gate result

`PASS` — with the product-owner-approved ADR-003 real-row evidence exception recorded in the final retest below.

The final retest closes `P2R-07`–`P2R-10`, accepts the elevated event-based production trace for `AC-DAT-04`, and finds no remaining actionable false positive, Blocker/High finding, or unproven mandatory Phase 2 acceptance criterion. The historical failures and remediations remain below for auditability.

## Evidence reviewed

- Requirements and acceptance criteria: `docs/requirements/PHASE_2.md`, the Task 16–20 prompts, the trusted-source policy and matching acceptance criteria in `docs/product/PC_STEWARD_PRODUCT_SPEC.md`.
- ADRs: Provisional ADR-003 and Accepted ADR-006, ADR-014, ADR-015, ADR-016, ADR-017 and ADR-021.
- Complete diff: `ea292b1033db5481656a7d49b64cf19772bfedcd...ce0cfaef28d5617a99e46307f9dbba0fbd8b588a` (40 files, 1,916 insertions and 165 deletions).
- Task 90 evidence: `docs/review/TASK_90_P2R_EVIDENCE.md`, including the approved 20-run live timing aggregate, process-scoped connection metadata and desktop observation.
- Focused Phase 2 suites: 66 passed — 33 Domain and 33 Application integration.
- Release build: zero warnings and zero errors.
- Full suite: 89 passed — Domain 37, Application integration 48, Architecture 3 and Security fixture 1.
- Fixture benchmark: 30 local runs at 1.78 ms median / 1.97 ms p95; 20 two-source fixture refreshes at 15.41 ms median / 15.96 ms p95.
- Read-only built-assembly reproductions: wrong-source same-ID mapping returned `High`, `IsActionable=true`, no review reason; x64+x86 product projected as x64 and an x64 candidate returned `IsActionable=true`, no review reason.
- No new live source or machine-state-changing exercise was performed in this review.

## Findings

| ID | Severity | Requirement | File/line | Finding | Required remediation | Verification |
|---|---|---|---|---|---|---|
| `P2R-07` | High | `FR-ID-02–04`, `AC-MAT-03`, trusted-source policy §10.4 | `src/PCSteward.Desktop/AdditionalLocalInventoryProviders.cs:45`; `src/PCSteward.Application/InventoryWorkflow.cs:130–132`; `src/PCSteward.Domain/ProductPackageMatching.cs:188–191` | The installed WinGet source is stored in `InstallationInstance.Channel`, but `CreateProfiles` carries only the package ID into `ProviderPackageId` and the matching engine treats that ID as globally unique. A product recorded from `msstore` and a candidate from `winget` with the same ID therefore produce a High-confidence actionable automatic mapping. Source and native package ID are a compound provider target; omitting the source permits the wrong package/source pair. | Preserve installed source separately from channel, carry it into the match profile, and require the exact installed source plus package ID for automatic provider-ID confidence. Conflicting or missing source evidence must be review-only. | Add domain and composed-workflow regressions where the same package ID exists in `winget` and `msstore`; only the source-qualified identity may map and the opposite source must never be actionable. |
| `P2R-08` | High | `AC-MAT-04`, product risk “Multiple installations of same app” | `src/PCSteward.Application/InventoryWorkflow.cs:123–133` | `CreateProfiles` reduces every canonical product to one `preferred` installation and copies only that instance's version, scope, architecture and channel. Other instances are ignored. The reproduction supplied x64 and x86 instances for one exact WinGet identity; the profile became x64 and an x64 candidate remained actionable with no ambiguity reason. This can target the wrong install instance and makes the result depend on instance ordering. | Assess every retained installation instance or fail closed when their version/scope/architecture/channel evidence differs. A product-level candidate must not be actionable until the provider-specific target instance is unambiguous. | Add composed tests for x86+x64, user+machine, stable+preview and differing installed versions in both input orders; all ambiguous cases must be non-actionable and explain the conflict. |
| `P2R-09` | Medium | `FR-SRC-03`, Task 90 persisted-disclosure claim | `src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs:117–126`; `src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs:252` | `IsWinget` recognizes only `WinGet installed inventory`. Approval for `WinGet package sources` is therefore never written. On the next launch `InventoryWorkflow.LoadAsync` sees approval as false and disables the source connector, so the documented persisted approval does not survive restart. This fails closed but leaves the production source-check journey incomplete. | Apply the disclosure setting policy to both named WinGet connectors without broad string matching, and retain the user's approved state until they revoke it. | Save, close, reopen and load both connector states; assert enabled/approved persistence and assert a false approval can never restore an enabled connector. |
| `P2R-10` | Medium | Trusted-source policy §10.4, `FR-UI-04–05`, Phase 2 traceability | `src/PCSteward.Domain/UpdateIntelligence.cs:14–36`; `src/PCSteward.Domain/ProductPackageMatching.cs:112–120`; `docs/requirements/TRACEABILITY.md:26` | Source disagreement is still collapsed into one selected mapping or an undifferentiated `Unmanaged` result; the model cannot display both source results and timestamps as required. No source-disagreement test exists, although the Task 18 traceability row says disagreement fixtures passed. | Preserve competing source candidates in the assessment/presentation model, show their source-specific timestamps, block automatic selection when source/channel/architecture/publisher differs, and correct traceability to cite exact executed tests. | Add live+live and live+cached disagreement tests for different versions and conflicting metadata; verify both results remain visible, neither is silently selected, and traceability names the regressions. |

## Missing evidence

- `AC-DAT-04` remains unproven. The recorded observation ran both sources, saw no socket for the short WinGet check and one unclassified TLS destination for Store, and contains no disabled-source control. It cannot confirm that only enabled connector destinations are contacted.
- ADR-003 remains Provisional. The approved live runs discarded provider output, so real WinGet/Store parser, locale, capability and source-column behavior is still unexercised.
- The canonical fixture test is green, but it does not contain the wrong-source collision or multi-instance ambiguity reproduced by this review.

## Positive controls confirmed

- Numeric, prerelease, downgrade, malformed and connector-deferred versions have deterministic non-lexical outcomes.
- Current tests keep low-confidence, explicit architecture/channel conflicts and incomparable versions non-actionable.
- The checked-in 320-record catalog, its five mapping controls and all 20 named edges are loaded by the curated test; its declared negative controls produce zero actionable mappings.
- Source refresh remains bounded and independent; cached results retain timestamps and stale/unavailable labels.
- Standard output and error are bounded while read, and overflow terminates the process tree.
- The desktop exposes no update-execution action; manual mappings target an exact package present in a live/cached fixed-source snapshot and remain reversible.

## Residual risks accepted by product owner

ADR-021 makes `AC-UX-01–09` Optional — best effort, so the incomplete keyboard exercise is not a Phase 2 gate failure. The accepted live-source timing supports the 90-second target on this machine and date but does not remove the fixed-baseline limitation recorded by ADR-006.

## Recommended next action

Run Task 90 to remediate `P2R-07`–`P2R-10` and the missing mandatory evidence, then repeat Task 20 in a fresh GPT-5.6 Sol / High review. Do not merge Phase 2 or begin Phase 3.

## Fresh Task 20 review — 2026-07-28

**Base commit:** `7fe0e45`
**Head commit:** `2ebd713`
**Reviewer model/effort:** GPT-5.6 Sol / High
**Gate result:** `FAIL`

`P2R-07`–`P2R-09` passed. `P2R-10` failed because a mapping marked with `SourceDisagreement` could be non-actionable yet still receive the `Current` status when its selected version compared Equal, excluding it from `AttentionNeeded`. The subsequent local remediation is not part of this review and requires a fresh review.

The approved live calls verified fixed arguments, version/capability detection, and the no-upgrade path for both sources, but both produced zero candidates. `AC-DAT-04` still lacks an event-based, process-attributed disabled/local-only versus explicitly enabled production trace with no dropped events. ADR-003 still lacks a documented sanitized real update row that exercises production field and source-column validation. Focused review tests: **6 passed**; Release build: **0 warnings, 0 errors**; full suite: **94 passed**. Do not merge Phase 2 or begin Phase 3.

## Final Task 20 retest — 2026-07-28

**Base commit:** `ea292b1033db5481656a7d49b64cf19772bfedcd`
**Head commit:** `561143944178ca36581a52b3b437c1869f324a9b`
**Reviewer model/effort:** GPT-5.6 Sol / High
**Gate result:** `PASS`

No new actionable finding was identified. `P2R-07`–`P2R-10` are closed: exact installed-source identity is retained, conflicting installation targets fail closed, both source disclosures persist and fail closed on revocation, and source disagreement is always review-required even when the selected version compares Equal. The UI exposes no update-execution action.

The approved elevated ETW evidence in `docs/review/TASK_90_P2R_EVIDENCE.md` is sufficient for `AC-DAT-04`: the disabled/local-only production segment started no WinGet process and emitted no PC Steward remote-endpoint event; the explicitly approved production source check produced process-attributed WinGet TLS endpoint events; the disclosures were revoked afterward; and the trace reported zero lost events.

Focused review tests: **8 passed** — five Domain and three Application integration. Release build: **0 warnings, 0 errors**. Full Release suite: **95 passed** — Domain 41, Application integration 50, Architecture 3 and Security fixture 1. Fixture benchmark: 30 local runs at **2.13 ms median / 2.74 ms p95** and 20 two-source fixture refreshes at **15.39 ms median / 15.76 ms p95**. No live source was contacted by this retest.

### Approved ADR-003 exception

The product owner accepts deferral of ADR-003's sanitized real update-row and source-column evidence until Phase 3 because three approved read-only probes on this controlled host returned zero update rows and no update is currently available to exercise. ADR-003 remains **Provisional**; this exception does not claim real-row parsing is proven, does not make WinGet table output execution authority, and does not relax the fixed executable/source/argument, bounded-output, validation, or fail-closed controls. Capture the provenance-recorded sanitized row when an update is available during Phase 3 and reassess ADR-003 there.

Phase 2 may proceed to branch integration. Begin Task 21 only after the Phase 2 branch is merged.
