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

- `AC-DAT-04` gate passage: the event-based enabled/disabled evidence below now has the requested process attribution and loss status, but a fresh reviewer must make the independent gate determination.
- `AC-UX-02`: keyboard text entry is verified but keyboard navigation/activation across all Phase 2 controls remains unproven.
- ADR-003: real WinGet/Microsoft Store parser/capability behavior remains unproven.

## Product-owner accessibility priority decision — 2026-07-28

ADR-021 classifies all `AC-UX-01–09` criteria as Optional — best effort. The existing accessibility implementation/evidence remains positive quality evidence, but incomplete keyboard navigation or accessibility evidence is not a Phase 2 gate failure.

`AC-DAT-04` remains subject to independent fresh-review assessment of the approved event-based production-composition evidence below. ADR-003 real-row parser evidence remains unproven.

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

### Follow-up approved WinGet row probe — 2026-07-28

After explicit approval to disclose installed-package metadata to the fixed WinGet source, one further read-only `winget.exe upgrade --source winget --disable-interactivity` query completed with exit code **0** in **1,542.32 ms**. It produced **zero** parser-table rows. The reducer retained only that row count and no package identifier, name, version, listing text, or payload. This reconfirms that this host cannot supply the required real-row evidence; it does not satisfy ADR-003's real-row requirement.

A second, still read-only probe added WinGet's `--include-unknown` flag to surface entries that the default listing may omit. It also completed with exit code **0** in **1,100.61 ms** and yielded **zero** parser-table rows. It retained only the fixed source, fixed arguments, timing, exit code, and row count. The two different WinGet-only probes are therefore insufficient; see `docs/failures/90-phase2-real-row-evidence.md` for the blocked handoff.

After separate explicit approval for the Microsoft Store destination, `winget.exe upgrade --source msstore --disable-interactivity` completed with exit code **0** in **1,064.09 ms** and also yielded **zero** parser-table rows. It retained only the fixed source, fixed arguments, timing, exit code, and row count. No default-source row exists on this controlled host; a sanitized capture from another controlled host remains necessary.

### Approved process-scoped connection observation — 2026-07-28

The product owner also approved one direct fixed source check per source for `AC-DAT-04`. Each process used exactly `winget.exe upgrade --source <winget|msstore> --disable-interactivity`; stdout and stderr were asynchronously drained to the null stream. The observer retained only source, process ID, UTC interval, exit code, elapsed time, and established remote IP/port.

| Fixed source | Process | UTC interval | Exit | Duration | Observed destination |
|---|---:|---|---:|---:|---|
| `winget` | 36712 | 16:41:16.693–16:41:17.817 | 0 | 1,124.02 ms | No established socket observed during process lifetime. |
| `msstore` | 37476 | 16:41:17.848–16:41:19.037 | 0 | 1,188.81 ms | `[2a02:26f0:1c80:d9b::4106]:443` |

This is process-scoped connection metadata, not packet capture. Together with the fixed allowlist, explicit connector approval tests, and the local-only/disabled-connector tests, it is evidence for `AC-DAT-04`; a fresh reviewer must determine whether it is sufficient for the gate.

### Fresh-review result and final deterministic correction — 2026-07-28

Fresh GPT-5.6 Sol / High review passed `P2R-07`–`P2R-09` but found that a source-disagreement mapping whose selected candidate was Equal could still be labelled Current and omit the product from `AttentionNeeded`. `UpdateIntelligenceTests.SourceDisagreementNeverClassifiesAnOtherwiseCurrentMappingAsCurrent` now verifies that such a mapping is `ReviewRequired`, remains non-actionable, and appears in attention. Release build reported **0 warnings, 0 errors**; the full suite reported **95 passed**.

The same fresh review judged the direct-command connection observation insufficient for `AC-DAT-04`: it lacks an event-based, process-attributed comparison between a disabled/local-only production path and an explicitly enabled production path. It also judged the zero-row probes insufficient for the real source-row portion of ADR-003. A final evidence run needs a bounded ETW/WFP/Procmon-style metadata trace with no dropped events and one provenance-documented sanitized real update row from a controlled Windows 11 host (or an equivalent sanitized captured fixture). Do not treat the zero-row result as proof of real row/source-column parsing.

### Approved production UI trace attempt — 2026-07-28

The product owner approved a temporary metadata-only trace. In the Release desktop application, both WinGet disclosures were disabled and a local-only scan completed while the update-check button was disabled. The package-source disclosure was then explicitly approved, the production update-check button ran, and the UI reported a live Winget source with the Store source unavailable. The package-source control was then disabled again; the UI returned to Local-only and the update-check button was disabled.

`netsh trace` ran with `capture=no`, so packet capture was disabled. The broad InternetClient session was too noisy to provide trustworthy process attribution. A narrow TCP endpoint/process-ID plus kernel-process provider session failed with Windows error 5 because this session lacks a Windows-administrator token. A separate 15-second process-start monitor, restricted to PC Steward/WinGet process name, parent/child ID, and timestamp, did not start successfully. Temporary ETW and CSV files were deleted after aggregate analysis; no raw events, payloads, package listings, or identifiers were retained.

This attempt confirms the production UI control transitions but does not close `AC-DAT-04`. Re-run the metadata-only trace under a controlled Windows-administrator token or equivalent approved endpoint instrumentation that can emit process-attributed connection records and dropped-event status.

### Approved elevated event-based production-composition trace — 2026-07-28

The product owner approved a temporary event-based production network trace retaining metadata only. A UAC-elevated `logman` controller ran for **79 seconds**, from **17:27:05.431Z** to **17:28:24.875Z**, with a 64 MB circular cap. It enabled:

- `Microsoft-Windows-Kernel-Process` process events (`0x10`);
- `Microsoft-Windows-TCPIP` endpoint, process-ID hint, connect, close, remote-endpoint, address, summary and dropped-event metadata (`0x0000291400008601`).

Packet capture was not enabled. TCP packet, send-path, receive-path and payload keywords were not enabled. The retained reduction contains no package identifiers, package listings, command output, packet contents or payload data.

| Production segment | UI evidence | Event evidence |
|---|---|---|
| Disabled / Local-only | Both WinGet disclosures were unchecked; **Check approved update sources** was disabled; local-only scan completed at **17:27:18.181Z**. | PC Steward PID **44124** emitted no remote-endpoint event, and no WinGet process started before explicit approval. |
| Explicitly enabled | The product owner manually enabled **WinGet package sources** and ran the production source check. | Starting at **17:27:52.253Z**, four WinGet children were attributed to PC Steward PID 44124. |
| Restored Local-only | The product owner unchecked **WinGet package sources** after completion. Both WinGet disclosures were unchecked, the source-check button was disabled, and the UI again stated that inventory was Local-only. | The UI retained the completed source result: Winget live at **17:27:55.184Z** and Store unavailable. No connector remained enabled. |

All four child start events reported medium integrity (`S-1-16-8192`) and `ProcessTokenIsElevated=0`.

| WinGet PID | Parent PID | UTC lifetime | Exit | Successful remote endpoint event |
|---:|---:|---|---:|---|
| 47260 | 44124 | 17:27:52.253–17:27:52.392 | 0 | None; short capability process. |
| 37364 | 44124 | 17:27:52.302–17:27:52.417 | 0 | None; short capability process. |
| 40940 | 44124 | 17:27:52.415–17:27:53.644 | 0 | `[2a02:26f0:fd00:1190::4106]:443` (`Status=0x0`). |
| 25128 | 44124 | 17:27:52.438–17:27:55.172 | 0 | `[2603:1061:14:d6::1]:443` (`Status=0x0`). |

The endpoint reducer observed three successful connect-completion events across those two unique TLS destinations and three subsequent close/cleanup events. It deliberately does not assign a source name to a PID because the selected process provider did not retain command lines; source-level outcome comes from the production UI.

`tracerpt` processed **96 buffers** and **45,977 events** with **0 events lost**. The temporary ETL was 6,291,456 bytes. Raw ETL/XML, summary, marker, provider-list and controller files were deleted after this sanitized reduction was recorded. This supplies the event-based, process-attributed disabled-versus-enabled comparison requested by the fresh review; `AC-DAT-04` gate passage remains for that reviewer to decide.
