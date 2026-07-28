# Phase 2 review — initial gate

**Phase:** Phase 2 — Update intelligence
**Base commit:** `ea292b1033db5481656a7d49b64cf19772bfedcd`
**Head commit:** `92eaf13cd0e12e6207ee218f8bd71785f62378cf`
**Reviewer model/effort:** GPT-5.6 Sol / High
**Date:** 2026-07-28

## Gate result

`FAIL`

The deterministic engines and focused tests are green, but Phase 2 is not a production update-intelligence vertical slice. Four High findings remain, including stale source state that can be presented as live and mandatory acceptance criteria with no end-to-end or canonical-fixture proof. The gate cannot pass with a High finding or an unproven mandatory acceptance criterion.

## Evidence reviewed

- Requirements and acceptance criteria: `docs/requirements/PHASE_2.md`, the Task 16–20 prompts, and the cited authoritative product requirements.
- ADRs: Provisional ADR-003 and Accepted ADR-014, ADR-015 and ADR-016.
- Complete diff: `ea292b1033db5481656a7d49b64cf19772bfedcd...92eaf13cd0e12e6207ee218f8bd71785f62378cf` (25 files, 1,291 insertions and 31 deletions).
- Focused deterministic suites: 32 Domain tests passed; 27 Application integration tests passed.
- Release build: zero warnings and zero errors.
- Full suite: 82 passed — Domain 36, Application integration 42, Architecture 3 and Security fixture 1.
- Fixture benchmark: 20 two-source runs, 15.32 ms median and 16.16 ms p95. This used in-memory fixture connectors only.
- Manual/isolated-environment evidence: none added; no real WinGet or Store source was contacted.

## Findings

| ID | Severity | Requirement | File/line | Finding | Required remediation | Verification |
|---|---|---|---|---|---|---|
| `P2R-01` | High | `FR-SRC-03–04`, `FR-UI-01/04–06`, `AC-MAT-07`, `AC-UX-01/05–06` | `src/PCSteward.Desktop/App.xaml.cs:17`; `src/PCSteward.Application/InventoryWorkflow.cs:47`; `src/PCSteward.Desktop/MainWindow.cs:69` | The production composition root creates only inventory providers. It never creates `WingetUpdateSourceConnector`, `UpdateSourceRefreshService`, product match profiles, or `UpdateIntelligence`; the workflow exposes no source-check or manual-mapping use case; and the desktop explicitly says live/cached checks are unavailable. Consequently a user cannot check sources, see real counts/details/failures, or create/delete a manual mapping. | Compose only the fixed approved sources behind explicit online-check consent, project inventory into bounded match profiles, load/cache intelligence, and expose create/review/delete mapping use cases without adding execution authority. | End-to-end application tests must start from persisted inventory, exercise live/cached/failure paths, and prove a keyboard user can identify an update and create/delete an exact trusted mapping. |
| `P2R-02` | High | `NFR-REL-04`, `AC-MAT-06`, `AC-UX-06` | `src/PCSteward.Application/InventoryExperienceViewModel.cs:79` | Freshness is derived from the first source alphabetically and copied into every product detail. With one live source and another cached/unavailable source, the UI can label cached results as live and omit the stale timestamp/source-unavailable warning. | Preserve freshness per source and derive each product detail from its mapping source; show a conservative aggregate plus every failing/stale source warning. | Add mixed-source tests for live+cached, live+unavailable, cached+unavailable and mappings from each source. |
| `P2R-03` | High | `FR-SRC-05`, `NFR-PERF-02` | `src/PCSteward.Provider.WinGet/WingetInstalledInventoryProvider.cs:111` | The WinGet process adapter reads stdout and stderr to completion before the connector checks the one-million-character limit. Source output is therefore unbounded in memory at the trust boundary, so the claimed output-size control does not resist resource exhaustion. | Bound both streams while reading, terminate the process tree on overflow, and return a typed output-too-large result without retaining the excess. | Add process-boundary tests for oversized stdout and stderr, timeout/cancellation, and successful bounded output. |
| `P2R-04` | High | `AC-MAT-03` | `tests/PCSteward.Domain.UnitTests/ProductPackageMatchingTests.cs:22`; `spikes/performance-fixtures/FIXTURE_CATALOG.md:1` | The zero-false-positive test constructs six bespoke candidates and does not load the checked-in 320-record catalog or its five `mappingCases`. The canonical mandatory acceptance fixture is therefore not exercised, and the zero-false-positive phase claim is unproven. | Add a test adapter for `fixtures/provider-records.json`, exercise all five declared mapping controls and relevant `EDGE-001`–`EDGE-020` records, and fail if any negative control becomes actionable. | Retain a machine-readable result recording total controls, true actionable controls and false-positive actionable mappings; the latter must be zero. |
| `P2R-05` | Medium | `FR-POL-01–05`, `FR-HIS-02/04`, `FR-UI-02/07` | `src/PCSteward.Desktop/MainWindow.cs:63`; `src/PCSteward.Desktop/MainWindow.cs:110`; `src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs:231` | Decision UX supports only current-version pin and indefinite ignore with no reason. It lacks source-package pin, ignore-until, mapping list/reversal, timeline display, cache-retention control and update-status filter choices; the stored timeline contains decisions only, not scans or mappings. | Complete the scoped decision controls and accessible timeline/settings views, exclude expired ignores from “active,” and implement the specified source-cache retention policy. | Add view-model, persistence and UI/accessibility tests for every decision type, expiry, reversal, scan/mapping timeline entries, retention and status filtering. |
| `P2R-06` | Medium | Phase 2 traceability | `docs/requirements/TRACEABILITY.md:26` | Traceability says output is bounded, stale results remain visibly stale, and disagreement/compatibility/offline fixtures passed. `P2R-02/03` contradict the first two claims, and no source-disagreement test is present. | Correct the evidence statement after remediation and link the exact tests/results rather than summarising unproved behavior. | Review the updated matrix against the new regression tests and repeat this gate. |

## Missing evidence

- `AC-DAT-04`: no approved network inspection proves that only enabled WinGet/Store connectors are contacted.
- `AC-NFR-02`: the 20-run result is an in-memory fixture latency measurement, not an end-to-end online source check; it does not establish the 90-second p95 requirement for the composed application.
- `AC-UX-02/04`: no keyboard, 150% text-scaling or high-contrast exercise covers the new Phase 2 controls; most required controls do not yet exist.
- Provisional ADR-003 still lacks an approved real-source parser/capability exercise, including Microsoft Store behavior.

## Task 90 remediation record

**Fixing commit:** `1fd152c` — `fix: remediate phase review findings`

**Verification owner/model:** Task 90, GPT-5.6 Terra / High
**Status:** code/test remediation completed; this historical review remains `FAIL` until a fresh Task 20 reviewer assesses the complete diff and the mandatory-evidence boundary.

| Finding | Remediation and regression verification |
|---|---|
| `P2R-01` | `App.xaml.cs` now composes fixed `winget` and `msstore` connectors into `InventoryWorkflow`; `MainWindow.cs` exposes the disclosure-gated source check and constrained create/delete mapping path. `InventoryWorkflowUpdateSourceTests.ComposedWorkflowUsesApprovedSourceOnlyWhenCheckedAndSupportsExactManualMappings` begins with persisted inventory and verifies local-only, live source metadata, and an exact mapping. |
| `P2R-02` | `InventoryExperienceViewModel` labels every candidate from its own source and preserves each source state in the aggregate. `MixedSourceFreshnessIsShownPerCandidateInsteadOfAsTheAlphabeticallyFirstSource` proves a cached Winget candidate is never presented as live merely because Microsoft Store is live. |
| `P2R-03` | `WindowsWingetProcess` bounds stdout and stderr while draining, marks overflow, and terminates the child process tree. `OversizedProcessOutputFailsClosedWithoutRetainingMoreThanTheBound` exercises over-one-million-character stdout and stderr at the process boundary; connector classification remains fail-closed. |
| `P2R-04` | `FixtureCatalogMatchingTests` loads `provider-records.json`, asserts all 320 records and five mapping controls, evaluates all `EDGE-001`–`EDGE-020`, and asserts zero false-positive actionable negative controls. |
| `P2R-05` | The desktop provides source-package pin, reason, ignore-until, decision/mapping reversal, source check status, update-status filters, per-product timeline and cache-retention disclosure. Expired ignores are removed from active decisions, source snapshots are pruned after 30 days, and persistence/application regressions cover the behavior. |
| `P2R-06` | Traceability now links this remediation evidence and explicitly excludes unexecuted network, real-source performance, and Phase 2 accessibility claims. |

## Positive controls confirmed

- Numeric, prerelease, downgrade, malformed and connector-deferred version outcomes are deterministic and have no lexical fallback.
- Current focused matching tests keep low-confidence, ambiguous, architecture/channel-conflicting and incomparable candidates non-actionable.
- The source connector constructor accepts only `winget` and `msstore`, uses `winget.exe` with structured arguments, and exposes no raw-command or arbitrary-web connector.
- Cache fallback and connector-isolation tests distinguish live, cached, local-only and unavailable domain states.
- The desktop exposes no executable Update action, and stable decisions/source cache remain outside disposable scan data.

## Residual risks accepted by product owner

ADR-021 classifies `AC-UX-01–09` as Optional — best effort. Existing accessibility implementation/evidence remains positive quality evidence but is not a Phase 2 gate condition. This does not waive independent fresh-review assessment of `AC-DAT-04` process-scoped connection evidence.

## Recommended next action

Review the Task 90 remediation/evidence in a fresh GPT-5.6 Sol / High Task 20 chat. Do not merge Phase 2 or begin Phase 3 unless that review passes.
