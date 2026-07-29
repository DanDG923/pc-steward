# MVP requirement traceability

This matrix maps every mandatory, partial-MVP, and desirable-MVP item to at least one numbered Codex task. Review tasks verify the whole phase even when not repeated below.

Every independent phase/design/release gate is preceded by a Terra/High evidence-readiness task (`14A`, `19A`, `24A`, `26A`, or `30A`). Its committed readiness report maps mandatory criteria to reproducible sanitized evidence, a recorded product-owner exception, or a blocking next action; a missing/stale report is itself unproven gate evidence.

## Phase 0 evidence links

| Task | Requirement IDs informed | Evidence |
|---|---|---|
| 02 — packaging and WinGet spike | `FR-DAT-12`, `AC-DAT-18`, `FR-INV-05`, `FR-SRC-01–05`, `FR-UPD-02–04`, `NFR-MNT-03` | `spikes/winget-packaging/SPIKE_REPORT.md`; informs Provisional ADR-002 and ADR-003. |
| 03 — privilege and safe-deletion spike | `FR-UPD-04`, `FR-DAT-08`, `FR-DAT-11–15`, `AC-DAT-11–16`, `AC-UPD-03` | `spikes/security-boundaries/SPIKE_REPORT.md`; 42 standard-host assertions passed. Task 90's approved isolated local-PC fixture adds 14 assertions against the production cache for symbolic links, scratch-VHD mount, and deterministic root/ancestor races; informs ADR-018/019 and Provisional ADR-011/013. |
| 04 — performance baseline and fixture matrix | `NFR-PERF-01–02`, `NFR-PERF-06`, `NFR-PERF-08–09`, `AC-NFR-01–03`, `AC-NFR-07`, `AC-NFR-09` | `spikes/performance-fixtures/BASELINE.md`; 320 fixtures, 30 local and 20 simulated-online samples; supports Accepted ADR-006 measurement protocol, not final compliance. |
| 05 — architecture decision lock | `OD-01–13`, `NFR-MNT-01–05`, `NFR-OBS-01–05`, `FR-UPD-01–04`, `FR-DAT-06–15`, `FR-SCH-01–05` | `docs/review/TASK_05_ARCHITECTURE_DECISION_REVIEW.md`; ADR-001–020 leave no Proposed decisions and define four Provisional evidence gates. |
| 06 — solution and guardrail scaffold | `NFR-MNT-01–05`, `NFR-OBS-01–05`, `NFR-PERF-01–10`, `FR-UPD-02–04`, `FR-DAT-06–15`, `FR-SCH-02–04` | `PCSteward.slnx`, central build settings, architecture-test project, Windows-only CI and command scripts establish scaffold evidence only; no feature requirement is marked complete. |
| 07 — core domain and SQLite persistence | `NFR-REL-02`, `NFR-MNT-04`, `FR-ID-01`, `AC-DAT-01`, `AC-DAT-06–07` | Domain lifecycle records, SQLite migration, atomic inventory promotion and stable-decision preservation tests; inventory and UI requirements remain unimplemented. |
| 08 — registry inventory providers | `FR-INV-01–03`, `FR-INV-06`, `FR-INV-08–09`, `AC-INV-01`, `AC-INV-03–04`, `AC-INV-06–07`, `NFR-REL-05` | Injectable read-only uninstall-registry provider with 64-bit, 32-bit and current-user fixtures; no elevation, MSI, WMI or unrelated registry enumeration. |
| 09 — MSIX/AppX inventory provider | `FR-INV-04`, `FR-INV-06`, `FR-INV-08`, `AC-INV-01/04/06/07` | Current-user package catalog boundary with retained identities, conservative framework/resource/system classification, warnings and fixtures. |
| 10 — WinGet installed inventory | `FR-INV-05–06/08`, `AC-INV-01/04/05/06/07` | Read-only fixed-executable list adapter with bounded output, timeout/exit classification and safe parser fixtures; no source refresh or update command. |
| 11 — normalization and conservative deduplication | `FR-INV-07/09`, `FR-ID-01`, `AC-INV-02`, `NFR-MNT-02` | Deterministic provider-identity fingerprints and fixture matrix preserve raw provenance while rejecting scope, architecture, version, channel and near-name over-grouping. |
| 12 — scan orchestration and measurement | `FR-RSC-01–03/05`, `FR-HIS-01`, `NFR-REL-02`, `NFR-MNT-01/04`, `NFR-OBS-01–02`, `AC-NFR-03–04/09` | Exclusive cancellable local orchestration, provider timing/warning isolation, and last-known-good publish tests; final UI/process performance evidence remains unmeasured. |
| 13 — app shell, onboarding and inventory experience | `FR-ONB-01–04`, `FR-UI-02–03/05/07`, `FR-RSC-01`, `AC-UX-01–04/06/09` | Local-only onboarding, connector trust/toggle model, accessible scan controls, local inventory search and raw-provider installation detail. Desktop accessibility inspection passed; a clean local interactive scan completion retest remains outstanding. |
| 14 — exports and Clear scan data | `FR-DAT-01–03/06–07/10/13–15`, `AC-DAT-01–03/05–07/14–15/17`, `AC-UX-02/07` | Snapshot-backed CSV/JSON export with privacy warning and Phase 3 history placeholder; confirmed, transactionally idempotent clear-data lifecycle preserves decisions, mappings, update history and downloaded artifacts. Integration tests cover export fidelity, offline persistence, no-scan reset and preserve/delete semantics. |
| 15 — Phase 1 review gate | All Phase 1 requirements and criteria | Fresh review through `5fcaf3e` passes the Phase 1 gate with the product-owner-approved temporary `AC-NFR-06` exception. P1RRR-01–04 are closed by final-app cancellation/resource, active-termination recovery, measured concurrency/second-scan rejection and 150% text-scaling evidence. See `docs/review/PHASE_1_REVIEW.md`. |
| 90 — Phase 1 review remediation | `FR-INV-04–09`, `FR-RSC-03/05`, `FR-HIS-01`, `FR-UI-02/05/07`, `FR-DAT-10`, `NFR-REL-02`, `NFR-MNT-01/05`, `NFR-OBS-01–02`, `AC-INV-02/05`, `AC-NFR-03/04/07/09`, `AC-UX-01/02/04/06/09` | Explicit WinGet approval/fail-closed persistence, contained timeout recovery, typed metadata/raw diagnostics/events, presentation boundary, stable filters and final-app baseline evidence are recorded in `docs/review/TASK_90_P1RR_EVIDENCE.md`. P1RRR evidence records 236-ms keyboard cancellation recovery, a 2.3173%/194.7-MB active-scan sample, active-scan termination recovery, measured single-provider concurrency/second-scan rejection, and a restored 150% text-scale exercise. The five-minute CPU result remains a temporary product-owner exception. |
| 16 — version comparison engine | `FR-VER-01–05`, `NFR-MNT-02`, `AC-MAT-04–05` | Pure bounded numeric-dotted parser and comparison table returns explicit newer/equal/older/incomparable/connector-deferred outcomes. Unit cases cover omitted components, leading zeros, recognized prereleases, stable-to-preview blocking, vendor suffixes, malformed values, connector deferral and downgrade blocking. |
| 17 — deterministic matching and confidence engine | `FR-ID-01–07`, `AC-MAT-01–05/07` | Pure matching resolves only unique high-confidence exact package/family/MSI identity or supported trusted user/imported mappings. Publisher-qualified aliases remain review-only; architecture, scope, channel, version, ambiguity, trust and manual-target validation fail closed. Curated true-match, non-match and near-name/publisher fixtures assert one actionable match and zero false-positive actionable mappings. |
| 18 — WinGet update intelligence, source cache and freshness | `FR-SRC-01–05/10`, `FR-UI-01/04–06`, `NFR-PERF-02`, `NFR-REL-01/04`, `NFR-MNT-01/03`, `AC-MAT-01/03–06`, `AC-DAT-04`, `AC-NFR-02/05`, `AC-UX-06` | Fixed WinGet/Store-only source connectors use bounded field/output parsing, explicit online refresh, cache timestamps and independently fail closed. Cached source results remain visibly stale on failure/offline; deterministic intelligence emits current, verified, review, unmanaged, pinned, ignored and unavailable states. Fixture tests cover healthy/stale/malformed/timeout/compatibility/offline paths and a 20-run fixture online benchmark. An approved elevated ETW run now supplies the event-based enabled/disabled, process-attributed `AC-DAT-04` evidence with zero lost events; real-row parser evidence remains pending. See `docs/review/TASK_90_P2R_EVIDENCE.md`. |
| 24 — downloaded-artifact cache and safe cleanup | `FR-DAT-08–11/13–15`, `NFR-PERF-10`, `AC-DAT-08–11/14–17`, `AC-NFR-10`, `AC-UX-07` | SQLite ownership ledger persists relative path, size, hash, job association, outcome, active lease and last-cleaned time. A literal per-user PC Steward download root uses canonical containment, ADS/UNC/device rejection, reparse-component rejection, and a non-following handle-bound delete. Retention (seven-day verified / 24-hour failed-incomplete), 2 GB soft quota, oldest-inactive eviction, locked residuals, idempotence and traversal preservation are covered by focused fixtures. Settings and update-completion cleanup disclose and preserve all provider caches. |
| 24A — Phase 3 evidence readiness | `FR-UPD-01–11`, `FR-DAT-08–15`, `NFR-REL-03`, `NFR-OBS-01–05`, `NFR-PERF-10`, `AC-UPD-*`, `AC-DAT-*`, `AC-NFR-10`, `TM-03–06/12/15` | `docs/review/PHASE_3_EVIDENCE_READINESS.md` records passing static, hostile-fixture, security, architecture, cache/batch/diagnostic, build and full-suite evidence. The product-owner-approved [host-test exception](../review/PHASE_3_HOST_TEST_EXCEPTION.md) adds valid temporary signing, no-op UAC acceptance/denial and pre-provider teardown, plus 10 isolated symbolic-link/race/scratch-VHD assertions with complete cleanup. A separately approved Firefox exercise proved a real reviewed machine-scope `152.0.6` → `153.0.1` update by exact post-update WinGet re-inventory; the fixture and temporary certificate were removed afterward. The app crash before outcome presentation is documented with packaging/dialog-lifecycle remediations and green regression evidence; no second live replay was performed. Task 24A is ready for the required Task 25 review, which must run under Sol / Extra High. |
| 19 — Manual decisions and update-intelligence UX | `FR-POL-01–05`, `FR-UI-01–07`, `FR-HIS-02/04`, `AC-MAT-02/07`, `AC-UX-01–06` | Durable user-decision service stores optional reasons and ignore expiry, allows only exact package IDs supplied by enabled trusted connectors, supports reversal and policy reapplication, and preserves decisions independently of scan data. The presentation model exposes counts, attention, freshness, per-product safety evidence and a disabled-update explanation; desktop details support current-version pin and indefinite ignore without creating execution authority. |
| 90 — Phase 3 review remediation | `FR-UPD-02–06/08–09`, `FR-DAT-08/10/15`, `NFR-MNT-03`, `NFR-REL-03`, `NFR-OBS-03`, `NFR-PERF-10`, `AC-UPD-01–02/04/06/08/10`, `AC-DAT-08/11/16`, `AC-NFR-10`, `TM-01–06/10–13/15` | [Final P3R-14/P3R-15 evidence](../review/TASK_90_P3R14_P3R15_EVIDENCE.md) composes the official WinGet COM structured-candidate authority in the actual desktop, binds exact trusted source/ID/installed/candidate versions through review and targeted verification, and leaves display tables non-executable. The approved [raw host record](../review/TASK_90_P3R15_HOST_EVIDENCE.json) proves the real fixed-root validator/default transport with exact protected ACLs, valid same-signer binaries and a completed signed no-op UAC path; real tamper, trusted-wrong-signer, caller-writable, helper-reparse and between-validation replacement layouts all have zero helper launches, followed by verified root/certificate teardown. The final running-target gate remains closed. |
| 25 — Phase 3 security review gate | All Phase 3 requirements and criteria | Fresh GPT-5.6 Sol / Extra High review at exact implementation commit `5013c942ae2fa0dc62c9bf71258bcbffedd03347` passes the gate with `P3R-01–15` closed and no remaining Blocker, High or Medium finding. Focused 61, security 19, architecture 5, Release build, full suite 182, hostile 42/1 approved skip, WinGet 11, dependency and read-only installed-helper teardown checks pass. See [Phase 3 security review](../review/PHASE_3_SECURITY_REVIEW.md). Phase 3 was subsequently merged through `b473792`. |
| Design visual foundation — supporting Task 26 | `AC-UX-02–04` (Optional — best effort) | Central WinUI semantic light/dark/high-contrast resources, typography, reusable control styles, and the approved PC Steward identity assets are integrated into the desktop host. This is implementation plumbing only: it does not claim accessibility acceptance from appearance, alter existing workflows, or implement Task 26 scheduling/notifications. |
| 26 — scheduled checks and Windows notifications | `FR-SCH-01–05`, `FR-RSC-04`, `NFR-PERF-07`, `AC-NFR-11`, `AC-UX-02/04/05` | Persisted Manual/Daily/Weekly settings, a fixed-action current-user Task Scheduler adapter, a scan-only low-priority one-shot host, overlap mutex, bounded exit, repeated-failure/attention-only notification policy, and the NavigationView Settings surface are covered by 111 focused integration tests and the 187-test full suite. The host has no execution/elevation reference and receives no task arguments. A live schedule was not created because the required explicit approval was not supplied; the visual foundation remains optional best-effort evidence, not accessibility acceptance. |
| 26B — native shell, onboarding and scan experience | `FR-ONB-01–04`, `FR-UI-02–03/05/07`, `FR-RSC-01–03/05`, `FR-HIS-01`, `AC-UX-01–04/06/09` | Navigation routes retain the active operation banner and present a dedicated scan screen with local-only guidance, explicit read-only/no-elevation scope, cancellation, prior-snapshot retention and text provider-warning status. The state matrix includes partial/failed scan warnings with prior freshness retained. Release build passed with 0 warnings/errors and 188 tests passed; visual/accessibility evidence remains best effort only. |
| 26C — applications, findings and update review experience | `FR-UI-01–07`, `FR-ID-01–07`, `FR-SRC-01–05/10`, `FR-POL-01–05`, `FR-UPD-01–11`, `AC-MAT-01–07`, `AC-UPD-01–11`, `AC-UX-02–06` | Updates route presents only typed application evidence, with source, package ID, versions, architecture, channel, confidence and freshness; non-verified/stale/unavailable evidence has an explicit non-actionable explanation. Existing immutable reviewed-update workflow remains the sole execution path. Release build passed with 0 warnings/errors and 188 tests passed. |
| 26A — factory-reset design evidence readiness | `FR-DAT-04/12–15`, `AC-DAT-12–16/18`, `AC-UX-07–08`, ADR-013/019 | `review/FACTORY_RESET_DESIGN_EVIDENCE_READINESS.md` inventories fixed roots, retention, active-work locks, scheduler ownership, next-launch marker/handle sequence, hostile-path coverage and the two-dialog UI contract. It records the reset-manifest hostile fixture as Missing with a precise Task 28 action; no reset was implemented or exercised. |
| 27 — factory-reset design gate | `FR-DAT-04/12–15`, `AC-DAT-12–16/18`, `AC-UX-02/04/07–08`, ADR-013/019 | `architecture/FACTORY_RESET_DESIGN.md` accepts the fixed next-launch marker/no-helper mechanism after inspecting actual SQLite, logging, cache and scheduled-host lifetimes. It freezes the logical/physical manifest, shared cross-process lease, marker-before-store ordering, transaction/failure/verification rules, advanced controls, uninstall interaction, hostile fixture matrix and two-dialog native UI contract. ADR-013 is Accepted; Release build passed with 0 warnings/errors and the full suite passed 188/188. Task 28 implementation evidence remains mandatory. |
| 28 — factory reset and advanced data controls | `FR-DAT-04/12–15`, `AC-DAT-12–16/18`, `AC-UX-02/04/07–08`, ADR-013/019 | Native controls independently clear source metadata, logs, update history, and mappings/policies with transaction boundaries and Cancel-default reviews. Factory reset requires scope review then exact case-sensitive `RESET`, writes a fixed marker, closes, and is completed at desktop bootstrap before SQLite composition. The scheduled host exits on sentinel preflight. The fixed-root coordinator removes the manifest database leaves, logs/downloads and current-user schedule, retains a marker on a partial result, and uses the existing non-following owned-root primitive. Focused integration: 17 passed. |
| 21 — immutable update jobs and constrained non-elevated runner | `FR-UPD-01–04/07`, `NFR-MNT-03`, `NFR-OBS-01–03`, `AC-UPD-01–05/07/11` | Explicit confirmation creates a get-only typed job snapshot with a canonical digest; SQLite appends it and rejects mutated rows. Fresh exact evidence is revalidated immediately before execution. The WinGet adapter owns the fixed executable and exact argument construction, accepts no command/path/environment input, runs user-scope jobs only, and returns machine scope as elevation-required without execution. Results distinguish denial, elevation, cancellation, timeout, output overflow, product failure, provider failure and awaiting verification; exit zero is never Success. Release build passed with zero warnings/errors; 109 tests passed, including injection, mutation, fixed-surface, sanitization and non-zero-exit fixtures. |
| 22 — short-lived elevated helper and secure IPC | `FR-UPD-04`, `AC-UPD-03–05`, `TM-04` | Machine scope alone reaches an on-demand `runas` launch after fresh immutable-job revalidation. A current-user/SYSTEM, local-only, single-instance random pipe binds both process IDs, user SID, session, fixed paths, trusted Authenticode signatures and the same signer. Bootstrap carries only protocol/PID/pipe/nonce; the one-shot bounded request rejects replay, expiry, mutation, unknown/duplicate fields, paths and free-form authority. The machine-only WinGet adapter reconstructs fixed arguments; denial, cancellation, timeout, crash/failure, provider failure and completion remain distinct, sanitized, and never Success before Task 23 verification. The approved host substitute recorded valid temporary signatures, UAC acceptance to pre-provider exit `24`, UAC denial without helper launch, and zero residual process/pipe/certificate state; it does not prove per-user no-prompt, provider-disconnect cancellation, or clean-image behavior. |
| 23 — sequential batches, verification, history and diagnostics | `FR-UPD-05–06/08–11`, `FR-HIS-01–06`, `NFR-REL-03`, `NFR-OBS-01–05`, `AC-UPD-06–11`, `AC-UX-05` | Typed sequential batch coordinator records append-only queued/running/outcome events and never exposes force-close authority. Cancellation marks only unstarted jobs cancelled; a started item whose provider/verification state is interrupted remains `VerificationRequired`, including crash recovery, rather than being relabelled queued/cancelled. A successful provider result is verified by targeted re-inventory before it becomes Success; otherwise it is Restart required, Inconclusive, or Verification failed. SQLite retains per-product timelines and bounded verbose events. Sanitized copyable JSON diagnostics redact credentials, paths and control characters. Controlled fixtures cover independent batches, cancellation, crash recovery, negative source/download/installer/elevation/timeout outcomes, persistence, retention and diagnostics. |

| ID | Requirement / criterion | MVP marker | Task(s) |
|---|---|---|---|
| `FR-ONB-01` | Explain local inventory collection, network source access, permissions and data storage before the first scan. | Yes | 13 |
| `FR-ONB-02` | Permit a local-only first scan. | Yes | 13 |
| `FR-ONB-03` | Show all enabled connectors and their trust category. | Yes | 13 |
| `FR-ONB-04` | Allow the user to disable nonessential connectors. | Yes | 13 |
| `FR-INV-01` | Read machine-wide 64-bit uninstall registry entries. | Yes | 08 |
| `FR-INV-02` | Read machine-wide 32-bit uninstall registry entries. | Yes | 08 |
| `FR-INV-03` | Read current-user uninstall registry entries. | Yes | 08 |
| `FR-INV-04` | Enumerate installed MSIX/AppX packages visible to the current user. | Yes | 09 |
| `FR-INV-05` | Import installed-application information reported by WinGet. | Yes | 10 |
| `FR-INV-06` | Preserve raw provider records for diagnosis while presenting normalized products in the UI. | Yes | 08, 09, 10 |
| `FR-INV-07` | Deduplicate equivalent records without merging genuinely separate user/machine or architecture instances. | Yes | 11 |
| `FR-INV-08` | Capture name, version, publisher, install scope, architecture, source provider, install date and path where safely available. | Yes | 08, 09, 10 |
| `FR-INV-09` | Classify runtimes, redistributables, drivers, system components and applications separately where evidence allows. | Partial | 08, 11 |
| `FR-ID-01` | Maintain a canonical product identity separate from each install instance. | Yes | 11, 17 |
| `FR-ID-02` | Store exact package IDs returned by trusted connectors. | Yes | 17 |
| `FR-ID-03` | Compute a deterministic confidence level and explanation for each mapping. | Yes | 17 |
| `FR-ID-04` | Prevent low-confidence candidates from becoming executable update actions. | Yes | 17 |
| `FR-ID-05` | Allow a user to create, review and delete a manual mapping to a trusted connector package. | Yes | 17 |
| `FR-ID-06` | Preserve mapping origin: automatic, provider-supplied, user-confirmed or imported. | Yes | 17 |
| `FR-ID-07` | Detect channel conflicts such as Stable versus Beta where source metadata permits. | Partial | 17 |
| `FR-SRC-01` | Support the WinGet default package source through a connector abstraction. | Yes | 18 |
| `FR-SRC-02` | Support Microsoft Store package information available through WinGet. | Yes | 18 |
| `FR-SRC-03` | Refresh source metadata on demand and expose the last successful refresh time. | Yes | 18 |
| `FR-SRC-04` | Cache source results so previous data remains visible during a temporary outage. | Yes | 18 |
| `FR-SRC-05` | Treat source metadata as untrusted and validate field lengths, formats and allowed values. | Yes | 18 |
| `FR-SRC-06` | Permit a user-confirmed mapping to an official GitHub Releases repository for read-only version checks. | Desirable MVP | 18B |
| `FR-SRC-10` | Let the user add only connector types supported by the product; no raw command connector. | Yes | 18 |
| `FR-VER-01` | Compare numeric dotted versions correctly when both sides provide them. | Yes | 16 |
| `FR-VER-02` | Handle prerelease labels and avoid offering Beta/Preview builds to Stable users by default. | Yes | 16 |
| `FR-VER-03` | Defer to connector-specific version semantics where generic comparison is unsafe. | Yes | 16 |
| `FR-VER-04` | Mark a result as unknown rather than inventing an ordering for incomparable versions. | Yes | 16 |
| `FR-VER-05` | Prevent a downgrade unless the user enters a dedicated future downgrade workflow. | Yes | 16 |
| `FR-UI-01` | Show counts for verified updates, review-required updates, current, unmanaged, pinned, ignored and failed items. | Yes | 18, 19 |
| `FR-UI-02` | Support search, sorting and filtering by status, publisher, source and scope. | Yes | 13, 19 |
| `FR-UI-03` | Show an application detail page with installed and candidate metadata side by side. | Yes | 13, 19 |
| `FR-UI-04` | Display source provenance, package ID and confidence explanation before an update action. | Yes | 18, 19 |
| `FR-UI-05` | Show last scan time and whether the displayed result is live or cached. | Yes | 13, 18, 19 |
| `FR-UI-06` | Provide a compact “attention needed” view. | Yes | 18, 19 |
| `FR-UI-07` | Provide a detailed diagnostics view without exposing unnecessary implementation noise by default. | Yes | 13, 19 |
| `FR-UPD-01` | Require explicit selection and confirmation before every update or batch. | Yes | 21 |
| `FR-UPD-02` | Execute only provider-defined, structured operations against an immutable package ID. | Yes | 21 |
| `FR-UPD-03` | Never execute a command string retrieved from the local database or remote metadata. | Yes | 21 |
| `FR-UPD-04` | Request elevation only when the provider determines it is needed. | Yes | 21, 22 |
| `FR-UPD-05` | Warn when a target application appears to be running. | Yes | 23 |
| `FR-UPD-06` | Do not force-close an application without explicit consent. | Yes | 23 |
| `FR-UPD-07` | Capture start time, end time, provider, package ID, target version, exit code and sanitized output. | Yes | 21 |
| `FR-UPD-08` | Re-inventory the target after the installer exits. | Yes | 23 |
| `FR-UPD-09` | Report success only when the expected version is detected or the provider supplies a reliable restart-pending result. | Yes | 23 |
| `FR-UPD-10` | Run MVP batch updates sequentially. | Yes | 23 |
| `FR-UPD-11` | Allow cancellation before the next queued update begins. | Yes | 23 |
| `FR-POL-01` | Pin a product to its current version, a major/minor branch or a source channel where supported. | Partial MVP: current version/source package | 19 |
| `FR-POL-02` | Ignore an application indefinitely or until a selected date. | Yes | 19 |
| `FR-POL-03` | Record an optional reason for pinning, ignoring or manually mapping. | Yes | 19 |
| `FR-POL-04` | Show all active decisions in one settings view. | Yes | 19 |
| `FR-POL-05` | Make all decisions reversible. | Yes | 19 |
| `FR-HIS-01` | Store scan summaries and update-job history locally. | Yes | 12, 23 |
| `FR-HIS-02` | Show a timeline for each product. | Yes | 19, 23 |
| `FR-HIS-03` | Distinguish source, download, installer, elevation, timeout and verification failures. | Yes | 23 |
| `FR-HIS-04` | Apply configurable retention limits to verbose logs and source caches. | Yes | 19, 23 |
| `FR-HIS-05` | Allow a user to copy a sanitized diagnostic bundle. | Yes | 23 |
| `FR-HIS-06` | Never include credentials, tokens or full environment dumps in exported diagnostics. | Yes | 23 |
| `FR-SCH-01` | Allow an optional daily, weekly or manual-only scan schedule. | Yes | 26 |
| `FR-SCH-02` | Use Windows Task Scheduler rather than an always-running service. | Yes | 26 |
| `FR-SCH-03` | Scheduled scans may check but must not install updates in the MVP. | Yes | 26 |
| `FR-SCH-04` | Show a Windows notification only when attention is required or a scan fails repeatedly. | Yes | 26 |
| `FR-SCH-05` | Allow the schedule and notifications to be disabled independently. | Yes | 26 |
| `FR-DAT-01` | Export current inventory to CSV and JSON. | Yes | 14 |
| `FR-DAT-02` | Optionally include update history in JSON export. | Yes | 14 |
| `FR-DAT-03` | Warn that exports reveal installed software and potentially identifying machine metadata. | Yes | 14 |
| `FR-DAT-04` | Provide advanced controls to clear source metadata, logs, update history or manual mappings/policies independently. | Yes | 28 |
| `FR-DAT-06` | Provide a dedicated **Clear scan data** button that removes current inventory, raw provider records, update candidates and scan-run records. | Yes | 14 |
| `FR-DAT-07` | **Clear scan data** must preserve global settings, enabled connectors, schedule, update-job history and app-owned downloaded update files. User-created mappings, pins and ignores must be retained in a stable decision store and reapplied when the same product is rediscovered. | Yes | 14 |
| `FR-DAT-08` | Provide a dedicated **Clear downloaded files** button in Settings that deletes only PC Steward-owned update payloads and temporary extraction files not in active use. | Yes | 24 |
| `FR-DAT-09` | Show **Delete downloaded files** on the update-completion view whenever PC Steward-owned files remain, together with the space recoverable. | Yes | 24 |
| `FR-DAT-10` | Display the current size, last-cleaned time and deletion scope for scan data and downloaded update files. | Yes | 14, 24 |
| `FR-DAT-11` | Do not delete Windows, WinGet, Microsoft Store or another provider's cache unless that provider exposes a documented, safe cleanup operation; disclose this boundary in the UI. | Yes | 24 |
| `FR-DAT-12` | Provide a dedicated **Factory reset PC Steward** button that removes all local application data, settings, decisions, caches, logs, consent records and the current-user scheduled task, then returns the next launch to first-run onboarding. The installed app binaries remain. | Yes | 28 |
| `FR-DAT-13` | Require an explicit confirmation for each destructive action and a second confirmation for factory reset. | Yes | 14, 24, 28 |
| `FR-DAT-14` | Disable destructive controls while a dependent scan or update is active, or require the user to cancel the operation first. | Yes | 14, 24, 28 |
| `FR-DAT-15` | Deletion operations must be idempotent, report locked or undeletable items accurately and never claim success while relevant app-owned data remains. | Yes | 14, 24, 28 |
| `FR-RSC-01` | Display scan phase, elapsed time and a **Cancel scan** action for manual scans. | Yes | 12, 13 |
| `FR-RSC-02` | Permit only one active full scan and prevent it from overlapping update execution, except for a controlled post-update verification scan. | Yes | 12 |
| `FR-RSC-03` | Apply bounded connector concurrency, per-provider timeouts and cancellation tokens so one source cannot consume unbounded resources or block the whole scan. | Yes | 12 |
| `FR-RSC-04` | Run scheduled scans through Task Scheduler without an always-running process, at lower priority where supported, and exit after completion, cancellation or timeout. | Yes | 26 |
| `FR-RSC-05` | Record scan duration and provider timings locally for performance diagnosis without collecting unrelated application-usage telemetry. | Yes | 12 |
| `NFR-PERF-01` | Local inventory must complete within 15 seconds at the 95th percentile on the agreed baseline PC with 300 provider records. | Mandatory | 04, 12, 29 |
| `NFR-PERF-02` | A full online check must complete within 90 seconds at the 95th percentile when sources are healthy; source timeouts must not block the whole application indefinitely. | Mandatory | 04, 18, 29 |
| `NFR-PERF-03` | The UI must remain responsive during scans and updates. | Mandatory | 12, 29 |
| `NFR-PERF-04` | The application should cold-start to an interactive shell within five seconds on the baseline PC. | Mandatory | 29 |
| `NFR-PERF-05` | With the UI open and no active job for five minutes, average PC Steward CPU usage must remain at or below 1% of total system CPU, working set should remain at or below 300 MB, and the app must not perform recurring disk or network activity. | Mandatory | 12, 29 |
| `NFR-PERF-06` | During a local scan on the baseline PC, the combined PC Steward process tree must average no more than 20% total CPU and remain below 500 MB peak working set. App-owned work must not sustain more than 50% total CPU for over five seconds. | Mandatory | 04, 12, 29 |
| `NFR-PERF-07` | Closing the UI must leave no resident PC Steward process when no approved update or scheduled scan is active. A scheduled scan process must exit within 30 seconds after completion, cancellation or final timeout handling. | Mandatory | 26, 29 |
| `NFR-PERF-08` | A scan cancellation request must be acknowledged in the UI within two seconds and return the application to an operable state within five seconds, including termination of cancellable child-provider work. | Mandatory | 04, 12, 29 |
| `NFR-PERF-09` | Only one full scan may run at a time. Online connector concurrency defaults to a maximum of three and must be configurable internally without a code rewrite. | Mandatory | 04, 12, 29 |
| `NFR-PERF-10` | PC Steward-owned downloaded update files must be bounded by a configurable soft quota with a 2 GB default, excluding a file actively required by an update. | Mandatory | 24, 29 |
| `NFR-REL-01` | A failed connector must not discard successful results from other connectors. | Mandatory | 18, 29 |
| `NFR-REL-02` | Database writes must be transactional so interrupted scans do not corrupt the last known good inventory. | Mandatory | 12, 29 |
| `NFR-REL-03` | A cancelled or crashed batch must preserve completed job results and leave unstarted jobs clearly queued or cancelled. | Mandatory | 23, 29 |
| `NFR-REL-04` | Cached results must always show their timestamp and must not be presented as a live successful check. | Mandatory | 18, 29 |
| `NFR-REL-05` | The application must recover gracefully from malformed or missing registry values. | Mandatory | 08, 29 |
| `NFR-MNT-01` | Inventory providers and update connectors must implement stable interfaces and fail independently. | Mandatory | 12, 18, 29 |
| `NFR-MNT-02` | Matching and version rules must be unit-testable without launching the UI. | Mandatory | 11, 16, 29 |
| `NFR-MNT-03` | Provider commands, argument construction and result parsing must be isolated from presentation code. | Mandatory | 18, 21, 29 |
| `NFR-MNT-04` | Database migrations must be versioned and reversible during development. | Mandatory | 12, 29 |
| `NFR-MNT-05` | Architecture decisions affecting security or deployment must be recorded under `docs/adr`. | Mandatory | 29 |
| `NFR-OBS-01` | Each scan and update job receives a correlation ID. | Mandatory | 12, 21, 23, 29 |
| `NFR-OBS-02` | Logs use structured events and severity levels. | Mandatory | 12, 21, 23, 29 |
| `NFR-OBS-03` | Diagnostics distinguish user cancellation from product or installer failure. | Mandatory | 21, 23, 29 |
| `NFR-OBS-04` | Logs are bounded by retention and file-size policy. | Mandatory | 23, 29 |
| `NFR-OBS-05` | User-facing diagnostics can be exported in sanitized form. | Mandatory | 23, 29 |
| `AC-INV-01` | Given applications registered in each supported inventory surface, a scan creates a record for every fixture entry expected from that surface. | Mandatory | 08, 09, 10 |
| `AC-INV-02` | Duplicate raw records for the same install instance are presented once, while distinct user/machine or x86/x64 instances remain separately inspectable. | Mandatory | 11 |
| `AC-INV-03` | The scan does not invoke `Win32_Product` and does not trigger MSI repair operations. | Mandatory | 08 |
| `AC-INV-04` | A malformed or incomplete registry entry does not crash the scan; it is either represented with unknown fields or recorded as a provider warning. | Mandatory | 08, 09, 10 |
| `AC-INV-05` | A local-only scan makes no update-source network request. | Mandatory | 10 |
| `AC-INV-06` | A normal scan does not require or request administrator elevation. | Mandatory | 08, 09, 10 |
| `AC-INV-07` | The application shows the provider and freshness of every displayed inventory record. | Mandatory | 08, 09, 10 |
| `AC-MAT-01` | Every actionable update displays a stable package ID, source, candidate version, confidence and explanation. | Mandatory | 17, 18 |
| `AC-MAT-02` | No low-confidence or unmatched candidate exposes an enabled Update button. | Mandatory | 17, 19 |
| `AC-MAT-03` | The curated fixture set produces zero false-positive executable update mappings. | Mandatory | 17, 18 |
| `AC-MAT-04` | Architecture or channel incompatibility prevents an update from being actionable. | Mandatory | 16, 17, 18 |
| `AC-MAT-05` | Version strings that cannot be ordered safely produce an Unknown/Review state rather than a guessed update. | Mandatory | 16, 17, 18 |
| `AC-MAT-06` | A temporary source failure leaves the last cached result visible with a stale timestamp and a source-unavailable warning. | Mandatory | 18 |
| `AC-MAT-07` | The user can create and later delete a manual mapping to a trusted package ID. | Mandatory | 17, 19 |
| `AC-UPD-01` | No installer runs without the user selecting it and confirming the individual or batch review screen. | Mandatory | 21 |
| `AC-UPD-02` | The update runner accepts only approved provider operations and rejects arbitrary executable paths or command strings. | Mandatory | 21 |
| `AC-UPD-03` | A per-user update that does not require elevation completes without a UAC prompt. | Mandatory | 21, 22 |
| `AC-UPD-04` | A machine-wide update requests UAC only when execution begins, not during scanning or review. | Mandatory | 21, 22 |
| `AC-UPD-05` | If the user declines UAC, the job is recorded as cancelled/denied rather than failed or successful. | Mandatory | 21, 22 |
| `AC-UPD-06` | After a successful test update, the post-update scan detects the expected newer version. | Mandatory | 23 |
| `AC-UPD-07` | A non-zero provider or installer result is never displayed as success. | Mandatory | 21, 23 |
| `AC-UPD-08` | If the installer reports success but the expected version is not detected, the result is Inconclusive or Restart required, not Success. | Mandatory | 23 |
| `AC-UPD-09` | Batch execution runs sequentially and reports each item's independent result. | Mandatory | 23 |
| `AC-UPD-10` | Cancelling a batch prevents the next unstarted item from running and preserves completed results. | Mandatory | 23 |
| `AC-UPD-11` | Update jobs record timestamps, provider, package ID, versions, exit code and sanitized diagnostic output. | Mandatory | 21, 23 |
| `AC-DAT-01` | Closing and reopening PC Steward preserves inventory, mappings, decisions and update history. | Mandatory | 14 |
| `AC-DAT-02` | CSV and JSON exports match the displayed current inventory and identify export time. | Mandatory | 14 |
| `AC-DAT-03` | The product functions for inventory and history while offline. | Mandatory | 14 |
| `AC-DAT-04` | Network inspection confirms that the MVP contacts only enabled connectors and the optional PC Steward update endpoint. | Mandatory | 18, 90 |
| `AC-DAT-05` | No account, email address or cloud sign-in is required. | Mandatory | 14 |
| `AC-DAT-06` | Selecting **Clear scan data** removes current inventory, raw scan/provider records, update candidates and scan-run records, and the dashboard returns to a no-scan state. | Mandatory | 14 |
| `AC-DAT-07` | After **Clear scan data**, global settings, enabled sources, schedule, update-job history and app-owned downloaded files remain unchanged. A subsequent scan can reapply a previously saved pin, ignore or manual mapping to the same product. | Mandatory | 14 |
| `AC-DAT-08` | Selecting **Clear downloaded files** removes all inactive files in the PC Steward-owned download-cache root, updates the displayed size to zero where no locked item remains, and does not alter installed applications, scan data, source metadata or update history. | Mandatory | 24 |
| `AC-DAT-09` | When an update completes and app-owned downloaded files remain, the result view shows **Delete downloaded files** and reports the correct recoverable size. | Mandatory | 24 |
| `AC-DAT-10` | Provider-owned Windows, WinGet or Store caches remain untouched unless a documented provider cleanup adapter is explicitly implemented and tested. | Mandatory | 24 |
| `AC-DAT-11` | A locked or protected app-owned file produces an accurate partial-cleanup result and is not silently reported as deleted. | Mandatory | 24 |
| `AC-DAT-12` | Factory reset removes the SQLite database, scan data, source metadata, logs, update history, app-owned downloaded files, settings, consent records, user decisions and current-user scheduled task after two confirmations. | Mandatory | 28 |
| `AC-DAT-13` | After factory reset and restart, first-run onboarding appears and no previous inventory, history, settings, mappings, pins or ignores is visible. PC Steward remains installed. | Mandatory | 28 |
| `AC-DAT-14` | Clear and reset controls require no administrator elevation for the per-user MVP data model. | Mandatory | 14, 24, 28 |
| `AC-DAT-15` | Clear and reset actions are unavailable during an active update or scan until the user cancels or completes it. | Mandatory | 14, 24, 28 |
| `AC-DAT-16` | Cleanup tests prove that a junction, symbolic link or crafted metadata value cannot cause deletion outside the allowlisted PC Steward data roots. | Mandatory | 24, 28 |
| `AC-DAT-17` | Default logs contain no credentials, licence keys, browser data or document contents. | Mandatory | 14, 24 |
| `AC-DAT-18` | Uninstall removes binaries and scheduled tasks and offers a clear choice to retain or remove per-user data. | Mandatory | 28, 30 |
| `AC-UX-01` | A first-time user can complete the first scan and identify available updates without using a command line. | Optional — best effort | 13, 19, 29 |
| `AC-UX-02` | All primary navigation, scan, review, update, clear-data and factory-reset actions are keyboard accessible. | Optional — best effort | 13, 14, 19, 26, 28, 29 |
| `AC-UX-03` | Status is expressed through text/icons as well as colour. | Optional — best effort | 13, 19, 29 |
| `AC-UX-04` | Windows text scaling and high-contrast mode do not hide primary controls or status text. | Optional — best effort | 13, 19, 26, 28, 29 |
| `AC-UX-05` | Every failure view contains a plain-language summary and a safe next action. | Optional — best effort | 19, 23, 26, 29 |
| `AC-UX-06` | The UI always distinguishes live, cached and local-only scan results. | Optional — best effort | 13, 18, 19, 29 |
| `AC-UX-07` | The Data & reset page visibly separates routine cleanup from factory reset and shows what each action deletes, preserves and can recover in storage. | Optional — best effort | 14, 24, 28, 29 |
| `AC-UX-08` | Factory reset requires a second explicit confirmation and cannot be triggered by a single accidental click or keyboard activation. | Optional — best effort | 28, 29 |
| `AC-UX-09` | During a scan, the user can see elapsed time, current phase and an enabled **Cancel scan** action. | Optional — best effort | 13, 29 |
| `AC-NFR-01` | Local inventory meets NFR-PERF-01 on the agreed baseline test system over at least 30 repeated runs after warm-up. | Mandatory | 04, 12, 29 |
| `AC-NFR-02` | Full online scan meets NFR-PERF-02 when test sources are healthy over at least 20 repeated runs. | Mandatory | 04, 18, 29 |
| `AC-NFR-03` | Cancelling a scan or connector request returns the UI to an operable state without data corruption and meets NFR-PERF-08. | Mandatory | 04, 12, 29 |
| `AC-NFR-04` | Terminating the application during a scan leaves the previous successful inventory readable on next launch. | Mandatory | 12, 29 |
| `AC-NFR-05` | A connector timeout does not block results from other connectors. | Mandatory | 18, 29 |
| `AC-NFR-06` | Five-minute idle measurement meets the CPU, memory, disk and network limits in NFR-PERF-05. | Mandatory | 12, 29 |
| `AC-NFR-07` | Local-scan process-tree measurement meets the CPU and memory limits in NFR-PERF-06. | Mandatory | 04, 12, 29 |
| `AC-NFR-08` | Closing PC Steward with no active work leaves no PC Steward process after 30 seconds. | Mandatory | 26, 29 |
| `AC-NFR-09` | Attempts to start a second scan are rejected or routed to the existing scan, and measured connector concurrency never exceeds the configured bound. | Mandatory | 04, 12, 29 |
| `AC-NFR-10` | Filling the app-owned download cache beyond its soft quota evicts only inactive oldest files and never blocks or deletes the artifact required by an active update. | Mandatory | 24, 29 |
| `AC-NFR-11` | A scheduled scan runs without launching a persistent tray app and exits within the NFR-PERF-07 limit. | Mandatory | 26, 29 |
