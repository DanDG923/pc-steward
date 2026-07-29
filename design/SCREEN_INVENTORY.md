# PC Steward screen inventory

**Naming:** `PCS-[area]-[number]`  
**Target shell:** WinUI 3 desktop NavigationView  
**State source:** presentation/view-model data only; UI must not decide trust, execution authority or deletion paths  
**Interactive reference:** `design/native-prototype/` is authoritative; `design/prototype/` is a superseded browser-rendered visual archive only

## 1. Shell and global surfaces

| ID | Surface | Purpose | Core elements | Primary states |
|---|---|---|---|---|
| PCS-SH-01 | App shell | Stable navigation and operation context | Title bar, navigation rail, page host, global operation bar, theme menu | Expanded, collapsed, narrow, offline, active operation |
| PCS-SH-02 | Global operation bar | Keep active work visible across navigation | Operation icon, name, phase, elapsed time, `View`, safe cancel when supported | Scan, update, verification, cleanup, cancelling |
| PCS-SH-03 | Global notification region | Announce transient outcomes | Toast stack, dismiss, optional reversible action | Success, info, warning |
| PCS-SH-04 | Global blocking banner | Preserve cross-page safety or source context | Icon + text, timestamp, recovery action | Offline, cached, partial provider failure, restart required |
| PCS-SH-05 | Navigation overflow | Reach all destinations in compact shell | Icon, label, badge, future tag | 900 px minimum, 150–200% text scale |

## 2. Onboarding

### PCS-ONB-01 — Welcome

- **Entry:** first launch or verified factory-reset completion.
- **Purpose:** establish trust before requesting any data or network access.
- **Content:** local-first promise, no account, scan/no-change statement, no elevation for scan, lightweight operation.
- **Actions:** `Continue`; `Read privacy summary`.
- **Acceptance:** primary action visible at 200% text scale; no source enabled by this screen.

### PCS-ONB-02 — Choose first scan

- **Entry:** Continue from Welcome.
- **Purpose:** select local-only or approved online mode.
- **Content:** two mode cards, connector disclosure, trust category, estimated target time.
- **Actions:** select mode; enable disclosed connector; Back; Continue.
- **States:** local-only selected by default; offline disables online mode with explanation.

### PCS-ONB-03 — Review first scan

- **Entry:** Continue from scan choice.
- **Purpose:** final read-only operation summary.
- **Content:** providers, network destinations by connector name, local storage category, no-change statement.
- **Actions:** `Start first scan`; Back.
- **Exit:** PCS-SCN-02.

### PCS-ONB-04 — Local store unavailable

- **Entry:** initialisation failure.
- **Content:** plain summary; no raw exception by default; optional technical details.
- **Actions:** `Try again`; `Copy support details`; Close.

## 3. Overview

### PCS-OV-01 — No scan completed

- **Headline:** `Ready when you are`.
- **Content:** what will be checked, local-only badge, first-scan illustration.
- **Primary:** `Scan this PC`.
- **Secondary:** `Choose scan options`.
- **Trust cue:** `Scanning reads information. It does not install, remove or delete anything.`

### PCS-OV-02 — Current overview

- **Headline:** state sentence such as `3 updates are ready to review`.
- **Content:** freshness strip; attention groups; application summary; recent activity; resource/privacy footnote.
- **Primary:** contextual: `Review 3 updates` or `Scan again`.
- **Secondary:** `View all applications`.
- **States:** live, cached, local-only, source unavailable.

### PCS-OV-03 — No findings

- **Headline:** `Nothing needs your attention from this scan`.
- **Content:** no-findings illustration; scope checked; timestamp; unmanaged/info counts remain available without alarm.
- **Primary:** `View scan details`.
- **Secondary:** `Scan again`.

### PCS-OV-04 — Partial result

- **Headline:** `Most checks completed`.
- **Content:** successful summary retained; failed provider/source; last successful cached time; retry.
- **Primary:** `Retry unavailable checks`.
- **Secondary:** `Review completed results`.

### PCS-OV-05 — Offline

- **Headline:** `Local information is still available`.
- **Content:** local results, cached source age, network-sensitive actions disabled.
- **Primary:** `View local inventory`.
- **Secondary:** `Try source check again`.

## 4. Scan

### PCS-SCN-01 — Scan configuration

- **Content:** mode cards, provider checklist, source disclosure, last successful provider times, target performance note.
- **Actions:** `Scan this PC`; `Restore recommended settings`.
- **Disabled reason:** active scan/update/cleanup or unapproved online source.

### PCS-SCN-02 — Scan progress

- **Content:** phase visualisation; percent when determinate; elapsed time; provider rows; previous-snapshot reassurance.
- **Actions:** `Cancel scan`; `Pause` only when supported.
- **Announcements:** phase changes and cancellation state.

### PCS-SCN-03 — Scan paused *(future orchestration support)*

- **Content:** frozen progress; pause reason; work already completed.
- **Actions:** `Resume scan`; `Cancel scan`.
- **Production rule:** do not implement the control until domain orchestration safely supports pause.

### PCS-SCN-04 — Cancelling

- **Content:** `Stopping safely…`; elapsed time; provider being cancelled.
- **Actions:** no duplicate cancel; navigation may remain available.
- **Timeout:** after expected five-second operable-state target, show bounded-provider delay note.

### PCS-SCN-05 — Cancelled

- **Content:** previous completed data preserved, this run not published.
- **Actions:** `Start again`; `Return to previous overview`.

### PCS-SCN-06 — Permission/source consent required

- **Use:** online connector enablement, not local scan elevation.
- **Content:** what identifier is disclosed, to which connector class, why, and how to revoke.
- **Actions:** `I approve`; `Keep local-only`.

### PCS-SCN-07 — Scan error

- **Content:** plain failure category, whether previous data is safe, provider-specific detail.
- **Actions:** `Try again`; `Use local-only mode`; `Copy diagnostic details`.

## 5. Results and findings

### PCS-RES-01 — Scan results

- **Content:** completion/freshness strip; grouped finding list; filters; search; selection summary.
- **Groups:** Ready to review; Needs a decision; Informational; Could not be checked.
- **Actions:** `Review selected`; `Export results`.
- **Rule:** no default selected finding.

### PCS-RES-02 — No findings

- **Content:** what was checked, source freshness, no-findings illustration.
- **Actions:** `View application inventory`; `Done`.

### PCS-RES-03 — Partial scan failure

- **Content:** provider/source result cards, completed findings, retry controls.
- **Rule:** do not discard successful provider results.

### PCS-FND-01 — Update finding detail

- **Content:** installed/candidate evidence; confidence; source/package ID; action state.
- **Actions:** `Add to review`; Pin; Ignore; Remind later.
- **Rule:** action only for live verified/high evidence.

### PCS-FND-02 — Unmanaged application detail

- **Content:** local metadata; why no approved source; safe manual mapping path.
- **Actions:** `Search approved source`; Ignore; Mark self-managed.
- **Rule:** no arbitrary URL, path or command input.

### PCS-FND-03 — App-owned storage finding detail

- **Content:** exact PC Steward root category, recoverable size, retained/active items, provider-cache exclusion.
- **Actions:** `Add cleanup to review`.

### PCS-FND-04 — Startup finding detail *(Future)*

- **Content:** entry evidence, source location, signature/publisher where available, uncertainty.
- **Actions:** no production mutation until approved product module exists.

### PCS-FND-05 — Configuration concern *(Future)*

- **Content:** observed setting, evidence, source, consequence uncertainty and manual guidance.
- **Rule:** no automated registry/configuration change.

### PCS-FND-06 — Permission notice

- **Content:** exact operation, why privilege is needed, when UAC appears, what happens on decline.
- **Rule:** scan never uses this state for elevation.

## 6. Review and remediation

### PCS-REV-01 — Selected action queue

- **Content:** ordered action list, grouping, sequential badge, per-item effect/reversibility, total app-owned space.
- **Actions:** remove item; Back; `Continue review`.
- **Empty:** `Select findings before review`.

### PCS-REV-02 — Final confirmation

- **Content:** immutable targets, consequences, elevation/restart/running-app warnings, verification statement.
- **Actions:** `Confirm X actions`; Cancel.
- **Default:** Cancel.

### PCS-REV-03 — Execution

- **Content:** queue; active job; finished/pending items; elapsed time; bounded detail.
- **Actions:** `Stop after current item` where coordinator supports it.
- **Rule:** no generic percent for unknown installer progress.

### PCS-REV-04 — Batch outcome

- **Content:** successful, failed, skipped, cancelled, restart and inconclusive groups.
- **Actions:** context recovery; `Delete downloaded files` when applicable; `View activity`.
- **Rule:** mixed batch never has a single success status.

## 7. Applications and updates

### PCS-APP-01 — Application inventory

- **Content:** command bar; search; filters; sortable virtualized table/list.
- **Columns:** application, version, status, publisher, scope, source/freshness.
- **Actions:** open row; export.
- **States:** no scan, loading, empty filter, offline/cached.

### PCS-APP-02 — Product detail

- **Content:** product summary; install instances; update intelligence; decisions; timeline.
- **Actions:** Review update; map; pin; ignore; copy package ID.

### PCS-APP-03 — Installation instance detail

- **Content:** provider identity, version, publisher, scope, architecture, channel, source, safe path disclosure.
- **Rule:** separate user/machine and x86/x64 instances remain distinct.

### PCS-APP-04 — Raw diagnostics disclosure

- **Content:** bounded raw provider records and warnings.
- **Actions:** copy sanitized record.
- **Accessibility:** monospace content reflows/scrolls horizontally without hiding Close.

### PCS-UPD-01 — Updates

- **Tabs:** Ready to review; Needs decision; Pinned/ignored; Unavailable.
- **Content:** source freshness; batch selection; update rows.
- **Rule:** review-required items cannot enter executable selection.

### PCS-UPD-02 — Update evidence review

- **Content:** installed/candidate comparison, match rationale, package/source identity, run/UAC/restart warnings.
- **Actions:** `Install approved update`; Cancel; Pin; Ignore.

### PCS-UPD-03 — Source disagreement

- **Content:** all competing source candidates with versions and timestamps.
- **Actions:** choose preferred source only through approved mapping workflow; no automatic selection.

## 8. Startup and storage

### PCS-STA-01 — Startup applications *(Future)*

- **Banner:** `Future concept — read-only in this native design prototype`.
- **Content:** name, publisher, enabled state, source, evidence label, search/filter.
- **Action:** open detail only.

### PCS-STO-01 — PC Steward storage

- **Content:** app-owned segmented visualisation; category cards; quota/retention; provider-cache boundary.
- **Actions:** navigate to Privacy clear controls.
- **Core scope:** safe to implement now.

### PCS-STO-02 — Device storage analysis *(Future)*

- **Banner:** product approval required.
- **Content:** aggregate chart, user-approved locations, read-only category detail.
- **Rule:** chart region itself never deletes.

## 9. Activity

### PCS-ACT-01 — Activity timeline

- **Content:** filters for scan/update/cleanup/decision/schedule; virtualized timeline/table.
- **Actions:** open result; export/copy sanitized diagnostics.

### PCS-ACT-02 — Scan run detail

- **Content:** trigger, mode, phases, provider timing, warnings, cancellation/failure, freshness.

### PCS-ACT-03 — Update job detail

- **Content:** target evidence, event timeline, provider outcome, verification outcome, exit code in technical details.

### PCS-ACT-04 — Cleanup detail

- **Content:** category, requested/recovered size, deleted/residual counts, residual reasons, preserved scope.

### PCS-ACT-05 — Empty history

- **Content:** explanation that activity is stored locally; link to scan.

## 10. Privacy and data

### PCS-PRV-01 — Privacy & locally stored data

- **Content:** local-first summary; connector contact summary; data category cards; export.
- **Primary:** no global destructive action.

### PCS-PRV-02 — Clear scan data dialog

- **Content:** explicit Deletes/Preserves columns.
- **Actions:** `Clear scan data`; Cancel.
- **Default:** Cancel.

### PCS-PRV-03 — Clear downloaded files dialog

- **Content:** recoverable size, exact app-owned scope, active/provider cache exclusions.
- **Actions:** `Delete downloaded files`; Cancel.

### PCS-PRV-04 — Partial cleanup result

- **Content:** recovered size; residual count; accessible list of reason categories.
- **Actions:** `Try again`; `View details`; Close.

### PCS-PRV-05 — Advanced data controls

- **Content:** source metadata, logs, update history and mappings/policies as independent categories.
- **Availability:** Phase 4 Task 28 only.

### PCS-PRV-06 — Factory reset zone

- **Content:** separated bordered danger section, scope summary, inactive-work requirement.
- **Action:** `Factory reset PC Steward`.

### PCS-PRV-07 — Factory reset scope confirmation

- **Content:** full reset manifest and preservation statement.
- **Actions:** `Continue to final confirmation`; Cancel.

### PCS-PRV-08 — Factory reset phrase confirmation

- **Content:** `Type RESET to confirm`; installed-app preservation; next-launch verification.
- **Action:** disabled `Factory reset PC Steward` until exact phrase.
- **Default:** none/destructive action not Enter-activated.

### PCS-PRV-09 — Reset pending

- **Content:** app close/restart requirement; no access to old state.

### PCS-PRV-10 — Partial reset recovery

- **Entry:** next-launch verification finds residual.
- **Content:** categories not removed, no old app data loaded, safe retry/support options.
- **Rule:** onboarding must not appear until complete.

## 11. Settings

### PCS-SET-01 — Sources

- **Content:** essential local sources, optional local provider, online sources, trust tier, last success, disclosure state.
- **Actions:** enable/disable, refresh, inspect disclosure.

### PCS-SET-02 — Schedule & notifications

- **Content:** Manual/Daily/Weekly, day/time, task conditions, notification toggle, last/next check.
- **Trust cue:** `Checks only. Scheduled work never installs updates.`
- **Availability:** Phase 4 Task 26.

### PCS-SET-03 — Appearance

- **Content:** System/Light/Dark; comfortable/compact table density; reduced motion follows Windows.

### PCS-SET-04 — Resource & retention

- **Content:** current fixed/default concurrency shown read-only unless product exposes safe options; cache quota; retention durations; no-resident-process statement.

### PCS-SET-05 — Decisions

- **Content:** pins, ignores, preferred sources and manual mappings.
- **Actions:** reverse; edit reason/expiry where supported.
- **Empty:** decisions are optional and reversible.

### PCS-SET-06 — Notifications permission/state

- **Content:** Windows notification availability and safe recovery.

## 12. About and support

### PCS-HLP-01 — About & support

- **Content:** product/version/build, supported platform, capability boundaries, privacy summary, licences, support links.
- **Actions:** copy sanitized diagnostics; view known limitations.

### PCS-HLP-02 — Diagnostic bundle review

- **Content:** categories included/excluded, path redaction state, privacy warning.
- **Actions:** `Create sanitized bundle`; Cancel.

### PCS-HLP-03 — Known limitations

- **Content:** unmanaged software is expected; no drivers/firmware/Windows Update; no arbitrary websites; no automatic close/update; no generic optimisation.

## 13. Empty, loading and error matrix

| Surface | Empty | Loading | Recoverable error | Blocking error |
|---|---|---|---|---|
| Overview | No scan / no findings | Restore previous content + subtle refresh | Partial provider/source | Local store cannot open |
| Applications | No scan / no filter match | Virtualized row skeleton after 300 ms | Raw details unavailable | Snapshot corruption with no valid previous state |
| Updates | No source check / no updates | Source check progress | One source unavailable with cache | No approved source evidence |
| Startup | Future/read-only | N/A in MVP | Evidence unavailable | Module not approved |
| Storage | No app-owned data | Size calculation | Locked artifact | Root validation failure |
| Activity | No activity | Page retrieval | One detail unavailable | Database unavailable |
| Privacy | Category size unknown | Per-card calculating state | Partial cleanup | Partial reset |
| Settings | No decisions | Save in progress | Setting rejected with reason | Schedule adapter unavailable |

## 14. Native prototype coverage

The authoritative WinUI design prototype in `design/native-prototype/` demonstrates:

- shell/navigation and light/dark modes;
- scenario switching;
- first-launch onboarding;
- scan configuration, progress, pause/resume and cancel concept;
- result grouping and finding details;
- selection and review queue;
- application inventory/search/detail;
- updates and evidence review;
- startup and device-storage future concepts;
- app-owned storage;
- activity;
- privacy/data cards;
- clear-data confirmation;
- double-confirm factory reset;
- settings;
- about/support;
- offline, no-findings and partial-failure states.

No native prototype action reads system state or performs a real change. Its navigation and state fixtures are the interactive review authority for this inventory.

The browser-rendered artifact in `design/prototype/` is superseded and retained only as historical visual reference. It must not be published as PC Steward, used as an implementation base, or treated as evidence of native Windows behaviour.
