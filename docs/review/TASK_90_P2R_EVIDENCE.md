# Task 90 — Phase 2 remediation evidence

**Model/effort:** GPT-5.6 Terra / High  
**Fixing commit:** pending final commit amendment  
**Scope:** `P2R-01`–`P2R-06` from `PHASE_2_REVIEW.md`

## Local evidence executed

| Finding | Evidence | Result |
|---|---|---|
| `P2R-01` | `InventoryWorkflowUpdateSourceTests.ComposedWorkflowUsesApprovedSourceOnlyWhenCheckedAndSupportsExactManualMappings` | Persisted inventory remains local-only until the caller explicitly checks the approved connector; the live snapshot yields a verified candidate; an exact cached trusted package can be mapped. |
| `P2R-02` | `InventoryExperienceViewModelTests.MixedSourceFreshnessIsShownPerCandidateInsteadOfAsTheAlphabeticallyFirstSource` | A Winget-mapped cached/stale candidate remains labelled `winget: cached`; simultaneous Store liveness cannot relabel it live. |
| `P2R-03` | `WingetInstalledInventoryProviderTests.OversizedProcessOutputFailsClosedWithoutRetainingMoreThanTheBound` | Both stdout and stderr over one million characters set the typed overflow flag, retain at most the configured bound, and result in a fail-closed source classification. |
| `P2R-04` | `FixtureCatalogMatchingTests.CanonicalFixtureCatalogControlsAndRelevantEdgesHaveZeroFalsePositiveActionableMappings` | Checked-in catalog: 320 records, five declared mapping controls, 20 adversarial edges, zero actionable negative controls. |
| `P2R-05` | View-model, source-refresh, decision-service and SQLite retention regressions | Source package pin/reason/ignore-until, active-expiry exclusion, mapping timeline, local scan timeline, 30-day source cache pruning, status filtering and mapping reversal are covered without execution authority. |
| `P2R-06` | `docs/requirements/TRACEABILITY.md` | Corrected to cite this evidence and distinguish local fixtures from pending external/manual evidence. |

## Commands and results

- Focused Phase 2 regressions: **23 passed** (Application integration) and canonical fixture: **1 passed** (Domain); the SQLite retention regression also passed in the full suite.
- Release build: `dotnet build PCSteward.slnx --configuration Release --no-restore` — **0 warnings, 0 errors**.
- Full Release suite: **89 passed** — Domain 37, Application integration 48, Architecture 3, Security fixture 1.
- Fixture benchmark: 30 local fixture runs at **1.85 ms median / 1.98 ms p95**; 20 two-source fixture-only refreshes at **15.50 ms median / 16.06 ms p95**. No live WinGet or Store source was contacted.

## Evidence deliberately not claimed

- `AC-DAT-04`: no real WinGet/Store source probe or network inspection was performed; explicit approval remains required.
- `AC-NFR-02`: a fixture-only benchmark does not prove end-to-end online-source p95.
- `AC-UX-02/04`: Phase 2 controls have not yet had keyboard, 150% text-scaling, or high-contrast desktop evidence; a local desktop automation attempt produced no usable target window.
- ADR-003: real WinGet/Microsoft Store parser/capability behavior remains unproven.

These boundaries remain blockers for a passing Phase 2 gate unless separately evidenced with appropriate authorization.
