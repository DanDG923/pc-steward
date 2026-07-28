# Phase 3 — Safe update execution and app-owned cache

## Objective

Execute only immutable, user-approved WinGet operations through constrained runners, request elevation only at execution, verify outcomes, preserve per-item history, and safely manage only PC Steward-owned downloaded artifacts.

## Required task order

1. **Task 21:** Immutable update jobs and constrained non-elevated runner
2. **Task 22:** Short-lived elevated helper and secure IPC
3. **Task 23:** Sequential batches, post-update verification, history, and diagnostics
4. **Task 24:** Downloaded-artifact cache and safe cleanup
5. **Task 24A:** Prepare Phase 3 security-review evidence
6. **Task 25:** Phase 3 security review gate

## Exit condition

Update and cleanup acceptance criteria pass in isolated clean Windows test environments; arbitrary commands and paths are structurally impossible; destructive cleanup cannot escape allowlisted roots.

## Scope boundaries

- Follow `AGENTS.md`.
- Do not implement Later/Never features.
- Do not begin the next phase until the phase review passes and the branch is merged.
- Requirement IDs may appear in more than one task when implementation and later hardening both provide evidence.

## Requirement and acceptance cross-reference

| ID | Requirement / acceptance criterion | MVP marker | Primary task(s) |
|---|---|---|---|
| `FR-UPD-01` | Require explicit selection and confirmation before every update or batch. | Yes | 21 |
| `FR-UPD-02` | Execute only provider-defined, structured operations against an immutable package ID. | Yes | 21 |
| `FR-UPD-03` | Never execute a command string retrieved from the local database or remote metadata. | Yes | 21 |
| `FR-UPD-04` | Request elevation only when the provider determines it is needed. | Yes | 21, 22 |
| `FR-UPD-07` | Capture start time, end time, provider, package ID, target version, exit code and sanitized output. | Yes | 21 |
| `NFR-MNT-03` | Provider commands, argument construction and result parsing must be isolated from presentation code. | Mandatory | 18, 21, 29 |
| `NFR-OBS-01` | Each scan and update job receives a correlation ID. | Mandatory | 12, 21, 23, 29 |
| `NFR-OBS-02` | Logs use structured events and severity levels. | Mandatory | 12, 21, 23, 29 |
| `NFR-OBS-03` | Diagnostics distinguish user cancellation from product or installer failure. | Mandatory | 21, 23, 29 |
| `AC-UPD-01` | No installer runs without the user selecting it and confirming the individual or batch review screen. | Mandatory | 21 |
| `AC-UPD-02` | The update runner accepts only approved provider operations and rejects arbitrary executable paths or command strings. | Mandatory | 21 |
| `AC-UPD-03` | A per-user update that does not require elevation completes without a UAC prompt. | Mandatory | 21, 22 |
| `AC-UPD-04` | A machine-wide update requests UAC only when execution begins, not during scanning or review. | Mandatory | 21, 22 |
| `AC-UPD-05` | If the user declines UAC, the job is recorded as cancelled/denied rather than failed or successful. | Mandatory | 21, 22 |
| `AC-UPD-07` | A non-zero provider or installer result is never displayed as success. | Mandatory | 21, 23 |
| `AC-UPD-11` | Update jobs record timestamps, provider, package ID, versions, exit code and sanitized diagnostic output. | Mandatory | 21, 23 |
| `FR-UPD-05` | Warn when a target application appears to be running. | Yes | 23 |
| `FR-UPD-06` | Do not force-close an application without explicit consent. | Yes | 23 |
| `FR-UPD-08` | Re-inventory the target after the installer exits. | Yes | 23 |
| `FR-UPD-09` | Report success only when the expected version is detected or the provider supplies a reliable restart-pending result. | Yes | 23 |
| `FR-UPD-10` | Run MVP batch updates sequentially. | Yes | 23 |
| `FR-UPD-11` | Allow cancellation before the next queued update begins. | Yes | 23 |
| `FR-HIS-01` | Store scan summaries and update-job history locally. | Yes | 12, 23 |
| `FR-HIS-02` | Show a timeline for each product. | Yes | 19, 23 |
| `FR-HIS-03` | Distinguish source, download, installer, elevation, timeout and verification failures. | Yes | 23 |
| `FR-HIS-04` | Apply configurable retention limits to verbose logs and source caches. | Yes | 19, 23 |
| `FR-HIS-05` | Allow a user to copy a sanitized diagnostic bundle. | Yes | 23 |
| `FR-HIS-06` | Never include credentials, tokens or full environment dumps in exported diagnostics. | Yes | 23 |
| `NFR-REL-03` | A cancelled or crashed batch must preserve completed job results and leave unstarted jobs clearly queued or cancelled. | Mandatory | 23, 29 |
| `NFR-OBS-04` | Logs are bounded by retention and file-size policy. | Mandatory | 23, 29 |
| `NFR-OBS-05` | User-facing diagnostics can be exported in sanitized form. | Mandatory | 23, 29 |
| `AC-UPD-06` | After a successful test update, the post-update scan detects the expected newer version. | Mandatory | 23 |
| `AC-UPD-08` | If the installer reports success but the expected version is not detected, the result is Inconclusive or Restart required, not Success. | Mandatory | 23 |
| `AC-UPD-09` | Batch execution runs sequentially and reports each item's independent result. | Mandatory | 23 |
| `AC-UPD-10` | Cancelling a batch prevents the next unstarted item from running and preserves completed results. | Mandatory | 23 |
| `AC-UX-05` | Every failure view contains a plain-language summary and a safe next action. | Optional — best effort | 19, 23, 26, 29 |
| `FR-DAT-08` | Provide a dedicated **Clear downloaded files** button in Settings that deletes only PC Steward-owned update payloads and temporary extraction files not in active use. | Yes | 24 |
| `FR-DAT-09` | Show **Delete downloaded files** on the update-completion view whenever PC Steward-owned files remain, together with the space recoverable. | Yes | 24 |
| `FR-DAT-10` | Display the current size, last-cleaned time and deletion scope for scan data and downloaded update files. | Yes | 14, 24 |
| `FR-DAT-11` | Do not delete Windows, WinGet, Microsoft Store or another provider's cache unless that provider exposes a documented, safe cleanup operation; disclose this boundary in the UI. | Yes | 24 |
| `FR-DAT-13` | Require an explicit confirmation for each destructive action and a second confirmation for factory reset. | Yes | 14, 24, 28 |
| `FR-DAT-14` | Disable destructive controls while a dependent scan or update is active, or require the user to cancel the operation first. | Yes | 14, 24, 28 |
| `FR-DAT-15` | Deletion operations must be idempotent, report locked or undeletable items accurately and never claim success while relevant app-owned data remains. | Yes | 14, 24, 28 |
| `NFR-PERF-10` | PC Steward-owned downloaded update files must be bounded by a configurable soft quota with a 2 GB default, excluding a file actively required by an update. | Mandatory | 24, 29 |
| `AC-DAT-08` | Selecting **Clear downloaded files** removes all inactive files in the PC Steward-owned download-cache root, updates the displayed size to zero where no locked item remains, and does not alter installed applications, scan data, source metadata or update history. | Mandatory | 24 |
| `AC-DAT-09` | When an update completes and app-owned downloaded files remain, the result view shows **Delete downloaded files** and reports the correct recoverable size. | Mandatory | 24 |
| `AC-DAT-10` | Provider-owned Windows, WinGet or Store caches remain untouched unless a documented provider cleanup adapter is explicitly implemented and tested. | Mandatory | 24 |
| `AC-DAT-11` | A locked or protected app-owned file produces an accurate partial-cleanup result and is not silently reported as deleted. | Mandatory | 24 |
| `AC-DAT-14` | Clear and reset controls require no administrator elevation for the per-user MVP data model. | Mandatory | 14, 24, 28 |
| `AC-DAT-15` | Clear and reset actions are unavailable during an active update or scan until the user cancels or completes it. | Mandatory | 14, 24, 28 |
| `AC-DAT-16` | Cleanup tests prove that a junction, symbolic link or crafted metadata value cannot cause deletion outside the allowlisted PC Steward data roots. | Mandatory | 24, 28 |
| `AC-DAT-17` | Default logs contain no credentials, licence keys, browser data or document contents. | Mandatory | 14, 24 |
| `AC-NFR-10` | Filling the app-owned download cache beyond its soft quota evicts only inactive oldest files and never blocks or deletes the artifact required by an active update. | Mandatory | 24, 29 |
| `AC-UX-07` | The Data & reset page visibly separates routine cleanup from factory reset and shows what each action deletes, preserves and can recover in storage. | Optional — best effort | 14, 24, 28, 29 |
