# Phase 1 — Local inventory vertical slice

## Objective

Deliver a local-only, non-elevated inventory workflow with durable storage, conservative deduplication, progress/cancellation, inventory UI, exports, Clear scan data, and repeatable resource measurements.

## Required task order

1. **Task 07:** Core domain model and SQLite persistence
2. **Task 08:** Registry inventory providers
3. **Task 09:** MSIX/AppX inventory provider
4. **Task 10:** WinGet installed-inventory provider
5. **Task 11:** Normalization, install identity, and conservative deduplication
6. **Task 12:** Scan orchestration, cancellation, timings, and performance harness
7. **Task 13:** App shell, onboarding, and inventory experience
8. **Task 14:** Exports and Clear scan data
9. **Task 14A:** Prepare Phase 1 review evidence
10. **Task 15:** Phase 1 review gate

## Exit condition

All Phase 1 acceptance criteria pass; the previous successful inventory survives interruption; local scanning meets or has an evidenced remediation plan for the defined budgets; no update execution exists.

## Scope boundaries

- Follow `AGENTS.md`.
- Do not implement Later/Never features.
- Do not begin the next phase until the phase review passes and the branch is merged.
- Requirement IDs may appear in more than one task when implementation and later hardening both provide evidence.

## Requirement and acceptance cross-reference

| ID | Requirement / acceptance criterion | MVP marker | Primary task(s) |
|---|---|---|---|
| `FR-INV-01` | Read machine-wide 64-bit uninstall registry entries. | Yes | 08 |
| `FR-INV-02` | Read machine-wide 32-bit uninstall registry entries. | Yes | 08 |
| `FR-INV-03` | Read current-user uninstall registry entries. | Yes | 08 |
| `FR-INV-06` | Preserve raw provider records for diagnosis while presenting normalized products in the UI. | Yes | 08, 09, 10 |
| `FR-INV-08` | Capture name, version, publisher, install scope, architecture, source provider, install date and path where safely available. | Yes | 08, 09, 10 |
| `FR-INV-09` | Classify runtimes, redistributables, drivers, system components and applications separately where evidence allows. | Partial | 08, 11 |
| `AC-INV-01` | Given applications registered in each supported inventory surface, a scan creates a record for every fixture entry expected from that surface. | Mandatory | 08, 09, 10 |
| `AC-INV-03` | The scan does not invoke `Win32_Product` and does not trigger MSI repair operations. | Mandatory | 08 |
| `AC-INV-04` | A malformed or incomplete registry entry does not crash the scan; it is either represented with unknown fields or recorded as a provider warning. | Mandatory | 08, 09, 10 |
| `AC-INV-06` | A normal scan does not require or request administrator elevation. | Mandatory | 08, 09, 10 |
| `AC-INV-07` | The application shows the provider and freshness of every displayed inventory record. | Mandatory | 08, 09, 10 |
| `NFR-REL-05` | The application must recover gracefully from malformed or missing registry values. | Mandatory | 08, 29 |
| `FR-INV-04` | Enumerate installed MSIX/AppX packages visible to the current user. | Yes | 09 |
| `FR-INV-05` | Import installed-application information reported by WinGet. | Yes | 10 |
| `AC-INV-05` | A local-only scan makes no update-source network request. | Mandatory | 10 |
| `FR-INV-07` | Deduplicate equivalent records without merging genuinely separate user/machine or architecture instances. | Yes | 11 |
| `FR-ID-01` | Maintain a canonical product identity separate from each install instance. | Yes | 11, 17 |
| `AC-INV-02` | Duplicate raw records for the same install instance are presented once, while distinct user/machine or x86/x64 instances remain separately inspectable. | Mandatory | 11 |
| `NFR-MNT-02` | Matching and version rules must be unit-testable without launching the UI. | Mandatory | 11, 16, 29 |
| `FR-RSC-01` | Display scan phase, elapsed time and a **Cancel scan** action for manual scans. | Yes | 12, 13 |
| `FR-RSC-02` | Permit only one active full scan and prevent it from overlapping update execution, except for a controlled post-update verification scan. | Yes | 12 |
| `FR-RSC-03` | Apply bounded connector concurrency, per-provider timeouts and cancellation tokens so one source cannot consume unbounded resources or block the whole scan. | Yes | 12 |
| `FR-RSC-05` | Record scan duration and provider timings locally for performance diagnosis without collecting unrelated application-usage telemetry. | Yes | 12 |
| `FR-HIS-01` | Store scan summaries and update-job history locally. | Yes | 12, 23 |
| `NFR-PERF-01` | Local inventory must complete within 15 seconds at the 95th percentile on the agreed baseline PC with 300 provider records. | Mandatory | 12, 29 |
| `NFR-PERF-03` | The UI must remain responsive during scans and updates. | Mandatory | 12, 29 |
| `NFR-PERF-05` | With the UI open and no active job for five minutes, average PC Steward CPU usage must remain at or below 1% of total system CPU, working set should remain at or below 300 MB, and the app must not perform recurring disk or network activity. | Mandatory | 12, 29 |
| `NFR-PERF-06` | During a local scan on the baseline PC, the combined PC Steward process tree must average no more than 20% total CPU and remain below 500 MB peak working set. App-owned work must not sustain more than 50% total CPU for over five seconds. | Mandatory | 12, 29 |
| `NFR-PERF-08` | A scan cancellation request must be acknowledged in the UI within two seconds and return the application to an operable state within five seconds, including termination of cancellable child-provider work. | Mandatory | 12, 29 |
| `NFR-PERF-09` | Only one full scan may run at a time. Online connector concurrency defaults to a maximum of three and must be configurable internally without a code rewrite. | Mandatory | 12, 29 |
| `NFR-REL-02` | Database writes must be transactional so interrupted scans do not corrupt the last known good inventory. | Mandatory | 12, 29 |
| `NFR-MNT-01` | Inventory providers and update connectors must implement stable interfaces and fail independently. | Mandatory | 12, 18, 29 |
| `NFR-MNT-04` | Database migrations must be versioned and reversible during development. | Mandatory | 12, 29 |
| `NFR-OBS-01` | Each scan and update job receives a correlation ID. | Mandatory | 12, 21, 23, 29 |
| `NFR-OBS-02` | Logs use structured events and severity levels. | Mandatory | 12, 21, 23, 29 |
| `AC-NFR-01` | Local inventory meets NFR-PERF-01 on the agreed baseline test system over at least 30 repeated runs after warm-up. | Mandatory | 12, 29 |
| `AC-NFR-03` | Cancelling a scan or connector request returns the UI to an operable state without data corruption and meets NFR-PERF-08. | Mandatory | 12, 29 |
| `AC-NFR-04` | Terminating the application during a scan leaves the previous successful inventory readable on next launch. | Mandatory | 12, 29 |
| `AC-NFR-06` | Five-minute idle measurement meets the CPU, memory, disk and network limits in NFR-PERF-05. | Mandatory | 12, 29 |
| `AC-NFR-07` | Local-scan process-tree measurement meets the CPU and memory limits in NFR-PERF-06. | Mandatory | 12, 29 |
| `AC-NFR-09` | Attempts to start a second scan are rejected or routed to the existing scan, and measured connector concurrency never exceeds the configured bound. | Mandatory | 12, 29 |
| `FR-ONB-01` | Explain local inventory collection, network source access, permissions and data storage before the first scan. | Yes | 13 |
| `FR-ONB-02` | Permit a local-only first scan. | Yes | 13 |
| `FR-ONB-03` | Show all enabled connectors and their trust category. | Yes | 13 |
| `FR-ONB-04` | Allow the user to disable nonessential connectors. | Yes | 13 |
| `FR-UI-02` | Support search, sorting and filtering by status, publisher, source and scope. | Yes | 13, 19 |
| `FR-UI-03` | Show an application detail page with installed and candidate metadata side by side. | Yes | 13, 19 |
| `FR-UI-05` | Show last scan time and whether the displayed result is live or cached. | Yes | 13, 18, 19 |
| `FR-UI-07` | Provide a detailed diagnostics view without exposing unnecessary implementation noise by default. | Yes | 13, 19 |
| `AC-UX-01` | A first-time user can complete the first scan and identify available updates without using a command line. | Optional — best effort | 13, 19, 29 |
| `AC-UX-02` | All primary navigation, scan, review, update, clear-data and factory-reset actions are keyboard accessible. | Optional — best effort | 13, 14, 19, 26, 28, 29 |
| `AC-UX-03` | Status is expressed through text/icons as well as colour. | Optional — best effort | 13, 19, 29 |
| `AC-UX-04` | Windows text scaling and high-contrast mode do not hide primary controls or status text. | Optional — best effort | 13, 19, 26, 28, 29 |
| `AC-UX-06` | The UI always distinguishes live, cached and local-only scan results. | Optional — best effort | 13, 18, 19, 29 |
| `AC-UX-09` | During a scan, the user can see elapsed time, current phase and an enabled **Cancel scan** action. | Optional — best effort | 13, 29 |
| `FR-DAT-01` | Export current inventory to CSV and JSON. | Yes | 14 |
| `FR-DAT-02` | Optionally include update history in JSON export. | Yes | 14 |
| `FR-DAT-03` | Warn that exports reveal installed software and potentially identifying machine metadata. | Yes | 14 |
| `FR-DAT-06` | Provide a dedicated **Clear scan data** button that removes current inventory, raw provider records, update candidates and scan-run records. | Yes | 14 |
| `FR-DAT-07` | **Clear scan data** must preserve global settings, enabled connectors, schedule, update-job history and app-owned downloaded update files. User-created mappings, pins and ignores must be retained in a stable decision store and reapplied when the same product is rediscovered. | Yes | 14 |
| `FR-DAT-10` | Display the current size, last-cleaned time and deletion scope for scan data and downloaded update files. | Yes | 14, 24 |
| `FR-DAT-13` | Require an explicit confirmation for each destructive action and a second confirmation for factory reset. | Yes | 14, 24, 28 |
| `FR-DAT-14` | Disable destructive controls while a dependent scan or update is active, or require the user to cancel the operation first. | Yes | 14, 24, 28 |
| `FR-DAT-15` | Deletion operations must be idempotent, report locked or undeletable items accurately and never claim success while relevant app-owned data remains. | Yes | 14, 24, 28 |
| `AC-DAT-01` | Closing and reopening PC Steward preserves inventory, mappings, decisions and update history. | Mandatory | 14 |
| `AC-DAT-02` | CSV and JSON exports match the displayed current inventory and identify export time. | Mandatory | 14 |
| `AC-DAT-03` | The product functions for inventory and history while offline. | Mandatory | 14 |
| `AC-DAT-05` | No account, email address or cloud sign-in is required. | Mandatory | 14 |
| `AC-DAT-06` | Selecting **Clear scan data** removes current inventory, raw scan/provider records, update candidates and scan-run records, and the dashboard returns to a no-scan state. | Mandatory | 14 |
| `AC-DAT-07` | After **Clear scan data**, global settings, enabled sources, schedule, update-job history and app-owned downloaded files remain unchanged. A subsequent scan can reapply a previously saved pin, ignore or manual mapping to the same product. | Mandatory | 14 |
| `AC-DAT-14` | Clear and reset controls require no administrator elevation for the per-user MVP data model. | Mandatory | 14, 24, 28 |
| `AC-DAT-15` | Clear and reset actions are unavailable during an active update or scan until the user cancels or completes it. | Mandatory | 14, 24, 28 |
| `AC-DAT-17` | Default logs contain no credentials, licence keys, browser data or document contents. | Mandatory | 14, 24 |
| `AC-UX-07` | The Data & reset page visibly separates routine cleanup from factory reset and shows what each action deletes, preserves and can recover in storage. | Optional — best effort | 14, 24, 28, 29 |
