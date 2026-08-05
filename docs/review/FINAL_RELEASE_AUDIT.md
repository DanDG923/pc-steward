# Final MVP release audit

**Verdict:** **NOT READY**

**Reviewed implementation:** `0f03127dc9378efa4ace1412b960dc7dd87e97bb`

**Reviewed archive:** `PCSteward-0.1.0-private-beta-win-x64.zip` — SHA-256 `A8FD4AF124019E44B23C9F067C49F49A3D61FE8025F6ED9D8AF5E51E4CBCA3FC`

**History reviewed:** `b0a37f78dd412f8b23ed2dc1586068f4e8b95781..0f03127dc9378efa4ace1412b960dc7dd87e97bb`

**Reviewer model/effort:** GPT-5.6 Sol / Extra High

**Date:** 2026-08-05

## Task 90 remediation addendum (not a fresh audit verdict)

Task 90 remediated the repository findings identified here without widening authority. `FRA-19` now uses a non-UI, deterministic post-reset decision that blocks every failed acknowledgement and every successful acknowledgement with a non-None marker before inventory load or Scan navigation. Its two executable scenarios cover failed acknowledgement plus marker `None` and successful acknowledgement plus residual marker. `FRA-20` labels historical lifecycle observations with their complete SHA-256 `20617AEFCFE42822534060CC7902BECFF5911D5B27C5F2274449A84935272FBF`; `FRA-18` records focused reset evidence accurately as 25/25.

The post-remediation unsigned local archive is SHA-256 `16DE2AFB0CBF26BAE167DFABFD7A4FCAE619ABA317BFE97076D4FDD16ADE0523` (94 payload files, 49,955,522 bytes, 15 components, 16 SBOM nodes / 32 edges) and mechanically validates. Release build is clean; serialized component tests pass 251/251; reset is 25/25; cleanup, hostile-boundary, WinGet-focused, advisory and high-confidence secret checks pass. This addendum does not revise the audit verdict or close `FRA-14–16`: no current package launch, live/source performance, installed-app update, real task, owner-state lifecycle, signing, channel or distribution action was performed. A fresh Task 31 is required to re-grade all findings.

## Gate decision

PC Steward is not ready for release or external private-beta distribution. This fresh review found four High, one Medium and one Low open findings. One mandatory acceptance criterion is Failed, nine are Unverified, eight mandatory performance requirements are Unverified, and multiple release-definition gates are Failed.

Task 90 closed the earlier owned-root enumeration, packaged cleanup, reset-marker and limitations findings. The fresh audit nevertheless found a separate fail-open UI consumer: onboarding acknowledgement can return failure after reset authority has disappeared, but `MainWindow` blocks only when the marker is still present. If saved inventory loads, the UI continues to Scan despite the failed acknowledgement. This contradicts the fail-closed reset authority and prevents Task 31 passing.

The exact rebuilt archive is mechanically coherent and the owner-context automated suites are green. Those facts do not replace current-hash package launch/update/lifecycle, complete agreed-baseline performance, destructive owner-state reset, scheduled final-timeout, production signing, or distribution-channel evidence. Prior host observations and owner exceptions are bound to older archive hashes and are historical evidence only.

## Audit scope and fresh verification

- Reviewed the product specification; Phase 0–4 requirements; traceability; ADR-001–021; architecture, data-flow, factory-reset, test-strategy and threat-model documents; approved native design package; phase reviews; release evidence/checklist; retained host evidence; source composition; Task 90 diff; and repository history.
- Reviewed 132 commits after the initial product-spec commit, 607 tracked files, and the implementation change set: 577 files, 60,632 insertions and 157 deletions.
- Owner-context Release build: **0 warnings, 0 errors**. Serialized full suite: **249/249 passed** — Domain 55, Application integration 157, Architecture 17, Security 20. Focused factory reset: **25/25 passed**.
- Fake-only packaged cleanup contract: **Pass**. Host-safe hostile boundary: **42 assertions passed**, with one local-PC/manual symbolic-link case skipped. Non-executing WinGet boundary: **11/11 passed**.
- Fixture performance retry completed: 30 local runs at 1.81 ms median / 2.05 ms p95 and 20 simulated-online runs at 15.53 ms median / 16.20 ms p95. The first combined invocation exited between phases; the bounded retry passed. Neither run is final-app, live-source, idle, process-tree or acceptance evidence.
- Current NuGet advisory query: **0 known vulnerable direct or transitive package rows** across 17 projects. Release analyzers/build are clean. Independent high-confidence tracked-secret scan found **0 matches**.
- Exact archive validator: **Pass** — 94 payload files / 49,950,826 bytes; 15 package components; 16 SBOM nodes / 32 edges; portable hashes, licences and notices; archive 15,419,133 bytes. Signing status is **Not requested** and packaged launch smoke is **Not performed**.
- Sandbox-account test attempts that overlapped or lacked NT file authority were excluded. After their exact orphaned test chain was stopped, the single owner-context focused and full reruns passed. A sandbox-only .NET first-run message reported an untrusted HTTPS development certificate; it was not used for product signing or release evidence.
- The tracked worktree was clean before review. Task 31 modifies only this audit and `docs/STATUS.md`.

## Open findings

| ID | Severity | Requirements | File/line | Finding | Exact remediation | Required verification |
|---|---|---|---|---|---|---|
| `FRA-19` | High | `FR-DAT-12/15`, `AC-DAT-13`, ADR-013/019, `TM-16` | `src/PCSteward.Desktop/MainWindow.cs:129–145`; `src/PCSteward.Storage/DownloadedArtifactCache.cs:523–587` | Onboarding acknowledgement blocks a failed result only while the marker remains. `AcknowledgeAndDelete` can return `Absent` or `ControlStateRecoveryFailed` after control state is absent; a subsequent marker read is then `None`. If saved inventory loads, the UI proceeds to Scan despite `result.Succeeded == false`, converting an authority/recovery failure into continuation. | Treat every failed acknowledgement as blocking. Use marker state only for diagnostics. Continue only after `result.Succeeded`, exact marker absence and successful fresh-state load; never infer success from absent control state. | Inject failed `Absent`, `ControlStateRecoveryFailed` and final-verification outcomes with marker `None`; assert no inventory load, navigation or scan transition. Retain success-path and retry tests, then rerun reset, UI/Architecture, Security and full suites. |
| `FRA-14` | High | `NFR-PERF-01–08`, `AC-NFR-01/02/06/07/11`, ADR-006 | `docs/review/TASK_90_OWNER_EXCEPTIONS.md:21–27`; `docs/review/PERFORMANCE_AND_ACCESSIBILITY_EVIDENCE.md:18–39` | The exact current archive has no final-app 30-run local series, 20-run healthy live series, current launch/update responsiveness, complete five-minute idle I/O/network, full scan process-tree or real scheduled final-timeout result. Fixture timings and old-hash host samples cannot establish these gates. | Execute the complete unchanged ADR-006 protocol on the approved 3XS / `3XS-Overclock` baseline using the exact rebuilt package and healthy sources. Remediate any measured failure rather than changing thresholds. | Retain raw timestamped samples, environment/power-plan/tool versions, exact hash, source health, every sample and p50/p95, process-tree attribution, update/cancellation interaction, five-minute CPU/memory/disk/network and every scheduled terminal exit. |
| `FRA-15` | High | `AC-UPD-03/06`, `AC-DAT-12/13/18`, `AC-NFR-08/11`, `NFR-PERF-07`, ADR-002/009/013/020, release definition of done | `docs/release/PRIVATE_BETA_CHECKLIST.md:15–24`; `docs/release/RELEASE_EVIDENCE.md:8,12–13,17` | No supported-host lifecycle is bound to archive `A8FD4AF1...4CBCA3FC`: packaged launch/close, controlled current-user update, retain-data replacement, real task removal, two-confirmation owner-state reset/onboarding, reset-then-package removal and scheduled final-timeout are unverified. Automated disposable-root tests do not prove these owner-state observations. | After `FRA-19` is fixed and the package is rebuilt, execute the complete supported-host lifecycle from a caller-writable extraction under separately approved action-time scope. | Record host/operator/UTC, exact archive and payload hashes, before/after owner state, task XML/identity, process exits, retained-state replacement, zero-prior-state onboarding, unrelated-task/outside-sentinel preservation, no helper/UAC, rollback and zero residual state where applicable. |
| `FRA-16` | High | Release definition of done, ADR-001/002/008/009, `TM-18` | `docs/release/PRIVATE_BETA_CHECKLIST.md:13,28–32`; `docs/release/RELEASE_EVIDENCE.md:16,19` | No product-owner-approved production signer, signing service, trusted distribution channel, recipient list, support contact or distributable rollback artifact exists. The archive is unsigned engineering evidence, not a release candidate. | Obtain explicit release identity/channel/support/rollback inputs, rebuild after all fixes, sign the exact payload and required distribution artifacts, bind the checksum to the immutable final archive and prohibit post-sign mutation. Do not reuse the removed development certificate. | Independently verify chain trust, code-signing EKU, signer identity, timestamp/validity, every payload hash, archive/checksum/channel binding, recipient access, support and rollback on a clean supported host; repeat package/lifecycle validation on that exact signed hash. |
| `FRA-20` | Medium | Release definition of done, ADR-002/009, evidence integrity | `docs/release/RELEASE_EVIDENCE.md:8,17`; `docs/release/PRIVATE_BETA_CHECKLIST.md:17`; package validator report | Current release evidence correctly says no launch claim is inherited by `A8FD...`, but later says native launch smoke passed and the checklist marks standalone launch complete without binding either statement to an older hash. The exact validator says launch smoke was not performed. | Bind every retained launch/update/lifecycle observation to its exact archive hash and label it historical. Mark all current-archive launch/lifecycle rows unchecked or Unverified until rerun. | Cross-check audit, checklist, release evidence, limitations, About/support text, package validator and exact hash; require one consistent current-versus-historical scope. |
| `FRA-18` | Low | Release evidence accuracy | `docs/release/RELEASE_EVIDENCE.md:13`; `tests/PCSteward.Application.IntegrationTests/FactoryResetTests.cs` | Release evidence understates the current focused reset suite as 22 tests; the current owner-context suite is 25/25. | Replace the stale count and include the pre-migration, control-swap and exact attempt-cap cases in the evidence summary. | Rerun the focused filter once without concurrent testhosts and reconcile the observed 25/25 count across current release documents. |

**Finding count:** 6 open — 0 Blocker, 4 High, 1 Medium, 1 Low.

## Closed prior findings

| ID | Fresh disposition | Independent basis |
|---|---|---|
| `FRA-11` | Closed for its original owned-root/control-manifest defect. `FRA-19` tracks the distinct UI consumer failure found in this audit. | Exact root/control enumeration, pre-migration authority checks, late-race restoration and reset 25/25 pass. |
| `FRA-12` | Closed. | Exact Task Scheduler COM setup/query/delete/re-query fails closed; fake-only contract and Architecture 17/17 pass; packaged/source cleanup hashes match. |
| `FRA-13` | Closed. | Strict six-field marker, atomic category progress, durable `Partial` and exact 99,999/100,000 attempt behavior pass. |
| `FRA-17` | Closed for the narrow update/limitations wording originally reported. `FRA-20` tracks the new current-hash launch contradiction. | Known limitations and release notes retain signing, performance and lifecycle blockers and bound prior exceptions to the old archive. |

## Mandatory MVP acceptance criteria

Only `Proven`, `Failed` and `Unverified` are used. Historical evidence may support unchanged source behavior, but it is not current-archive host evidence.

| Criterion | Status | Independent basis |
|---|---|---|
| `AC-INV-01` | Proven | Registry/MSIX/WinGet discovery and composed inventory coverage pass. |
| `AC-INV-02` | Proven | Conservative normalization preserves distinct scope/architecture instances. |
| `AC-INV-03` | Proven | No `Win32_Product` inventory path exists. |
| `AC-INV-04` | Proven | Malformed/incomplete registry records warn or preserve unknowns without scan failure. |
| `AC-INV-05` | Proven | Disabled/local-only process-attributed evidence emits no connector request. |
| `AC-INV-06` | Proven | Inventory is standard-user and requests no elevation. |
| `AC-INV-07` | Proven | UI/source model exposes provider and local/live/cached freshness. |
| `AC-MAT-01` | Proven | Actionable rows expose source, stable ID, versions, confidence and explanation. |
| `AC-MAT-02` | Proven | Low-confidence, unmatched, stale, conflicting and unavailable evidence remains non-actionable. |
| `AC-MAT-03` | Proven | Zero-false-positive critical fixtures pass. |
| `AC-MAT-04` | Proven | Architecture/channel/scope and multi-instance conflicts fail closed. |
| `AC-MAT-05` | Proven | Unsafe or incomparable versions remain Unknown/Review. |
| `AC-MAT-06` | Proven | Source failure retains visibly stale timestamped last-known-good results. |
| `AC-MAT-07` | Proven | Trusted exact-ID mappings are persisted, reversible and tested. |
| `AC-UPD-01` | Proven | Immutable explicit review/confirmation is the only runner entry. |
| `AC-UPD-02` | Proven | Typed execution rejects arbitrary command/path/argument authority. |
| `AC-UPD-03` | Unverified | Controlled current-user no-UAC update evidence is bound to an older archive, not `A8FD...`. |
| `AC-UPD-04` | Proven | Standalone machine scope is non-actionable; helper/UAC code is not composed or packaged. |
| `AC-UPD-05` | Proven | Denial/cancellation remain distinct from failure and Success. |
| `AC-UPD-06` | Unverified | The verified VS Code update is historical old-hash evidence; no current-archive update was run. |
| `AC-UPD-07` | Proven | Non-zero, timeout, overflow, cancellation and launch failures cannot become Success. |
| `AC-UPD-08` | Proven | Success requires exact bounded post-update version evidence. |
| `AC-UPD-09` | Proven | Batch execution is sequential with independent history. |
| `AC-UPD-10` | Proven | Cancellation preserves completed/running evidence and stops unstarted work. |
| `AC-UPD-11` | Proven | Typed, timestamped, sanitized local history is persisted and tested. |
| `AC-DAT-01` | Proven | Current SQLite persistence/migration integration tests pass. |
| `AC-DAT-02` | Proven | CSV/JSON snapshot fidelity and timestamp coverage pass. |
| `AC-DAT-03` | Proven | Inventory/history and cached-state paths remain usable offline. |
| `AC-DAT-04` | Proven | Process-attributed enabled/disabled trace recorded zero lost events. |
| `AC-DAT-05` | Proven | No account/cloud identity capability or dependency exists. |
| `AC-DAT-06` | Proven | Transactional clear-scan-data returns to no-scan state. |
| `AC-DAT-07` | Proven | Settings/schedule/history/downloads/decisions preservation rules pass. |
| `AC-DAT-08` | Proven | Inactive cache cleanup, accounting and category isolation pass. |
| `AC-DAT-09` | Proven | UI exposes recoverable owned bytes and cleanup action. |
| `AC-DAT-10` | Proven | Fixed app-owned roots and provider-cache exclusion pass hostile fixtures. |
| `AC-DAT-11` | Proven | Locked owned artifacts produce Partial with retained residual evidence. |
| `AC-DAT-12` | Unverified | Current deterministic reset deletion passes, but no current-hash owner-state two-confirmation lifecycle was authorized. |
| `AC-DAT-13` | Failed | Failed onboarding acknowledgement can still continue when the marker is absent; see `FRA-19`. |
| `AC-DAT-14` | Proven | Per-user clear/reset requests no elevation. |
| `AC-DAT-15` | Proven | Active-operation lease and destructive-action exclusion tests pass. |
| `AC-DAT-16` | Proven | Junction/reparse/path-escape fixtures preserve outside sentinels. |
| `AC-DAT-17` | Proven | Bounded structured diagnostics and sanitizer fixtures pass. |
| `AC-DAT-18` | Unverified | Exact cleanup contract passes, but no current-hash real task/folder-removal lifecycle exists. |
| `AC-NFR-01` | Unverified | No 30-run final-app local inventory series for the exact archive. |
| `AC-NFR-02` | Unverified | No 20-run healthy live final-app series for the exact archive. |
| `AC-NFR-03` | Proven | Current cancellation/recovery fixtures return to operable cached state. |
| `AC-NFR-04` | Proven | Active-scan termination retains the previous successful snapshot. |
| `AC-NFR-05` | Proven | Connector timeout/fault isolation and partial publication pass. |
| `AC-NFR-06` | Unverified | Complete five-minute CPU/memory/disk/network gate is absent for the exact archive. |
| `AC-NFR-07` | Unverified | Complete final-package local-scan process-tree protocol is absent. |
| `AC-NFR-08` | Proven | Process-exit implementation/regressions pass; exact-package launch/close remains a release-lifecycle gap under `FRA-15`. |
| `AC-NFR-09` | Proven | One-scan exclusivity and connector concurrency pass. |
| `AC-NFR-10` | Proven | Quota/oldest-inactive/active-artifact protection passes. |
| `AC-NFR-11` | Unverified | Real current-package scheduled final-timeout exit is absent. |

**Mandatory acceptance count:** 54 — 44 Proven, 1 Failed, 9 Unverified.

`AC-UX-01–09` remain Optional — best effort under ADR-021. No accessibility-conformance claim is made.

## Mandatory non-functional requirements

| Requirement | Status | Independent basis |
|---|---|---|
| `NFR-PERF-01` | Unverified | No current-package 30-run final-app local inventory series. |
| `NFR-PERF-02` | Unverified | No current-package 20-run healthy live-source series. |
| `NFR-PERF-03` | Unverified | Current-package scan and update responsiveness were not measured. |
| `NFR-PERF-04` | Unverified | Time-to-interactive evidence is bound to an older archive; startup code changed. |
| `NFR-PERF-05` | Unverified | Complete five-minute disk/network gate is absent. |
| `NFR-PERF-06` | Unverified | Complete final-package scan process-tree measurement is absent. |
| `NFR-PERF-07` | Unverified | Current-package normal/final-timeout scheduled exit matrix is incomplete. |
| `NFR-PERF-08` | Unverified | Current-package UI acknowledgement/recovery timing was not measured. |
| `NFR-PERF-09` | Proven | One-scan lease and bounded connector concurrency pass. |
| `NFR-PERF-10` | Proven | Configurable quota and active-artifact exclusion pass. |
| `NFR-REL-01` | Proven | Connector faults retain other successful results. |
| `NFR-REL-02` | Proven | Transactional promotion and crash recovery retain last-known-good state. |
| `NFR-REL-03` | Proven | Interrupted/cancelled batches preserve completed and verification-required states. |
| `NFR-REL-04` | Proven | Cached results retain timestamp and non-live status. |
| `NFR-REL-05` | Proven | Malformed/missing registry fixtures are contained. |
| `NFR-MNT-01` | Proven | Capability-specific interfaces isolate failures. |
| `NFR-MNT-02` | Proven | Matching/version rules have UI-free unit coverage. |
| `NFR-MNT-03` | Proven | Provider command/parser construction is outside presentation. |
| `NFR-MNT-04` | Proven | Versioned SQLite migration ownership/recovery evidence exists. |
| `NFR-MNT-05` | Proven | Security/deployment decisions are recorded in ADR-001–021. |
| `NFR-OBS-01` | Proven | Scan/update correlation IDs are typed and tested. |
| `NFR-OBS-02` | Proven | Structured bounded severity events are tested. |
| `NFR-OBS-03` | Proven | Cancellation, denial, timeout, verification, provider and product failures remain distinct. |
| `NFR-OBS-04` | Proven | Sink/log retention controls and tests are present. |
| `NFR-OBS-05` | Proven | Sanitized diagnostics and hostile-string fixtures pass. |

**Mandatory NFR count:** 25 — 17 Proven, 0 Failed, 8 Unverified.

## Release definition of done

| Release item | Status | Judgment |
|---|---|---|
| All mandatory acceptance criteria pass | Failed | One Failed and nine Unverified criteria remain. |
| Zero-false-positive fixture passes | Proven | Current critical mapping/version/actionability tests pass. |
| Threat model reviewed against implemented architecture | Failed | `FRA-19` violates fail-closed reset/onboarding authority. |
| Release binaries signed before external distribution | Failed | No approved signer or trusted channel exists. |
| Critical update-execution and privilege code has automated tests and manual review | Proven | Security/Architecture suites and standalone user-scope composition review pass. |
| Dependency, static-analysis and secret-scanning checks pass | Proven | Clean analyzers/build, zero known advisory rows and zero high-confidence secret matches. |
| Release SBOM generated | Proven | Validator reconciles 15 components, 16 nodes, 32 edges, licences, notices and hashes. |
| Standalone launch/replacement/task cleanup/package removal/factory reset tested | Failed | Exact-current-archive lifecycle evidence is missing; see `FRA-15/20`. |
| Scan and idle acceptance pass on agreed baseline | Failed | Required current-package performance gates remain Unverified; see `FRA-14`. |
| Destructive path-safety, locked-file and partial-reset tests pass | Failed | Storage fixtures pass, but the untested UI acknowledgement failure can continue; see `FRA-19`. |
| Known limitations documented in app and release notes | Proven | App and limitations retain signing, lifecycle and performance blockers without readiness claims. |
| Product owner completed real-PC local-only scan before update execution | Unverified | The retained scan/update sequence is bound to an older archive, not the current package. |

## Residual risks requiring explicit product-owner acceptance

These risks cannot override open findings or mandatory Failed/Unverified criteria:

- VM execution remains excluded by product-owner direction; supported-host evidence must identify the exact physical environment, power plan and archive.
- ADR-021 keeps `AC-UX-01–09` Optional — best effort; incomplete Narrator/high-contrast/scaling evidence cannot support accessibility conformance.
- Provider-disconnect cancellation, same-user substitution of the name-based temporary WinGet export, imperfect package-ID/process-name correlation and historical outcome-dialog replay remain bounded documented risks.
- Protected installed-helper/UAC evidence is historical/future-installed-mode evidence only; the standalone payload must continue to compose no elevated helper or machine-scope execution.
- A caller-writable extraction cannot provide installed-binary tamper resistance; signed channel/checksum controls and explicit guidance remain mandatory before external distribution.
- ADR-003 and ADR-011 remain Provisional and must be revisited if provider authority or cache policy changes.

## Required sequence

Task 31 fails. Run Task 90 under GPT-5.6 Terra / High. Remediate `FRA-19`, `FRA-20` and `FRA-18` without widening authority; `FRA-14–16` require separately approved host actions and external signer/channel/support/rollback inputs. Rebuild and re-hash after any code change, then repeat Task 31 under GPT-5.6 Sol / Extra High. Do not release, tag, publish, upload, share or call the MVP ready before a fresh audit returns READY.
