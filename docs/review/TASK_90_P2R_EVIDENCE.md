# Task 90 — Phase 2 remediation evidence

**Model/effort:** GPT-5.6 Terra / High  
**Fixing commit:** `1fd152c` — `fix: remediate phase review findings`
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

## Approved live-source evidence — 2026-07-28

The product owner explicitly approved sending installed-package identifiers to the fixed WinGet and Microsoft Store sources for this evidence run. Each command was read-only, used `winget upgrade --source <winget|msstore> --disable-interactivity`, discarded package listings, and retained only timing and exit-code aggregates.

- One approved probe: `winget` **0** / **2,241.70 ms**; `msstore` **0** / **866.65 ms**.
- Twenty complete sequential two-source checks: **20/20** had zero exit codes; **1,762.50 ms median**, **1,856.73 ms p95**, **1,921.53 ms maximum**.
- This supports `NFR-PERF-02` / `AC-NFR-02` for this machine and time. It does not generalize to other networks or source health conditions.

The initial timing-only evidence run retained no package listing, package identifier, remote endpoint, or packet payload. A separately approved, constrained connection-metadata observation follows.

## Approved process-scoped connection inspection — 2026-07-28

The product owner then approved retaining only destination IP/port, process ID, timestamps, and exit code for the two same read-only source checks. Standard output/error was asynchronously drained and discarded; no package listing or payload was retained.

| Fixed source | Process | Time (UTC) | Exit | Observed destination |
|---|---:|---|---:|---|
| `winget` | 7844 | 15:53:38–15:53:39 | 0 | No established socket was observed during the 1.18-second process lifetime. |
| `msstore` | 40328 | 15:53:39–15:53:40 | 0 | `[2a02:26f0:fd00:1189::4106]:443`, first observed 15:53:40.513. |

This is process-scoped observation of the fixed source-check commands, not packet capture. It found no connection from the Winget check and one TLS connection from the Store check, with no unapproved connector command or arbitrary endpoint path in the application composition. It is evidence for `AC-DAT-04`, subject to the fresh Task 20 reviewer's assessment of whether process-scoped connection metadata is sufficient.

## Desktop accessibility observation — 2026-07-28

In a local-only desktop session, the accessibility tree exposed named source-disclosure checkboxes, the disabled-until-approved source-check button, status text, update-intelligence counts, source-cache-retention text, named search/status/publisher/source/scope/sort controls, product list, decision area, and clear-data controls. This supports discoverability and text-based status presentation.

The product owner approved temporary display changes. Under an applied 150% text-scale override and enabled Windows high contrast, the desktop capture showed all Phase 2 header, source consent/check controls, status/count text, retention text, search/status/publisher/source/scope/sort filters, and inventory list visible without clipped text. The original high-contrast flags and absence of a text-scale override were restored afterward.

Keyboard text entry into the named Search control succeeded; the helper still did not reliably move focus with Tab or activate a list item. See `docs/failures/90-phase2-accessibility-evidence.md`. Do not treat the full `AC-UX-02` keyboard criterion as passed.

## Evidence deliberately not claimed

- `AC-DAT-04`: process-scoped connection metadata has been recorded; a fresh reviewer must determine whether this is sufficient versus a packet-level inspection.
- `AC-UX-02`: keyboard text entry is verified but keyboard navigation/activation across all Phase 2 controls remains unproven.
- ADR-003: real WinGet/Microsoft Store parser/capability behavior remains unproven.

## Product-owner exception — 2026-07-28

Per explicit product-owner direction, `AC-UX-02` is a **temporary approved exception for the Phase 2 gate**. It permits a fresh Task 20 reviewer to assess Phase 2 without treating the incomplete keyboard-navigation/activation exercise as compliance evidence. The criterion remains mandatory follow-up work and must be repeated on an interactive Windows desktop before a release claim relies on it.

`AC-DAT-04` remains subject to independent fresh-review assessment of the approved process-scoped connection evidence. ADR-003 real-source parser/capability evidence remains unproven.
