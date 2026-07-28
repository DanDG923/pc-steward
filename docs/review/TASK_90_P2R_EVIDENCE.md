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

## Product-owner accessibility priority decision — 2026-07-28

ADR-021 classifies all `AC-UX-01–09` criteria as Optional — best effort. The existing accessibility implementation/evidence remains positive quality evidence, but incomplete keyboard navigation or accessibility evidence is not a Phase 2 gate failure.

`AC-DAT-04` remains subject to independent fresh-review assessment of the approved process-scoped connection evidence. ADR-003 real-source parser/capability evidence remains unproven.

## Repeated-review remediation — approved evidence complete

**Scope:** `P2R-07`–`P2R-10` from the repeated Task 20 review.

| Finding | Deterministic evidence | Result |
|---|---|---|
| `P2R-07` | `ProductPackageMatchingTests.SamePackageIdFromAnotherTrustedSourceIsReviewOnly`; `InventoryNormalizationTests.SameProviderPackageIdFromDifferentTrustedSourcesStaysSeparate` | A same-ID candidate from a different trusted source is Low-confidence and non-actionable; source provenance stays in normalized instance identity. |
| `P2R-08` | `InventoryWorkflowUpdateSourceTests.SamePackageIdFromAnotherSourceAndMultipleInstallTargetsRemainReviewOnly` | Multiple WinGet target records with different version/scope/architecture/channel/source evidence produce `MultipleInstallationInstanceAmbiguity` and cannot become actionable. |
| `P2R-09` | `SqliteDatabaseTests.BothWinGetConnectorDisclosuresPersistAndFailClosedWhenRevoked` | Both `WinGet installed inventory` and `WinGet package sources` disclosures survive restart and fail closed when revoked. |
| `P2R-10` | `UpdateIntelligenceTests.DifferentTrustedSourceVersionsStayVisibleAndBlockAutomaticSelection`; `InventoryExperienceViewModelTests.UpdateDetailsExposeSafetyEvidenceAndNeverOfferAnExecutableAction` | Per-source candidate observations remain visible; trusted-source differences make automatic/provider-supplied mappings review-only with `SourceDisagreement`. |

Local verification completed: Release build reported **0 warnings, 0 errors**; full Release suite reported **94 passed** (40 Domain, 50 Application integration, 3 Architecture, 1 Security fixture); fixture benchmark reported **1.88 ms median / 2.89 ms p95** over 30 local runs and **15.33 ms median / 16.07 ms p95** over 20 two-source fixture refreshes.

### Approved fixed-source parser/capability probe — 2026-07-28

The product owner approved two read-only `WingetUpdateSourceConnector` calls, one for each allowlisted source. The connector ran only `winget.exe --version` and `winget.exe upgrade --source <winget|msstore> --disable-interactivity`. Package content was not written to disk or terminal; this record retains only safe aggregate parser results.

| Source | Parser state | Provider version | Candidate count | Warnings | Duration |
|---|---|---|---:|---:|---:|
| `winget` | Live | present | 0 | 0 | 1,178.02 ms |
| `msstore` | Live | present | 0 | 0 | 990.23 ms |

This exercises the real fixed-argument connector, its process boundary, and its parser/capability path for ADR-003. It records no package identifiers, names, versions, listing text, or payload.

### Approved process-scoped connection observation — 2026-07-28

The product owner also approved one direct fixed source check per source for `AC-DAT-04`. Each process used exactly `winget.exe upgrade --source <winget|msstore> --disable-interactivity`; stdout and stderr were asynchronously drained to the null stream. The observer retained only source, process ID, UTC interval, exit code, elapsed time, and established remote IP/port.

| Fixed source | Process | UTC interval | Exit | Duration | Observed destination |
|---|---:|---|---:|---:|---|
| `winget` | 36712 | 16:41:16.693–16:41:17.817 | 0 | 1,124.02 ms | No established socket observed during process lifetime. |
| `msstore` | 37476 | 16:41:17.848–16:41:19.037 | 0 | 1,188.81 ms | `[2a02:26f0:1c80:d9b::4106]:443` |

This is process-scoped connection metadata, not packet capture. Together with the fixed allowlist, explicit connector approval tests, and the local-only/disabled-connector tests, it is evidence for `AC-DAT-04`; a fresh reviewer must determine whether it is sufficient for the gate.
