# PC Steward — all copy/paste Codex prompts

Use the settings shown for each task and start a fresh chat for every numbered task. See `CODEX_BUILD_PLAYBOOK.md` for branch transitions and gates.

## Task 00 — Environment and repository audit

**Model:** GPT-5.6 Luna  
**Reasoning:** Low  
**Branch:** `phase/0-foundation`  
**Run after:** Initial repository commit

```text
Task 00 — audit the Windows development environment and establish the repository baseline.

Read only:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_0.md`
- `docs/product/PC_STEWARD_PRODUCT_SPEC.md`, section 14

Do not write production code. Do not install, upgrade, enable, or reconfigure system software. Do not change global Git or Codex settings.

Inspect the Windows host with read-only commands and record:
- Windows edition/build and x64 architecture;
- PowerShell version;
- Git and Git identity availability;
- .NET SDKs and runtimes;
- Visual Studio/Build Tools detection through `vswhere` when available;
- Windows SDK and WinUI/Windows App SDK workload evidence;
- WinGet version, source availability, and whether read-only `winget list` works;
- Developer Mode status where it can be read safely;
- signing/package tooling already present;
- ability to create and run a minimal test process;
- availability of a disposable Windows test VM or sandbox, without creating one.

Create:
- `docs/environment/ENVIRONMENT_REPORT.md` with Pass/Warning/Blocker status, exact commands, concise outputs, and redacted machine-specific data;
- `docs/environment/SETUP_ACTIONS.md` containing only the missing prerequisites, ordered by necessity, and linking them to an official vendor source by name;
- an updated `docs/STATUS.md`.

Run `git status`. Commit only the documentation changes if Git is already initialized, identity is configured, and the tree contains no unrelated changes. Otherwise leave the changes uncommitted and state the exact blocker.
```

## Task 01 — Architecture, threat model, and execution plan

**Model:** GPT-5.6 Sol  
**Reasoning:** High  
**Branch:** `phase/0-foundation`  
**Run after:** Task 00 complete

```text
Task 01 — produce the implementation architecture, threat model, and execution plan. Use Plan mode. Do not write production or spike code.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_0.md`
- `docs/environment/ENVIRONMENT_REPORT.md`
- the full product specification once
- `docs/product/DECISION_LOG.md`

Create:
1. `docs/architecture/COMPONENT_ARCHITECTURE.md`
2. `docs/architecture/DATA_FLOW_AND_TRUST_BOUNDARIES.md`
3. `docs/architecture/THREAT_MODEL.md`
4. `docs/architecture/TEST_STRATEGY.md`
5. `docs/architecture/IMPLEMENTATION_PLAN.md`
6. proposed ADR files for every open decision in specification section 22, using `docs/adr/ADR_TEMPLATE.md`

The architecture must explicitly separate:
- UI;
- application orchestration;
- domain rules;
- SQLite persistence and migrations;
- inventory providers;
- update-source connectors;
- version and matching engines;
- non-elevated update runner;
- short-lived elevated helper;
- downloaded-artifact ownership and deletion;
- scheduling/notifications;
- diagnostics and performance harnesses.

The threat model must cover malicious source metadata, incorrect product matching, arbitrary command execution, UAC/IPC abuse, reparse-point deletion escape, TOCTOU, database tampering, log leakage, stale cache, cancellation/crash recovery, and resource exhaustion.

For each ADR, list the evidence the upcoming spike must collect. Do not mark an ADR Accepted yet. Update `docs/STATUS.md` with the planned spike order and unresolved decisions.
```

## Task 02 — Spike: packaging and WinGet integration

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/0-foundation`  
**Run after:** Task 01 complete

```text
Task 02 — run the disposable packaging and WinGet integration spike for OD-02 and OD-03.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_0.md`
- `docs/architecture/COMPONENT_ARCHITECTURE.md`
- the proposed ADRs for OD-02 and OD-03
- `docs/environment/ENVIRONMENT_REPORT.md`

Create a self-contained spike under `spikes/winget-packaging/`. It must not be referenced by production projects.

Collect evidence for:
- packaged MSIX versus unpackaged signed deployment;
- whether each option can meet current-user storage, Task Scheduler, WinGet, on-demand UAC, uninstall-data choice, and update-helper requirements;
- WinGet CLI versus a supported programmatic route;
- capability/version detection;
- structured output or the most stable parseable output available;
- installed inventory, available-upgrade metadata, Microsoft Store source behaviour, scope/architecture fields, timeouts, cancellation, exit codes, and sanitized diagnostics;
- command construction using an executable plus an argument list, never shell-concatenated text.

Safety:
- run only read-only WinGet operations on the host;
- do not install, upgrade, uninstall, reset sources, change source agreements, invoke UAC, or alter package-manager state;
- any update-invocation experiment must be represented as a non-executed fixture or be deferred to an explicitly approved disposable VM.

Create `spikes/winget-packaging/SPIKE_REPORT.md` with commands, results, limitations, and a recommendation for each decision. Add automated parser/capability tests where practical. Update the proposed ADRs with evidence but leave final status for Task 05.
```

## Task 03 — Spike: privilege boundary and safe deletion

**Model:** GPT-5.6 Sol  
**Reasoning:** High  
**Branch:** `phase/0-foundation`  
**Run after:** Task 01 complete

```text
Task 03 — run the disposable privilege-boundary and path-safe-deletion spike for OD-13 and the elevated-helper design.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_0.md`
- `docs/architecture/THREAT_MODEL.md`
- proposed ADRs related to privilege, cleanup, and factory reset

Create a self-contained spike under `spikes/security-boundaries/`; do not reference it from production projects.

Demonstrate, entirely inside a newly created disposable test root:
- canonical-root validation;
- rejection of relative paths, alternate roots, crafted metadata paths, and path-prefix confusion;
- detection/rejection of junctions, symbolic links, mount points, and other reparse points;
- safe handling of locked files, partial deletion, retry, idempotence, and accurate residual reporting;
- mitigation of a path-swap/TOCTOU attempt to the extent Windows APIs allow;
- a reset manifest that names only fixed PC Steward-owned categories.

Prototype the helper boundary as a no-op or test-only protocol:
- immutable job identifier;
- allowlisted operation verb and package/provider identifiers;
- authenticated/nonce-bound request;
- no arbitrary executable path, command string, working directory, environment, or output path;
- short lifetime and explicit denial/cancellation states.

Do not delete real AppData, invoke real updates, install a service, persist elevated code, or request UAC on the host. If a reparse-point test needs privilege unavailable in the sandbox, record it as a VM/manual test with exact safe steps.

Create `spikes/security-boundaries/SPIKE_REPORT.md`, update the threat model and relevant proposed ADRs with evidence, and update `docs/STATUS.md`.
```

## Task 04 — Spike: performance baseline and fixture matrix

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/0-foundation`  
**Run after:** Task 01 complete

```text
Task 04 — establish the repeatable performance baseline and deterministic software fixture matrix for OD-06.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_0.md`
- specification sections 18 and 19
- `docs/environment/ENVIRONMENT_REPORT.md`
- `docs/architecture/TEST_STRATEGY.md`

Create `spikes/performance-fixtures/` containing:
- deterministic fixture generators for at least 300 provider records;
- normal, malformed, missing-field, duplicate, user/machine, x86/x64, runtime, prerelease, incomparable-version, and false-match cases;
- a benchmark harness that can measure elapsed time, process-tree CPU, working set, cancellation acknowledgement, and bounded concurrency without requiring the future UI;
- scripts or documented commands for 30 warmed local-scan runs and 20 healthy online-check runs;
- a redacted baseline-system capture;
- machine-readable benchmark output.

Do not claim the final application meets a budget based on spike code. Do not tune by weakening accuracy, safety, diagnostics, or cancellation.

Create:
- `spikes/performance-fixtures/BASELINE.md`
- `spikes/performance-fixtures/FIXTURE_CATALOG.md`
- `spikes/performance-fixtures/BENCHMARK_PROTOCOL.md`
- a concise evidence update to the OD-06 ADR.

Run the harness, preserve raw result files, summarize median/p95 and measurement limitations, and update `docs/STATUS.md`.
```

## Task 05 — Lock ADRs and architecture

**Model:** GPT-5.6 Sol  
**Reasoning:** High  
**Branch:** `phase/0-foundation`  
**Run after:** Tasks 02–04 complete

```text
Task 05 — lock the architecture decisions using spike evidence. Do not write production code.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- all Phase 0 architecture documents;
- all three spike reports and their test/benchmark evidence;
- all proposed ADRs;
- specification sections 12–15, 18–24.

For OD-02, OD-03, OD-06, OD-11, and OD-13:
- choose one implementable approach;
- mark the ADR Accepted or Provisional;
- state rejected alternatives and the evidence;
- define concrete safety/performance tests and reconsideration triggers.

Also finalize ADRs for:
- solution/component boundaries;
- SQLite ownership and migrations;
- structured logging/redaction;
- update-job immutability;
- helper IPC and elevation;
- app-owned artifact roots;
- scheduling without a resident process.

Reconcile contradictions across documents. Update:
- `docs/architecture/COMPONENT_ARCHITECTURE.md`
- `docs/architecture/DATA_FLOW_AND_TRUST_BOUNDARIES.md`
- `docs/architecture/THREAT_MODEL.md`
- `docs/architecture/IMPLEMENTATION_PLAN.md`
- `docs/product/DECISION_LOG.md`
- `docs/STATUS.md`

The task is complete only when Task 06 can scaffold without making a new security or packaging decision.
```

## Task 06 — Scaffold solution and engineering guardrails

**Model:** GPT-5.6 Terra  
**Reasoning:** Medium  
**Branch:** `phase/0-foundation`  
**Run after:** Task 05 complete

```text
Task 06 — scaffold the production solution and engineering guardrails from the accepted ADRs. Do not implement product features.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_0.md`
- accepted ADRs;
- `docs/architecture/COMPONENT_ARCHITECTURE.md`
- `docs/architecture/TEST_STRATEGY.md`
- `docs/environment/ENVIRONMENT_REPORT.md`

Create the smallest buildable .NET 10/WinUI solution that reflects the accepted boundaries. Include only the projects justified by the ADRs, plus:
- unit and integration test projects;
- a performance/benchmark project or harness entry point;
- versioned build settings, nullable analysis, analyzers, deterministic builds, formatting rules, and central dependency management where appropriate;
- PowerShell scripts for restore, build, test, and benchmark;
- a minimal app shell that proves startup only, with no inventory or update behaviour;
- CI configuration only if it can run on a Windows host without secrets.

Do not add placeholder services that pretend to implement requirements. Do not add telemetry, cloud services, auto-update, sample download code, or broad third-party frameworks.

Run restore, build, and the empty test suite. Record the canonical commands in `AGENTS.md` and `docs/STATUS.md`. Update traceability to show scaffolding evidence without marking feature requirements complete.
```

## Task 07 — Core domain model and SQLite persistence

**Model:** GPT-5.6 Terra  
**Reasoning:** Medium  
**Branch:** `phase/1-inventory`  
**Run after:** Phase 0 gate passed and merged

```text
Task 07 — implement the core domain model and versioned SQLite persistence, with no inventory providers or UI features.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_1.md`
- ADRs for architecture, persistence, logging, and data lifecycles.

Model separate lifecycle categories for:
- canonical products;
- installation instances;
- raw provider records;
- scan runs/provider timings/warnings;
- update candidates and source cache;
- stable user decisions and manual mappings;
- update jobs/history;
- downloaded artifacts;
- settings/consent/schedule metadata.

Implement:
- stable identifiers/fingerprints that can reapply decisions after Clear scan data;
- versioned, transactional migrations;
- repositories/unit-of-work boundaries appropriate to the ADR;
- last-known-good inventory semantics;
- bounded field lengths and safe normalization at persistence boundaries;
- test-only database factories.

Do not implement provider scanning, matching, version ordering, update execution, deletion UI, or factory reset.

Add migration, transaction, round-trip, interruption, and decision-preservation tests. Update `docs/STATUS.md` and traceability.
```

## Task 08 — Registry inventory providers

**Model:** GPT-5.6 Terra  
**Reasoning:** Medium  
**Branch:** `phase/1-inventory`  
**Run after:** Task 07 complete

```text
Task 08 — implement machine and current-user uninstall-registry inventory providers.

Requirements are listed under Task 08 in `docs/requirements/TRACEABILITY.md`.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_1.md`
- directly relevant provider interfaces and persistence/domain code.

Implement read-only providers for:
- HKLM 64-bit uninstall view;
- HKLM 32-bit uninstall view;
- HKCU uninstall entries for the current user.

Capture safely available fields required by the specification, preserve raw records, and emit provider warnings for malformed data. Scanning must not require elevation, invoke MSI, query `Win32_Product`, enumerate unrelated registry areas, or fail the full provider because one entry is malformed.

Use an injectable/testable registry abstraction. Add fixtures for missing names/versions/publishers, malformed dates, oversized fields, duplicate keys, x86/x64 separation, and access-denied records.

Run targeted tests, then the full build/test suite. Update status and traceability.
```

## Task 09 — MSIX/AppX inventory provider

**Model:** GPT-5.6 Terra  
**Reasoning:** Medium  
**Branch:** `phase/1-inventory`  
**Run after:** Task 08 complete

```text
Task 09 — implement the current-user MSIX/AppX inventory provider.

Requirements are listed under Task 09 in `docs/requirements/TRACEABILITY.md`.

Read only the Phase 1 brief, accepted provider ADRs, and relevant code.

Use supported Windows APIs behind an adapter; do not build production behaviour around shell-concatenated PowerShell output. Enumerate only packages visible to the current user unless the specification explicitly permits more. Preserve package family/full name, version, architecture, publisher identity, install location only where safe, and source/freshness metadata.

Handle framework/resource/system packages conservatively and classify rather than hide them. A single inaccessible or malformed package must produce a warning, not abort the scan.

Add adapter fixtures and Windows integration tests that can skip with an explicit reason when the environment lacks a capability. Run targeted and full tests. Update status and traceability.
```

## Task 10 — WinGet installed-inventory provider

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/1-inventory`  
**Run after:** Task 09 complete

```text
Task 10 — implement the read-only WinGet installed-inventory provider using the accepted OD-03 route.

Requirements are listed under Task 10 in `docs/requirements/TRACEABILITY.md`.

Read:
- `AGENTS.md`
- Phase 1 brief;
- WinGet ADR and spike report;
- relevant provider/process abstractions.

Implement:
- capability/version/source detection;
- installed-application collection only;
- executable plus structured argument-list construction if a process adapter is used;
- bounded timeout, cancellation, output-size limits, encoding handling, exit-code classification, and sanitized diagnostics;
- parser fixtures for all supported WinGet output variants established by the spike;
- local-only mode that performs no source refresh or online update check.

Do not invoke upgrade, install, uninstall, source reset, agreement changes, or arbitrary package commands. Never accept raw commands from configuration, storage, or metadata.

Run parser/unit tests, a safe read-only integration test, and the full suite. Update status and traceability.
```

## Task 11 — Normalization, install identity, and conservative deduplication

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/1-inventory`  
**Run after:** Tasks 08–10 complete

```text
Task 11 — implement canonical product/install-instance normalization and conservative deduplication.

Requirements are listed under Task 11 in `docs/requirements/TRACEABILITY.md`.

Read:
- Phase 1 brief;
- domain/persistence code;
- the fixture catalog;
- accepted identity/grouping ADRs.

Create deterministic rules that:
- preserve every raw provider record;
- merge only evidence-equivalent records for the same installation instance;
- keep user/machine and x86/x64 instances inspectable;
- avoid over-grouping runtimes, redistributables, framework packages, channels, and similarly named products;
- produce stable product and instance fingerprints;
- retain provenance and an explanation of merge decisions;
- classify unknowns rather than invent data.

Add a fixture-driven matrix that proves expected merges and expected non-merges, including adversarial near-name cases. No update matching or executable confidence is implemented in this task.

Run all normalization tests plus the full suite. Update status and traceability.
```

## Task 12 — Scan orchestration, cancellation, timings, and performance harness

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/1-inventory`  
**Run after:** Task 11 complete

```text
Task 12 — implement the local inventory scan orchestrator, transactional persistence, progress/cancellation, provider timing, and performance harness integration.

Requirements are listed under Task 12 in `docs/requirements/TRACEABILITY.md`.

Implement:
- exactly one active full scan;
- phases, elapsed time, correlation ID, provider timing/warnings, and cancellation;
- independent provider failure handling;
- bounded provider concurrency and internal configuration;
- transactional replacement of the last-known-good inventory only after a successful commit;
- cancellation acknowledgement and child-process termination semantics;
- no network activity in local-only scans;
- structured, bounded logs;
- benchmark entry points using the established 300-record fixtures.

Do not add source refresh, update matching, or update execution.

Run:
- targeted orchestration/failure/cancellation tests;
- crash/interruption persistence tests;
- at least the documented warm-up plus local benchmark sample sufficient to detect gross regressions;
- CPU/memory/idle instrumentation where the current shell permits it.

Record raw benchmark evidence and clearly distinguish measured pass, measured fail, and not-yet-measurable UI criteria. Update status and traceability.
```

## Task 13 — App shell, onboarding, and inventory experience

**Model:** GPT-5.6 Terra  
**Reasoning:** Medium  
**Branch:** `phase/1-inventory`  
**Run after:** Task 12 complete

```text
Task 13 — implement the first-launch, local-only inventory user journey and inventory experience.

Requirements are listed under Task 13 in `docs/requirements/TRACEABILITY.md`.

Implement:
- first-run explanation of local data, connector/network behaviour, and permissions;
- local-only first scan as the default safe path;
- enabled-connector display and nonessential connector toggles;
- app navigation and responsive scan progress with phase, elapsed time, and Cancel scan;
- inventory list with search, sorting, status/source/scope filters as far as Phase 1 data permits;
- application details showing installation instances, raw-provider provenance, and freshness;
- clear live/local-only/cached terminology, although online cache is not implemented yet;
- keyboard, text-scaling, high-contrast, icon-plus-text status basics.

Do not expose an Update button, fabricate update statuses, implement source refresh, or request elevation.

Add view-model tests and the narrowest practical UI/integration tests. Manually exercise the first scan on the local test environment without changing the PC. Update status and traceability.
```

## Task 14 — Exports and Clear scan data

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/1-inventory`  
**Run after:** Task 13 complete

```text
Task 14 — implement inventory export and the dedicated Clear scan data lifecycle.

Requirements are listed under Task 14 in `docs/requirements/TRACEABILITY.md`.

Implement:
- CSV and JSON current-inventory exports with export timestamp and privacy warning;
- optional update-history field in the JSON schema, empty until Phase 3;
- a Data & reset page that clearly separates routine cleanup controls from future Factory reset;
- current scan-data size/last-cleared metadata;
- Clear scan data confirmation, operation lockout during an active scan/update, transactional deletion, idempotence, and accurate partial-failure reporting;
- exact preservation of settings, enabled connectors, schedule metadata, update-job history, downloaded artifacts, manual mappings, pins, and ignores;
- dashboard return to a no-scan state;
- reapplication of stable decisions after a later rediscovery.

Do not implement Clear downloaded files or Factory reset in this task; show them only as non-actionable future controls if the UX needs layout continuity.

Add persistence/integration tests proving delete/preserve semantics, offline operation, export fidelity, and no elevation. Run the full suite and update status/traceability.
```

## Task 15 — Phase 1 review gate

**Model:** GPT-5.6 Sol  
**Reasoning:** High  
**Branch:** `phase/1-inventory`  
**Run after:** Tasks 07–14 complete

```text
Task 15 — review Phase 1. Start with `/review` and review the branch from its merge-base with `main`. Do not modify production code.

Read:
- `AGENTS.md`
- `docs/requirements/PHASE_1.md`
- accepted ADRs;
- benchmark evidence;
- `docs/review/PHASE_REVIEW_TEMPLATE.md`
- the complete Phase 1 diff.

Run safe builds/tests/benchmarks needed to verify evidence. Review specifically for:
- missed inventory surfaces in scope;
- accidental `Win32_Product`/MSI repair;
- privilege or network use during local scans;
- over-deduplication;
- malformed-input failures;
- transactional and cancellation defects;
- Clear scan data deleting or preserving the wrong categories;
- misleading UX;
- resource-budget evidence and measurement quality;
- requirement/test traceability.

Write `docs/review/PHASE_1_REVIEW.md` with file/line references, severities, requirement IDs, missing evidence, and PASS/FAIL. A gate cannot pass with a Blocker/High finding or an unproven mandatory acceptance criterion.

Only update the review report and `docs/STATUS.md`. If the gate fails, state that Task 90 must be run and this review repeated.
```

## Task 16 — Version comparison engine

**Model:** GPT-5.6 Terra  
**Reasoning:** Medium  
**Branch:** `phase/2-intelligence`  
**Run after:** Phase 1 gate passed and merged

```text
Task 16 — implement the pure, deterministic version-comparison engine.

Requirements are listed under Task 16 in `docs/requirements/TRACEABILITY.md`.

Implement unit-testable comparison outcomes: Newer, Equal, Older, Incomparable, and connector-deferred. Cover numeric dotted versions, missing components, prerelease labels, Stable-versus-Preview defaults, leading zeros, vendor suffixes, malformed/unknown values, connector-specific semantics, and downgrade prevention.

Never use lexical ordering as a silent fallback and never invent an ordering for incomparable values. The engine must return an explanation code suitable for the UI.

Use the fixture catalog and add adversarial cases. No source calls, UI, matching, or update execution. Run targeted property/table tests and the full suite. Update status and traceability.
```

## Task 17 — Deterministic matching and confidence engine

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/2-intelligence`  
**Run after:** Task 16 complete

```text
Task 17 — implement the deterministic product-to-package matching and confidence engine.

Requirements are listed under Task 17 in `docs/requirements/TRACEABILITY.md`.

Use only explicit evidence such as exact provider/package IDs, package family/MSI identity where available, normalized publisher/product aliases, scope, architecture, channel, and user-confirmed mappings. Produce:
- canonical package mapping;
- confidence level;
- human-readable evidence explanation;
- actionable/non-actionable decision;
- mapping origin;
- incompatibility/review reasons.

Low confidence, architecture/channel conflict, ambiguous publisher, incomparable version, or unmatched data must never become actionable. Manual mappings target only a supported trusted connector package and are reversible.

Create a curated fixture suite with expected true matches, expected non-matches, and adversarial near-name/publisher cases. The test must assert zero false-positive actionable mappings. No update execution.

Run targeted and full tests; update status and traceability.
```

## Task 18 — WinGet update intelligence, source cache, and freshness

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/2-intelligence`  
**Run after:** Task 17 complete

```text
Task 18 — implement WinGet source refresh, available-update intelligence, validated source caching, and freshness.

Requirements are listed under Task 18 in `docs/requirements/TRACEABILITY.md`.

Using the accepted connector abstraction:
- refresh only enabled WinGet/Store sources on explicit online checks;
- expose last success, live/cached/local-only state, provider version, and warnings;
- validate remote/source fields with length, format, enum, and output-size limits;
- isolate connector failure and timeout;
- preserve the last known source result with an unmistakable stale timestamp;
- combine source candidates with the version and matching engines;
- generate Current, Verified update, Review required, Unmanaged, Pinned, Ignored, and Failed/Unavailable statuses;
- maintain online connector concurrency at the configured bound.

Do not execute updates, add raw command connectors, search arbitrary websites, or enable GitHub Releases unless running optional Task 18B later.

Add healthy, stale, malformed, timeout, disagreement, architecture/channel, and offline tests. Run the documented online-check benchmark against fixtures/test doubles; use real source calls only with network approval and without modifying package-manager state. Update status and traceability.
```

## Task 19 — Manual decisions and update-intelligence UX

**Model:** GPT-5.6 Terra  
**Reasoning:** Medium  
**Branch:** `phase/2-intelligence`  
**Run after:** Task 18 complete

```text
Task 19 — complete the update-intelligence user experience and stable user decisions.

Requirements are listed under Task 19 in `docs/requirements/TRACEABILITY.md`.

Implement:
- dashboard counts and attention-needed view;
- application details with installed/candidate metadata, source, stable package ID, confidence, explanation, freshness, architecture, channel, and reason an action is disabled;
- manual mapping create/review/delete against connector-supplied package IDs only;
- current-version/source-package pinning supported by the MVP;
- ignore indefinitely or until a date;
- optional reason, unified decisions settings view, and reversal;
- product timeline entries for scans and decisions;
- clear failure summaries and safe next actions.

Do not expose an executable Update action yet. Do not let UI state override matching safety.

Add view-model, persistence, decision-reapplication, and accessibility tests. Run the full suite and the zero-false-positive fixture test. Update status and traceability.
```

## Task 20 — Phase 2 review gate

**Model:** GPT-5.6 Sol  
**Reasoning:** High  
**Branch:** `phase/2-intelligence`  
**Run after:** Tasks 16–19 complete

```text
Task 20 — review Phase 2. Start with `/review` and review the branch from its merge-base with `main`. Do not modify production code.

Read the Phase 2 brief, accepted ADRs, fixture catalog/results, source-cache evidence, and complete diff.

Review for:
- false-positive actionable mappings;
- guessed/invented version ordering;
- architecture/channel/publisher ambiguity;
- unsafe manual mapping;
- stale results presented as live;
- unbounded or malformed source data;
- raw command connectors or arbitrary web behaviour;
- UI enabling actions not justified by domain state;
- cache/offline/timeout resilience;
- traceability and missing tests.

Run the curated mapping suite and relevant full tests. Write `docs/review/PHASE_2_REVIEW.md` using the template. The gate fails for any actionable false positive, Blocker/High finding, or unproven mandatory acceptance criterion.

Only update the review report and status. If failed, run Task 90 and repeat this review.
```

## Task 21 — Immutable update jobs and constrained non-elevated runner

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/3-execution`  
**Run after:** Phase 2 gate passed and merged; accepted update-execution and helper ADRs exist

```text
Task 21 — implement immutable, user-approved update jobs and the constrained non-elevated WinGet runner.

Requirements are listed under Task 21 in `docs/requirements/TRACEABILITY.md`.

Before editing, confirm the accepted update-execution and helper ADRs still match the current connector implementation.

Implement:
- immutable job snapshot containing provider, stable package ID, installed/candidate versions, scope, architecture, source, approval timestamp, and correlation ID;
- review/confirmation state required before execution;
- provider-owned operation selection, with executable and argument list constructed only from allowlisted code paths and validated enum/ID values;
- timeout, cancellation, bounded output, exit-code classification, sanitized logs, and denial states;
- non-elevated execution for operations that do not require elevation;
- structural rejection of arbitrary commands, executable paths, working directories, environment injection, or database/metadata-supplied arguments.

Do not add batch execution, UAC helper implementation, direct installer downloads, or post-verification beyond interfaces.

Add negative tests that attempt command/path/argument injection and prove rejection. Run targeted and full tests. Update status and traceability.
```

## Task 22 — Short-lived elevated helper and secure IPC

**Model:** GPT-5.6 Sol  
**Reasoning:** High  
**Branch:** `phase/3-execution`  
**Run after:** Task 21 complete

```text
Task 22 — implement the short-lived elevated helper and secure IPC exactly as accepted in the ADRs.

Requirements are listed under Task 22 in `docs/requirements/TRACEABILITY.md`.

Implement the narrowest possible helper:
- no service and no persistence;
- launched only when an approved job reaches execution and the provider requires elevation;
- authenticated, nonce-bound, single-purpose request tied to an immutable job;
- allowlisted operation verb/provider/package identifiers;
- validation against a non-user-editable approved-job representation;
- no arbitrary executable path, command string, working directory, environment, output path, or remote metadata;
- explicit user-denied, cancelled, timed-out, failed, and completed results;
- sanitized result channel and immediate exit.

Threat-model and test replay, substitution, stale job, tampered database, malformed IPC, duplicate request, path injection, privilege confusion, and helper crash. Do not invoke a real update on the product owner’s PC. UAC/integration tests must use a harmless test verb in a disposable VM or be documented as pending manual evidence.

Run static analysis and all helper boundary tests. Update the threat model, status, and traceability.
```

## Task 23 — Sequential batches, post-update verification, history, and diagnostics

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/3-execution`  
**Run after:** Tasks 21–22 complete

```text
Task 23 — implement application-running warnings, sequential batches, cancellation between items, post-update verification, history, and sanitized diagnostics.

Requirements are listed under Task 23 in `docs/requirements/TRACEABILITY.md`.

Implement:
- running-process detection as a warning, never an unapproved force close;
- explicit consent before any close action;
- sequential execution with independent per-item states;
- cancellation that prevents the next unstarted job while preserving completed results;
- target re-inventory after provider exit;
- Success only when the expected version is detected, or a reliable restart-pending state is returned;
- Inconclusive/Restart required for provider success without version proof;
- product timeline and job history;
- source/download/installer/elevation/timeout/verification/cancellation distinctions;
- bounded retention and sanitized diagnostic bundle.

Use controlled fixtures or disposable VM packages; do not update unrelated real applications. Add crash/restart recovery tests and negative provider-result tests. Run the full suite and update status/traceability.
```

## Task 24 — Downloaded-artifact cache and safe cleanup

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/3-execution`  
**Run after:** Task 23 complete

```text
Task 24 — implement PC Steward-owned downloaded-artifact tracking, quota/retention, and both downloaded-file cleanup buttons.

Requirements are listed under Task 24 in `docs/requirements/TRACEABILITY.md`.

Implement:
- a fixed per-user app-owned cache root and persisted artifact ownership records;
- active/inactive state, size, hash where available, job association, retention, last-cleaned time, and recoverable size;
- 2 GB soft quota with oldest-inactive eviction only;
- Settings **Clear downloaded files**;
- update-completion **Delete downloaded files** when files remain;
- exact UI disclosure that Windows, WinGet, Store, and other provider caches are untouched;
- canonical-root checks, handle-based/reparse-aware validation as defined by ADR, idempotence, locked-file partial results, and no false success;
- destructive-action lockout while dependent work is active.

Do not broaden deletion to provider-owned caches or accept any deletion path from metadata, package names, logs, or user text.

Reuse and promote the hostile deletion fixtures from the security spike. Add junction/symlink/reparse, path-swap, prefix-confusion, locked-file, quota, active-artifact, and preservation tests. Run static analysis and the full suite. Update threat model, status, and traceability.
```

## Task 25 — Phase 3 security review gate

**Model:** GPT-5.6 Sol  
**Reasoning:** Extra High  
**Branch:** `phase/3-execution`  
**Run after:** Tasks 21–24 complete

```text
Task 25 — perform the Phase 3 security review. Start with `/review` and inspect the branch from its merge-base with `main`. Do not modify production code.

Read the Phase 3 brief, all execution/helper/deletion ADRs, threat model, hostile fixtures, VM/manual evidence, and complete diff.

Review for:
- arbitrary command/path/argument execution;
- mutable or replayable approved jobs;
- UAC/IPC privilege escalation or confused deputy behaviour;
- database/metadata tampering;
- incorrect success states;
- unsafe process closing;
- batch cancellation/recovery defects;
- secrets or personal data in logs;
- cache ownership mistakes;
- path traversal, junction/symlink/reparse escape, TOCTOU, and false cleanup success;
- unbounded output/resource use;
- missing negative tests and unproven acceptance criteria.

Run all safe security, update-runner, and cleanup tests. Write `docs/review/PHASE_3_SECURITY_REVIEW.md` with PASS/FAIL. Any arbitrary-execution possibility, deletion escape, privilege-boundary weakness, false Success, Blocker/High finding, or missing mandatory VM evidence fails the gate.

Only update review/status. If failed, run Task 90 and repeat this review.
```

## Task 26 — Scheduled checks and Windows notifications

**Model:** GPT-5.6 Terra  
**Reasoning:** Medium  
**Branch:** `phase/4-beta`  
**Run after:** Phase 3 gate passed and merged

```text
Task 26 — implement optional scheduled checks and Windows notifications without a resident process.

Requirements are listed under Task 26 in `docs/requirements/TRACEABILITY.md`.

Implement:
- manual-only, daily, and weekly current-user schedules;
- Windows Task Scheduler registration/update/removal through a typed adapter;
- scheduled checks that scan/check only and never install;
- lower process priority and Task Scheduler conditions where supported by the accepted ADR;
- independent schedule and notification toggles;
- notification only for attention-needed results or repeated scan failure;
- single-instance/overlap protection;
- exit after completion, cancellation, or final timeout;
- no tray app, service, or background coordinator.

Add task-definition fixtures, create/update/delete/idempotence tests, overlap tests, no-install tests, and process-exit measurements. Use a harmless short test schedule only with explicit approval, then remove it. Update status and traceability.
```

## Task 27 — Factory-reset design gate

**Model:** GPT-5.6 Sol  
**Reasoning:** High  
**Branch:** `phase/4-beta`  
**Run after:** Task 26 complete

```text
Task 27 — finalize the factory-reset and advanced data-control design before implementation. Do not change production code.

Read:
- `AGENTS.md`
- Phase 4 brief;
- OD-13 ADR and security spike;
- current persistence, schedule, logging, artifact, and settings implementations;
- specification sections 12.5, 15.5, 17.3, and relevant acceptance criteria.

Produce/update:
- an exact reset manifest listing every deleted and preserved category;
- operation ordering and transaction boundaries;
- active-work lockout/cancellation rules;
- locked-file and partial-failure behaviour;
- schedule removal;
- in-process restart versus marker/helper mechanism based on actual file-handle behaviour;
- first-launch verification;
- rollback/safe failure state;
- hostile reparse/path tests;
- uninstall retain/remove-data interaction.

Update the OD-13 ADR to Accepted with current evidence and create `docs/architecture/FACTORY_RESET_DESIGN.md`. The design must not require admin rights, delete binaries, delete provider-owned caches, or accept arbitrary paths.

Update status. Do not implement until the document has no unresolved security decision.
```

## Task 28 — Factory reset and advanced data controls

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/4-beta`  
**Run after:** Task 27 complete

```text
Task 28 — implement advanced data controls and Factory reset PC Steward exactly from the accepted reset design.

Requirements are listed under Task 28 in `docs/requirements/TRACEABILITY.md`.

Implement:
- independent advanced clearing of source metadata, logs, update history, and manual mappings/policies;
- a visually separate Factory reset control;
- first confirmation explaining all categories;
- a second explicit confirmation resistant to accidental keyboard activation;
- active-work lockout;
- deletion of database, inventory, source metadata, logs, history, app-owned artifacts, settings, consent, user decisions, and current-user scheduled task;
- preservation of installed binaries and all provider-owned caches;
- accurate partial failure and no false success;
- restart/close and verified first-run onboarding;
- idempotence and no elevation.

Reuse the allowlisted-root deletion subsystem; do not create a broader delete primitive. Add full integration tests, hostile reparse/path fixtures, locked-file tests, scheduled-task cleanup, restart verification, and uninstall interaction tests where package tooling permits.

Run static analysis and the full suite. Update threat model, status, and traceability.
```

## Task 29 — Accessibility, reliability, and performance hardening

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/4-beta`  
**Run after:** Task 28 complete

```text
Task 29 — run the full accessibility, reliability, cancellation, and resource-budget hardening pass.

Requirements are listed under Task 29 in `docs/requirements/TRACEABILITY.md`.

First measure; then change only evidenced bottlenecks or failures. Execute the documented protocols for:
- cold start;
- five-minute idle CPU, working set, disk, and network;
- 30 warmed local inventory runs with p95;
- 20 healthy fixture-backed online checks with p95;
- process-tree CPU/peak working set;
- cancellation acknowledgement and operable-state recovery;
- connector timeout isolation;
- second-scan rejection and concurrency bound;
- no resident process after close;
- scheduled-process exit;
- cache quota/active-artifact protection;
- crash/interruption last-known-good recovery.

Perform keyboard, screen-reader naming, text scaling, high contrast, icon-plus-text, focus order, confirmation, and failure-message checks for every primary action.

Store raw machine-readable results and a concise `docs/review/PERFORMANCE_AND_ACCESSIBILITY_EVIDENCE.md`. Do not relax safety, matching precision, or destructive-operation controls to hit budgets. Fix failures, rerun affected protocols, and update status/traceability with measured—not assumed—results.
```

## Task 30 — Packaging, release evidence, SBOM, and private beta preparation

**Model:** GPT-5.6 Terra  
**Reasoning:** High  
**Branch:** `phase/4-beta`  
**Run after:** Task 29 complete

```text
Task 30 — prepare the private-beta package and release evidence using the accepted packaging ADR.

Read the Phase 4 brief, packaging ADR, environment report, all phase reviews, and release definition of done.

Implement or produce:
- reproducible Release build scripts;
- package/installer configuration;
- development signing only when an appropriate certificate already exists or the user explicitly approves certificate creation/use;
- versioning and release notes;
- install, upgrade, launch, uninstall, retain-data, remove-data, scheduled-task cleanup, and first-run test procedures;
- SBOM and dependency inventory;
- static analysis, dependency vulnerability, secret, licence, and package-integrity reports using approved tooling;
- known limitations and privacy/security statement;
- a clean-VM evidence checklist.

Do not publish, push, tag, upload, buy a certificate, or claim production signing. Do not mark a clean-VM test passed unless it actually ran and produced evidence.

Create `docs/release/PRIVATE_BETA_CHECKLIST.md`, `docs/release/KNOWN_LIMITATIONS.md`, and `docs/release/RELEASE_EVIDENCE.md`. Build the candidate and run all locally available release tests. Update status and traceability.
```

## Task 31 — Final release audit

**Model:** GPT-5.6 Sol  
**Reasoning:** Extra High  
**Branch:** `phase/4-beta`  
**Run after:** Task 30 complete

```text
Task 31 — perform the final MVP release audit. Start with `/review`. Review the complete repository and all evidence; do not modify production code.

Read:
- full product specification;
- all accepted ADRs and architecture documents;
- all phase review reports;
- traceability matrix;
- threat model;
- test, benchmark, VM/manual, SBOM, static-analysis, dependency, secret-scan, packaging, and uninstall evidence;
- complete diff/history from the initial product-spec commit.

Independently verify every mandatory acceptance criterion and release-definition item. Re-run safe critical suites, especially:
- zero false-positive actionable mapping;
- arbitrary command/path rejection;
- helper/IPC negative tests;
- post-update Success verification;
- destructive cleanup and factory-reset escape tests;
- cancellation/crash recovery;
- performance/resource budgets;
- no-resident-process and scheduled-exit tests.

Create `docs/review/FINAL_RELEASE_AUDIT.md` with:
- READY or NOT READY;
- criterion-by-criterion status: Proven, Failed, or Unverified;
- Blocker/High/Medium/Low findings with file/line and requirement IDs;
- exact remediation and verification;
- residual risks requiring explicit product-owner acceptance.

Any Blocker/High finding, executable false positive, privilege/deletion escape, false Success, failed mandatory criterion, or material Unverified criterion means NOT READY. Only update the audit report and status.
```

## Optional Task 18B — Official GitHub Releases read-only connector

```text
Optional Task 18B — implement user-confirmed, read-only official GitHub Releases version checks. Do not make GitHub an executable update provider.

Read FR-SRC-06 and the trusted-source policy. Require the user to map a product to an explicit official repository owner/name. Validate repository identity, API response sizes/fields, release channel, prerelease exclusion, rate-limit/error handling, and cached freshness.

The connector may return version/release-note intelligence only. It must not download or run assets, infer an official repository from search results, follow arbitrary URLs, authorize a match, or enable Update.

Add fixtures for renamed/deleted repositories, prereleases, malformed tags, no releases, rate limiting, stale cache, and repository ownership ambiguity. Run full tests and repeat Task 20 review with the added scope.
```

## Task 90 — Remediate a failed phase review

```text
Task 90 — remediate the current failed phase review without widening scope.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- the current phase brief;
- the latest phase review report;
- only the ADRs/files/tests cited by the findings.

Create `docs/PLANS.md` for the remediation, ordered Blocker → High → mandatory evidence → Medium. Implement the smallest changes that resolve each finding. Do not suppress tests, weaken acceptance thresholds, convert a failure into a warning, or mark unexecuted evidence as passed.

For every finding:
- add or improve a regression test;
- record the fixing commit and verification in the review report;
- rerun the narrow test, then the full relevant suite;
- rerun affected security/performance/manual evidence.

Update status and traceability. Commit only when no Blocker/High finding remains in your own diff review. Then stop; rerun the original numbered phase-review task in a fresh Sol chat.
```

## Task 91 — Escalate a stuck task

```text
Task 91 — resolve a stuck task from a compact failure handoff.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- the current phase brief;
- `docs/failures/<task-id>.md`;
- only the files and ADRs named in that failure report.

Do not restart the entire phase or redesign unrelated components. Reproduce the exact failure first. Identify the smallest root cause and choose one evidence-backed fix. If the failure reveals an ADR or specification conflict, stop after writing a proposed decision update rather than improvising a new architecture.

Implement the fix, add a regression test, run targeted and full relevant tests, review the diff, update the failure report with resolution evidence, status, and traceability. Commit only if green. Return to the original task’s completion criteria; do not begin the next task.
```
