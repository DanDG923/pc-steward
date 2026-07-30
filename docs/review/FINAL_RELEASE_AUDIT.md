# Final MVP release audit

**Verdict:** **NOT READY**
**Reviewed implementation:** `2ff0cd2241878257daeea8bc942b599952c9ad35`
**History reviewed:** `b0a37f78dd412f8b23ed2dc1586068f4e8b95781..2ff0cd2241878257daeea8bc942b599952c9ad35`
**Reviewer model/effort:** GPT-5.6 Sol / Extra High
**Date:** 2026-07-30

## Gate decision

PC Steward is not ready for private beta. The review found eight open High findings and two open Medium findings. Four mandatory acceptance criteria are Failed, seven are Unverified, and mandatory non-functional and release-definition gates also remain Failed or Unverified. In particular:

- factory reset does not implement the accepted shared cross-process lease, marker validation, exact scheduled-task absence proof, fresh-store verification, or post-reset onboarding;
- the current standalone candidate has no approved release signature or trusted distribution channel;
- the current user-scope-only candidate has no controlled per-user update/no-UAC/exact-version evidence;
- the known idle CPU result fails its mandatory budget, while baseline scan, cold-start, process-tree, and scheduled-exit evidence is incomplete; and
- standalone replacement, complete package removal, in-app limitations, dependency-licence/notices, and the approved no-browser-runtime package boundary are incomplete.

The green automated suites prove valuable safety controls but do not substitute for the failed or unexecuted mandatory observations. `PO-EXC-29-PERF-01` remains a deferral, not a pass. Task 90 remediation is required, followed by a fresh Task 31 review.

## Audit scope and fresh verification

- Read the complete product specification, Phase 0–4 requirements, traceability matrix, all ADRs, architecture, threat model, factory-reset design, test strategy, design delivery plan, approved native design package, every phase/release review, Task 90 evidence, release documents, and retained raw/manual/host evidence.
- Inspected the complete 128-commit history after the initial product-spec commit and the full `b0a37f7..2ff0cd2` change set: 573 files, 56,854 insertions, and 171 deletions. The reviewed repository contains 603 tracked files.
- Release build: **0 warnings, 0 errors** with .NET SDK `10.0.302`.
- Full Release suite: **209/209 passed** — Domain 55, Application integration 124, Architecture 10, Security 20.
- Focused critical matrix: **114/114 passed** — zero-false-positive/command-shape 22, helper/IPC and execution negatives 20, reset/cleanup/cancellation/verification/scheduling 62, architecture/resident/helper boundaries 10.
- Fresh fixture performance: 30 local runs at 1.86 ms median / 2.56 ms p95 and 20 simulated-online runs at 15.49 ms median / 15.56 ms p95. The harness correctly states that it is not final-app, live-source, idle, process-tree, or agreed-baseline evidence.
- NuGet advisory check: **0 known vulnerable direct or transitive package rows**. The tracked-file pattern scan produced two `task-...` filename-text false positives to an over-broad `sk-` pattern and no credential candidate after classification.
- Independent candidate inspection: 86 payload files, 86 manifest rows, zero hash mismatch, 86 ZIP entries, no elevated-helper entry, and ZIP SHA-256 `6E4AAC78E8ABF32796CC302CFB37A8EA2372AA738ADBB19B9C69660BC91B1662`.
- All 13 PC Steward-owned signed files resolve to one removed development signer and currently return `UnknownError`; the retained signing report explicitly classifies the certificate as untrusted. The ZIP is not signed.
- The SBOM contains 99 component rows but only 33 unique package/version pairs, no licence entries, and solution-wide test/dormant-helper dependencies. The ZIP contains three WebView runtime files.
- The worktree was clean before this report; current `git diff --check` passed.

## Findings

| ID | Severity | Requirements | File/line | Finding | Exact remediation | Required verification |
|---|---|---|---|---|---|---|
| `FRA-01` | High | `FR-DAT-14–15`, `AC-DAT-15`, ADR-013, `TM-16` | `src/PCSteward.Storage/FactoryReset.cs:15–40,51–79`; `src/PCSteward.Desktop/MainWindow.cs:694–704`; `src/PCSteward.ScheduledHost/Program.cs:15,26–38`; `tests/PCSteward.Application.IntegrationTests/FactoryResetTests.cs:8–43` | The accepted factory-reset coordination protocol is not implemented. `Arm` writes the sentinel without acquiring `Local\PCSteward.DataOperation.v1`; desktop scan/source/update/cleanup paths do not participate in that lease; ScheduledHost uses the different `Local\PCSteward.ScheduledCheck.v1` mutex. A scheduled scan or second desktop can therefore race reset arming/cleanup. Sentinel content is never parsed, so a malformed/unknown marker starts destructive cleanup instead of the required blocking recovery. The only reset test is a single happy-path/idempotence case and does not exercise the mandatory active-work, malformed-marker, crash, locked, partial, reparse, schedule-failure, or second-process matrix. | Implement one shared cross-process data-operation coordinator for every desktop and scheduled-host state consumer. Recheck and acquire it before arming, fail closed without changing state when busy, validate the fixed marker schema/state before deletion, and route malformed/unknown markers to blocking recovery. Preserve the marker on every post-commit failure. | Add production-path regressions for active manual/scheduled scans, source refresh, update/verification, cleanup, second desktop, malformed/unknown/temporary markers, crash points, locked/partial objects, and reparse/escape fixtures. Run controlled concurrent desktop/host observations and prove old state never reopens. |
| `FRA-02` | High | `FR-DAT-12/15`, `AC-DAT-12`, ADR-013, `TM-14/16` | `src/PCSteward.Scheduling.Windows/ScheduledCheckRegistration.cs:85–89`; `src/PCSteward.Storage/FactoryReset.cs:60–77`; `tests/PCSteward.Application.IntegrationTests/ScheduledChecksTests.cs:34–61`; `tests/PCSteward.Application.IntegrationTests/FactoryResetTests.cs:24–35` | Reset can claim success while the exact scheduled task remains. `RemoveAsync` treats every `schtasks /Delete` exit code `1` as success even though that code is not uniquely “not found,” and neither it nor reset performs the accepted exact-name absence query. Reset then removes the marker. Tests record only a successful delegate call and do not cover ambiguous failure or a residual task. | Distinguish an already-absent task through an exact query, treat every other delete/query failure as a residual, query again after deletion, and remove the reset marker only after exact absence is proven. | Use a controlled runner matrix for absent, successful delete, access denied, malformed output, cancellation, timeout, and task-still-present cases. Add an on-machine exact-task test proving failure retains the marker and success leaves no PC Steward task or unrelated-task change. |
| `FRA-03` | High | `FR-DAT-12`, `FR-ONB-01–04`, `AC-DAT-13`, ADR-013, `TM-16` | `src/PCSteward.Desktop/App.xaml.cs:19–63`; `src/PCSteward.Desktop/MainWindow.cs:33,44–45,92–156`; `docs/review/FINAL_RELEASE_EVIDENCE_READINESS.md:32–33` | The actual two-confirmation reset removed the old 674-row state, but the next launch went directly to Overview and produced a new 615-row scan. Bootstrap neither verifies default settings/zero prior-state counts nor enters first-run onboarding after reset. This is an observed mandatory failure, not missing evidence. | Carry a verified reset-complete bootstrap state, create/migrate the fresh store, assert default settings and zero prior inventory/history/mappings/pins/ignores, and route to the existing local-only onboarding without starting a scan automatically. | Add fresh-store/default/onboarding regressions and repeat the approved destructive lifecycle on the supported host. Verify onboarding appears before any scan and no previous state is visible. |
| `FRA-04` | High | Release definition of done, ADR-001/002/008/009, `TM-18` | `scripts/New-PrivateBetaPackage.ps1:33–71,90–92`; `artifacts/private-beta/reports/development-signing.json`; `docs/release/PRIVATE_BETA_CHECKLIST.md:13,28` | The candidate is signed only with a removed, intentionally untrusted development certificate. Independent validation now returns `UnknownError` for all 13 owned files, and the archive is unsigned. No approved recipient list, trusted channel, rollback artifact, support contact, or distribution approval is recorded. External private-beta distribution is prohibited by the mandatory signing gate. | Obtain the product-owner-approved release identity and private channel, sign the exact rebuilt payload and required distribution artifacts, bind checksums to the final archive, and record recipient, rollback, and support controls. Do not reuse the temporary certificate or claim trust from signature presence alone. | On a clean supported host, independently verify chain trust, code-signing EKU, signer identity, timestamp/validity, every payload hash, final archive/checksum binding, download-channel integrity, rollback, and zero post-sign mutation. |
| `FRA-05` | High | `AC-UPD-03`, `AC-UPD-06`, ADR-002/018 | `src/PCSteward.Desktop/App.xaml.cs:40–61`; `docs/review/FINAL_RELEASE_EVIDENCE_READINESS.md:28–29` | The revised standalone app correctly composes `UserScopeOnly`, but the only current candidate was HKLM Chrome and no update was run. Historical machine-scope Firefox/helper evidence belongs to the superseded installed/elevated model and cannot prove the current package's per-user no-UAC path or exact post-update result. | Identify a safe, genuinely current-user candidate through the production authority, reconfirm source/ID/scope/versions immediately before execution, obtain explicit approval, and run it through the exact standalone review/executor/verification path. | Record no UAC/helper launch, immutable reviewed fields, provider result, exact post-update source/ID/version re-inventory, independent installed-state corroboration, and cleanup/rollback. A missing candidate remains Unverified, not Pass. |
| `FRA-06` | High | `NFR-PERF-01–07`, `AC-NFR-01/02/06/07/11`, ADR-006 | `docs/review/PERFORMANCE_AND_ACCESSIBILITY_EVIDENCE.md:8,20–25,35`; `docs/review/raw/task-30a-performance.json:61`; `docs/review/TASK_05_ARCHITECTURE_DECISION_REVIEW.md:53` | Mandatory performance acceptance is not met. Overview idle averaged 4.9883% CPU against the 1% ceiling and network activity was unverified; even the lightweight Scan page measured 1.8392%. The current host is not the agreed ADR-006 baseline. The fresh 30/20 harness is fixture-only, and no complete final-app baseline record covers local/healthy-online p95, update responsiveness, cold start, scan process-tree, or real scheduled-host exit. `PO-EXC-29-PERF-01` does not pass the requirement. | Diagnose and remediate the sustained idle work without weakening the threshold. Establish the agreed baseline and fixed power plan, then execute the complete ADR-006 final-app protocol with raw samples and process-attributed CPU/memory/disk/network evidence. | Pass 30 warmed local scans, 20 healthy-online scans, cold start, scan/update responsiveness, five-minute idle, full process-tree resource limits, normal close, and scheduled completion/cancellation/final-timeout exit on the agreed baseline. |
| `FRA-07` | High | `AC-DAT-18`, `NFR-PERF-07`, `AC-NFR-11`, ADR-002/009/020, release definition of done | `docs/release/PRIVATE_BETA_CHECKLIST.md:15–24`; `docs/release/RELEASE_EVIDENCE.md:4,11–16`; `docs/architecture/FACTORY_RESET_DESIGN.md:211–222` | The positive isolated exact-task cleanup is proven, as are archive contents and launch/relaunch retention. However, no recorded supported-host run replaces the extracted package while retaining state, deletes the extracted folder and proves binaries absent, exercises a real scan-only scheduled-host completion/timeout exit, or completes the combined remove-data/package-removal sequence. The release checklist remains unchecked and the release evidence still says lifecycle observations are pending. Static instructions to delete a folder are not package-removal evidence. | Execute the entire fixed-hash standalone lifecycle in disposable caller-writable folders: extraction/launch, exact schedule/action/scan-only behavior, replacement with data retained, cleanup isolation, package-folder deletion, and the separate reset-before-removal choice after `FRA-01–03` are fixed. | Record host/operator/UTC, commands, task XML/action, process exits, before/after hashes, retained state, absent binaries/task/data as applicable, unrelated-task preservation, rollback, and zero elevation/helper activity. |
| `FRA-08` | High | Release definition of done, `FR-UI-05`, ADR-002/021 | `src/PCSteward.Desktop/MainWindow.cs:478–485`; `docs/release/RELEASE_NOTES_0.1.0-private-beta.md:4,9,15–19`; `docs/release/KNOWN_LIMITATIONS.md:6–12` | Mandatory known limitations are not documented in the application: About & support is a placeholder. Release notes also claim “on-demand elevation where required,” contradicting the user-scope-only package, and the limitations file still instructs Task 30A to classify evidence after that task is complete. The documents acknowledge incomplete signing/lifecycle, so they cannot accompany a ready release unchanged. | Implement a native About/limitations surface from reviewed static content, remove the obsolete elevation claim, update limitations and support/rollback/channel language to the final package, and keep unresolved mandatory items labelled blockers until verified. | Inspect the packaged app and release bundle together; confirm user-scope-only behavior, unsupported scope, privacy, data retention/removal, performance status, support/rollback, and signing/channel claims are accurate and consistent. |
| `FRA-09` | Medium | ADR-008, `TM-18`, dependency-licence review | `scripts/New-PrivateBetaPackage.ps1:73–88`; `docs/adr/ADR-008-LICENCE-AND-SOURCE-AVAILABILITY.md:29–38`; `docs/release/PRIVATE_BETA_CHECKLIST.md:29` | The SBOM is generated from all 17 solution projects rather than the shipped payload, producing 99 rows for 33 unique packages and including test/dormant-helper dependencies. It has no licences or dependency relationships; no third-party notices or licence-scan record exists. The hash report contains absolute user-profile paths instead of portable relative payload paths. | Generate a deterministic payload-scoped SBOM with unique components, package relationships, hashes and licence metadata; perform and review the private-distribution licence scan; add required notices; emit only normalized relative paths in distributable manifests. | Reconcile every shipped managed/native dependency and notice to the archive, prove zero missing/extra component, validate CycloneDX, review licence compatibility, and scan evidence/manifests for local-path or personal-data leakage. |
| `FRA-10` | Medium | Approved native design package, design implementation boundary | `design/README.md:3`; `design/CODEX_IMPLEMENTATION_BRIEF.md:17`; `docs/release/PRIVATE_BETA_CHECKLIST.md:23`; `scripts/New-PrivateBetaPackage.ps1:15–31` | The approved product boundary says no browser runtime or embedded WebView, and the release checklist requires proving that absence. The ZIP contains `Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.Core.Projection.dll`, and `WebView2Loader.dll`. No production WebView use was found, but the distributed runtime boundary still fails the approved package check. | Determine the supported Windows App SDK publish configuration that excludes unused WebView runtime files without breaking WinUI. If they are unavoidable, obtain an explicit product-owner/design/architecture revision before distribution rather than silently waiving the boundary. | Rebuild and inspect the exact signed archive for zero browser-runtime entries, then repeat launch/resource/theme/accessibility smoke tests and the full suite; or record and review an approved boundary change with dependency/security impact. |

**Finding count:** 10 open — 0 Blocker, 8 High, 2 Medium, 0 Low.

## Mandatory MVP acceptance criteria

Only `Proven`, `Failed`, and `Unverified` are used. An approved deferral is not converted to Proven.

| Criterion | Status | Independent basis |
|---|---|---|
| `AC-INV-01` | Proven | Registry/MSIX/WinGet fixture and composed scan coverage remains green. |
| `AC-INV-02` | Proven | Conservative normalization preserves distinct scope/architecture instances. |
| `AC-INV-03` | Proven | Static/source review and architecture tests find no `Win32_Product`/WMI inventory path. |
| `AC-INV-04` | Proven | Malformed/incomplete registry fixtures warn or preserve unknown fields without scan failure. |
| `AC-INV-05` | Proven | Approved disabled/local-only process-attributed trace emitted no connector request. |
| `AC-INV-06` | Proven | Normal scan composition and host evidence are standard-user and request no elevation. |
| `AC-INV-07` | Proven | Production UI exposes provider and local/live/cached freshness. |
| `AC-MAT-01` | Proven | Actionable presentation exposes source, stable ID, versions, confidence, and explanation. |
| `AC-MAT-02` | Proven | Low-confidence, unmatched, stale, conflicting, and unavailable evidence remains non-actionable. |
| `AC-MAT-03` | Proven | Fresh zero-false-positive critical matrix passed. |
| `AC-MAT-04` | Proven | Architecture/channel/scope and multi-instance conflicts fail closed. |
| `AC-MAT-05` | Proven | Unsafe/incomparable versions remain Unknown/Review. |
| `AC-MAT-06` | Proven | Source failure retains timestamped stale last-known-good results and warning. |
| `AC-MAT-07` | Proven | Trusted exact-ID mapping creation/reversal is persisted and tested. |
| `AC-UPD-01` | Proven | Immutable explicit review/confirmation is the only runner entry. |
| `AC-UPD-02` | Proven | Typed runner surfaces and fresh negatives reject arbitrary command/path/argument authority. |
| `AC-UPD-03` | Unverified | No controlled current per-user update/no-UAC observation; see `FRA-05`. |
| `AC-UPD-04` | Proven | Accepted standalone scope makes machine updates non-actionable and proves no helper/UAC path at scan, review, or execution. |
| `AC-UPD-05` | Proven | Denial/cancellation remain distinct in retained helper evidence and current outcome regressions. |
| `AC-UPD-06` | Unverified | No exact post-update observation through the current user-scope package; see `FRA-05`. |
| `AC-UPD-07` | Proven | Non-zero/timeout/overflow/cancellation outcomes cannot become Success. |
| `AC-UPD-08` | Proven | Success requires exact bounded post-update version evidence; mismatch remains verification-required/inconclusive. |
| `AC-UPD-09` | Proven | Batch execution and independent histories are sequentially tested. |
| `AC-UPD-10` | Proven | Cancellation preserves completed/running evidence and stops only unstarted work. |
| `AC-UPD-11` | Proven | Typed, timestamped, sanitized local history fields are persisted and tested. |
| `AC-DAT-01` | Proven | Persistence tests and actual package close/relaunch retained the 674-row state. |
| `AC-DAT-02` | Proven | CSV/JSON snapshot fidelity and timestamp coverage pass. |
| `AC-DAT-03` | Proven | Inventory/history and cached-state paths remain operable offline. |
| `AC-DAT-04` | Proven | Accepted process-attributed enabled/disabled trace had zero lost events. |
| `AC-DAT-05` | Proven | No account/cloud identity capability or dependency exists. |
| `AC-DAT-06` | Proven | Transactional Clear scan data returns to no-scan state and removes the fixed categories. |
| `AC-DAT-07` | Proven | Settings/schedule/history/downloads/decisions are preserved and reapplied as required. |
| `AC-DAT-08` | Proven | Inactive cache cleanup, size/residual accounting, and category isolation pass. |
| `AC-DAT-09` | Proven | Completion/storage presentation exposes recoverable owned bytes and cleanup action. |
| `AC-DAT-10` | Proven | Fixed app-owned roots and provider-cache exclusion pass static and hostile fixtures. |
| `AC-DAT-11` | Proven | Locked owned artifacts produce Partial with retained ledger/residual evidence. |
| `AC-DAT-12` | Failed | Reset does not prove exact task absence and can race state operations; see `FRA-01/02`. |
| `AC-DAT-13` | Failed | Actual restart skipped onboarding; see `FRA-03`. |
| `AC-DAT-14` | Proven | Current per-user clear/reset path requests no elevation. |
| `AC-DAT-15` | Failed | In-process flags do not exclude scheduled/second-process data operations; see `FRA-01`. |
| `AC-DAT-16` | Proven | Existing production safe-delete hostile fixtures preserve outside sentinels; reset-specific matrix remains a release-definition gap. |
| `AC-DAT-17` | Proven | Structured bounded diagnostics and sanitizer/credential/path fixtures pass. |
| `AC-DAT-18` | Unverified | Exact task isolation is proven, but complete package-folder removal/replacement/remove-data lifecycle is not; see `FRA-07`. |
| `AC-NFR-01` | Unverified | Fixture-only 30-run data is not final-app agreed-baseline evidence. |
| `AC-NFR-02` | Unverified | Fixture sources are not a 20-run healthy live final-app baseline. |
| `AC-NFR-03` | Proven | Keyboard cancellation recovery and contained provider cancellation meet the recorded limits. |
| `AC-NFR-04` | Proven | Termination recovery retains the prior successful snapshot. |
| `AC-NFR-05` | Proven | Connector timeout/fault isolation and partial publication pass. |
| `AC-NFR-06` | Failed | Actual five-minute idle CPU exceeds 1% and network is unverified. |
| `AC-NFR-07` | Unverified | The retained scan sample is not from the agreed ADR-006 baseline. |
| `AC-NFR-08` | Proven | Actual normal close left no PC Steward process after 30 seconds. |
| `AC-NFR-09` | Proven | Measured single-provider concurrency and second-scan rejection pass. |
| `AC-NFR-10` | Proven | Quota/oldest-inactive/active-artifact protection matrix passes. |
| `AC-NFR-11` | Unverified | No real scheduled scan completion/cancellation/final-timeout exit observation exists. |

**Mandatory acceptance count:** 54 — 43 Proven, 4 Failed, 7 Unverified.

`AC-UX-01–09` remain Optional — best effort under ADR-021. Current named-control/dark-theme evidence is useful, but the full keyboard, Narrator, high-contrast, scaling, and visual-mode matrix remains incomplete and is not promoted to acceptance.

## Mandatory non-functional requirements

| Requirement | Status | Independent basis |
|---|---|---|
| `NFR-PERF-01` | Unverified | No 30-run final-app local inventory on the agreed baseline. |
| `NFR-PERF-02` | Unverified | No 20-run healthy live-source final-app check on the agreed baseline. |
| `NFR-PERF-03` | Unverified | Scan responsiveness is measured, but current-package update responsiveness lacks the required field path. |
| `NFR-PERF-04` | Unverified | No recorded agreed-baseline cold-start-to-interactive-shell measurement. |
| `NFR-PERF-05` | Failed | 4.9883% Overview and 1.8392% lightweight-page idle CPU exceed 1%; network is unverified. |
| `NFR-PERF-06` | Unverified | Retained scan CPU/memory sample is not an agreed-baseline measurement. |
| `NFR-PERF-07` | Unverified | Normal close passes; real scheduled completion/cancellation/final-timeout exit is absent. |
| `NFR-PERF-08` | Proven | Measured cancellation acknowledgment/recovery and child containment pass. |
| `NFR-PERF-09` | Proven | One-scan lease/concurrency behavior is measured and tested. |
| `NFR-PERF-10` | Proven | Configurable 2 GB quota and active-artifact exclusion pass. |
| `NFR-REL-01` | Proven | Connector faults retain other successful results. |
| `NFR-REL-02` | Proven | Transactional inventory promotion and crash recovery retain last-known-good state. |
| `NFR-REL-03` | Proven | Interrupted/cancelled batches preserve completed and verification-required states. |
| `NFR-REL-04` | Proven | Cached results retain timestamp and non-live status. |
| `NFR-REL-05` | Proven | Malformed/missing registry fixtures are contained. |
| `NFR-MNT-01` | Proven | Capability-specific provider/connector interfaces and independent failures are present. |
| `NFR-MNT-02` | Proven | Matching/version rules have UI-free unit coverage. |
| `NFR-MNT-03` | Proven | Provider command/parser construction is outside presentation. |
| `NFR-MNT-04` | Proven | Versioned SQLite migration ownership and development recovery evidence are retained. |
| `NFR-MNT-05` | Proven | Security/deployment decisions are recorded in ADR-001–021. |
| `NFR-OBS-01` | Proven | Scan/update correlation IDs are typed and tested. |
| `NFR-OBS-02` | Proven | Structured bounded severity events are tested. |
| `NFR-OBS-03` | Proven | Cancellation, denial, timeout, verification, provider, and product failures remain distinct. |
| `NFR-OBS-04` | Proven | Bounded sink/log retention controls and tests are present. |
| `NFR-OBS-05` | Proven | Sanitized diagnostic export behavior and hostile-string fixtures pass. |

**Mandatory NFR count:** 25 — 18 Proven, 1 Failed, 6 Unverified.

## Release definition of done

| Release item | Status | Judgment |
|---|---|---|
| All mandatory acceptance criteria pass | Failed | Four Failed and seven Unverified criteria remain. |
| Zero-false-positive fixture passes | Proven | Fresh critical matrix passed with zero executable false positive. |
| Threat model reviewed against implemented architecture | Failed | Review found the Task 28 reset implementation contradicts the accepted `TM-16` coordination/verification controls. |
| Release binaries signed before external distribution | Failed | Only an untrusted removed development signer exists; see `FRA-04`. |
| Critical execution/privilege code has automated tests and manual review | Proven | Fresh Security/Architecture critical suites and full source review pass for the current user-scope boundary. |
| Dependency, static-analysis, and secret-scanning checks pass | Proven | Release analyzers/build pass, NuGet advisory count is zero, and classified tracked-file scan found no credential. Licence completeness is separately open under `FRA-09`. |
| Release SBOM generated | Proven | CycloneDX 1.5 file exists and parses; payload/licence quality remains `FRA-09`. |
| Standalone launch/replacement/task cleanup/package removal/factory reset tested | Failed | Launch and task isolation pass; reset failed and replacement/full removal remain unexecuted. |
| Scan and idle acceptance pass on agreed baseline | Failed | Baseline scans are Unverified and idle CPU Failed. |
| Destructive path-safety, locked-file, and partial-reset tests pass | Unverified | Cache path/locked evidence passes; the accepted reset-specific hostile/partial matrix is absent. |
| Known limitations documented in app and release notes | Failed | The app page is a placeholder and release text is inconsistent/stale; see `FRA-08`. |
| Product owner completed real-PC local-only scan before update execution | Proven | Thirty keyboard-started local-only final-app scans were recorded before later update execution work. |

## Residual risks requiring explicit product-owner acceptance

These risks do not override the open findings. They may be accepted only after mandatory remediation passes:

- VM execution remains excluded by explicit product-owner direction; the final supported-host evidence must still identify the actual physical test environment.
- ADR-021 keeps `AC-UX-01–09` Optional — best effort; incomplete Narrator/high-contrast/scaling matrices may be accepted only without claiming accessibility conformance.
- Same-user substitution of the name-based temporary WinGet export, imperfect package-ID/process-name running-app correlation, provider-disconnect cancellation, and replay of the historical outcome-dialog crash remain bounded documented risks.
- The old protected installed-helper/UAC evidence remains historical evidence only. The standalone payload must continue to contain and compose no elevated helper or machine-scope execution.
- A caller-writable standalone extraction cannot provide installed-binary tamper resistance. Acceptance requires trusted signing/channel/checksum controls and explicit user guidance; it cannot be described as equivalent to a protected installation.

## Required sequence

Run Task 90 under GPT-5.6 Terra / High, ordered `FRA-01` through `FRA-08`, then `FRA-09/10`. Every finding requires a regression or exact field verification. After remediation is committed, repeat Task 31 in a fresh GPT-5.6 Sol / Extra High review. Do not release, tag, publish, distribute, or call the MVP ready before a fresh audit returns READY.
