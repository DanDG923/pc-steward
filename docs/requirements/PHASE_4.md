# Phase 4 — Scheduling, reset, hardening, and private beta

## Objective

Add non-resident scheduled checks, notifications, factory reset, advanced data controls, accessibility, measured resource hardening, standalone packaging/removal behaviour, SBOM, and final release evidence.

## Required task order

1. **Task 26:** Scheduled checks and Windows notifications
2. **Task 26A:** Prepare factory-reset design-gate evidence
3. **Task 27:** Factory-reset design gate
4. **Task 28:** Factory reset and advanced data controls
5. **Task 29:** Accessibility, reliability, and performance hardening
6. **Task 29A:** Prototype-parity UI completion for existing features
7. **Task 29B:** Complete supported native-prototype parity and unify WinGet source consent
8. **Task 29C:** Repository-current application evidence view
9. **Task 29D:** Repository-managed application status view
10. **Task 29E:** Repository evidence, provider resilience, and readable freshness
11. **Task 29F:** WinGet inventory scan follow-up and resilient failure evidence
12. **Task 30:** Packaging, release evidence, SBOM, and private beta preparation
13. **Task 30A:** Prepare final release-audit evidence
14. **Task 31:** Final release audit

## Exit condition

The release definition of done is evidenced; no Blocker/High review findings remain; the candidate is ready for controlled installation on the product owner’s PC.

## Scope boundaries

- Follow `AGENTS.md`.
- Do not implement Later/Never features.
- Do not begin the next phase until the phase review passes and the branch is merged.
- Requirement IDs may appear in more than one task when implementation and later hardening both provide evidence.

## Requirement and acceptance cross-reference

| ID | Requirement / acceptance criterion | MVP marker | Primary task(s) |
|---|---|---|---|
| `FR-SCH-01` | Allow an optional daily, weekly or manual-only scan schedule. | Yes | 26 |
| `FR-SCH-02` | Use Windows Task Scheduler rather than an always-running service. | Yes | 26 |
| `FR-SCH-03` | Scheduled scans may check but must not install updates in the MVP. | Yes | 26 |
| `FR-SCH-04` | Show a Windows notification only when attention is required or a scan fails repeatedly. | Yes | 26 |
| `FR-SCH-05` | Allow the schedule and notifications to be disabled independently. | Yes | 26 |
| `FR-RSC-04` | Run scheduled scans through Task Scheduler without an always-running process, at lower priority where supported, and exit after completion, cancellation or timeout. | Yes | 26 |
| `NFR-PERF-07` | Closing the UI must leave no resident PC Steward process when no approved update or scheduled scan is active. A scheduled scan process must exit within 30 seconds after completion, cancellation or final timeout handling. | Mandatory | 26, 29 |
| `AC-NFR-08` | Closing PC Steward with no active work leaves no PC Steward process after 30 seconds. | Mandatory | 26, 29 |
| `AC-NFR-11` | A scheduled scan runs without launching a persistent tray app and exits within the NFR-PERF-07 limit. | Mandatory | 26, 29 |
| `AC-UX-02` | All primary navigation, scan, review, update, clear-data and factory-reset actions are keyboard accessible. | Optional — best effort | 13, 14, 19, 26, 28, 29 |
| `AC-UX-04` | Windows text scaling and high-contrast mode do not hide primary controls or status text. | Optional — best effort | 13, 19, 26, 28, 29 |
| `AC-UX-05` | Every failure view contains a plain-language summary and a safe next action. | Optional — best effort | 19, 23, 26, 29 |
| `FR-DAT-04` | Provide advanced controls to clear source metadata, logs, update history or manual mappings/policies independently. | Yes | 28 |
| `FR-DAT-12` | Provide a dedicated **Factory reset PC Steward** button that removes all local application data, settings, decisions, caches, logs, consent records and the current-user scheduled task, then returns the next launch to first-run onboarding. The installed app binaries remain. | Yes | 28 |
| `FR-DAT-13` | Require an explicit confirmation for each destructive action and a second confirmation for factory reset. | Yes | 14, 24, 28 |
| `FR-DAT-14` | Disable destructive controls while a dependent scan or update is active, or require the user to cancel the operation first. | Yes | 14, 24, 28 |
| `FR-DAT-15` | Deletion operations must be idempotent, report locked or undeletable items accurately and never claim success while relevant app-owned data remains. | Yes | 14, 24, 28 |
| `AC-DAT-12` | Factory reset removes the SQLite database, scan data, source metadata, logs, update history, app-owned downloaded files, settings, consent records, user decisions and current-user scheduled task after two confirmations. | Mandatory | 28 |
| `AC-DAT-13` | After factory reset and restart, first-run onboarding appears and no previous inventory, history, settings, mappings, pins or ignores is visible. PC Steward remains installed. | Mandatory | 28 |
| `AC-DAT-14` | Clear and reset controls require no administrator elevation for the per-user MVP data model. | Mandatory | 14, 24, 28 |
| `AC-DAT-15` | Clear and reset actions are unavailable during an active update or scan until the user cancels or completes it. | Mandatory | 14, 24, 28 |
| `AC-DAT-16` | Cleanup tests prove that a junction, symbolic link or crafted metadata value cannot cause deletion outside the allowlisted PC Steward data roots. | Mandatory | 24, 28 |
| `AC-DAT-18` | Removing the standalone package deletes extracted binaries and the exact scheduled task; data is retained by default and the existing factory reset is the explicit remove-data choice. | Mandatory | 28, 30 |
| `AC-UX-07` | The Data & reset page visibly separates routine cleanup from factory reset and shows what each action deletes, preserves and can recover in storage. | Optional — best effort | 14, 24, 28, 29 |
| `AC-UX-08` | Factory reset requires a second explicit confirmation and cannot be triggered by a single accidental click or keyboard activation. | Optional — best effort | 28, 29 |
| `NFR-PERF-01` | Local inventory must complete within 15 seconds at the 95th percentile on the agreed baseline PC with 300 provider records. | Mandatory | 12, 29 |
| `NFR-PERF-02` | A full online check must complete within 90 seconds at the 95th percentile when sources are healthy; source timeouts must not block the whole application indefinitely. | Mandatory | 18, 29 |
| `NFR-PERF-03` | The UI must remain responsive during scans and updates. | Mandatory | 12, 29 |
| `NFR-PERF-04` | The application should cold-start to an interactive shell within five seconds on the baseline PC. | Mandatory | 29 |
| `NFR-PERF-05` | With the UI open and no active job for five minutes, average PC Steward CPU usage must remain at or below 1% of total system CPU, working set should remain at or below 300 MB, and the app must not perform recurring disk or network activity. | Mandatory | 12, 29 |
| `NFR-PERF-06` | During a local scan on the baseline PC, the combined PC Steward process tree must average no more than 20% total CPU and remain below 500 MB peak working set. App-owned work must not sustain more than 50% total CPU for over five seconds. | Mandatory | 12, 29 |
| `NFR-PERF-08` | A scan cancellation request must be acknowledged in the UI within two seconds and return the application to an operable state within five seconds, including termination of cancellable child-provider work. | Mandatory | 12, 29 |
| `NFR-PERF-09` | Only one full scan may run at a time. Online connector concurrency defaults to a maximum of three and must be configurable internally without a code rewrite. | Mandatory | 12, 29 |
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
| `AC-NFR-01` | Local inventory meets NFR-PERF-01 on the agreed baseline test system over at least 30 repeated runs after warm-up. | Mandatory | 12, 29 |
| `AC-NFR-02` | Full online scan meets NFR-PERF-02 when test sources are healthy over at least 20 repeated runs. | Mandatory | 18, 29 |
| `AC-NFR-03` | Cancelling a scan or connector request returns the UI to an operable state without data corruption and meets NFR-PERF-08. | Mandatory | 12, 29 |
| `AC-NFR-04` | Terminating the application during a scan leaves the previous successful inventory readable on next launch. | Mandatory | 12, 29 |
| `AC-NFR-05` | A connector timeout does not block results from other connectors. | Mandatory | 18, 29 |
| `AC-NFR-06` | Five-minute idle measurement meets the CPU, memory, disk and network limits in NFR-PERF-05. | Mandatory | 12, 29 |
| `AC-NFR-07` | Local-scan process-tree measurement meets the CPU and memory limits in NFR-PERF-06. | Mandatory | 12, 29 |
| `AC-NFR-09` | Attempts to start a second scan are rejected or routed to the existing scan, and measured connector concurrency never exceeds the configured bound. | Mandatory | 12, 29 |
| `AC-NFR-10` | Filling the app-owned download cache beyond its soft quota evicts only inactive oldest files and never blocks or deletes the artifact required by an active update. | Mandatory | 24, 29 |
| `AC-UX-01` | A first-time user can complete the first scan and identify available updates without using a command line. | Optional — best effort | 13, 19, 29 |
| `AC-UX-03` | Status is expressed through text/icons as well as colour. | Optional — best effort | 13, 19, 29 |
| `AC-UX-06` | The UI always distinguishes live, cached and local-only scan results. | Optional — best effort | 13, 18, 19, 29 |
| `AC-UX-09` | During a scan, the user can see elapsed time, current phase and an enabled **Cancel scan** action. | Optional — best effort | 13, 29 |
