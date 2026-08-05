# PC Steward UX specification

**Status:** Implementation-ready interaction specification  
**Product baseline:** `docs/product/PC_STEWARD_PRODUCT_SPEC.md` v0.2 plus the attached design brief  
**Product UI target:** native Windows 11 desktop application using WinUI 3 / Windows App SDK; no website or embedded browser UI  
**Authoritative interactive reference:** `design/native-prototype/`; `design/prototype/` is a superseded browser-rendered visual archive only  
**Current implementation reviewed:** Phase 3 desktop composition, presentation/domain models, requirements, ADRs, status, plans and review evidence

## 1. Product interpretation

PC Steward’s approved core is a local-first Windows software inventory and trusted update-orchestration product. Its promise is not “make the PC faster”; it is:

> Account for discoverable installed software, show the evidence behind its status, and make no change without explicit approval.

The attached brief also asks for unnecessary-file, startup, storage, configuration and broader maintenance experiences. Those surfaces are designed, but they are separated into scope tiers:

| Tier | Scope | Production authority |
|---|---|---|
| **Core / approved MVP** | Inventory, trusted update checks, review, sequential update execution, app-owned download cleanup, privacy/data controls, history, scheduling, settings | Implement against existing requirements |
| **Phase 4 approved** | Scheduling, notifications, advanced data controls, factory reset, accessibility/reliability hardening | Implement only in existing numbered task order |
| **Future / product-gated** | Startup applications, general storage analysis, configuration concerns, broad unnecessary-file review, “software to review” classification | Design reference only until the product spec, threat model, domain model and acceptance criteria are amended |
| **Prohibited unless separately approved** | Registry cleaning, arbitrary file deletion, drivers/firmware/Windows Update, vulnerability claims, arbitrary web installers, automatic uninstalls | No action affordance |

This separation resolves the conflict between the expanded design brief and the current source of truth without discarding the requested future information architecture.

## 2. Principal users

### 2.1 Primary: careful Windows power user

**Context**

- Maintains a personal Windows 11 PC.
- Installs software through vendors, Store and package managers.
- Understands versions and publishers but does not want to audit multiple tools.
- Values control over speed or automation.

**Needs**

- A fast answer to what requires attention.
- Proof that an update matches the installed product.
- A complete inventory including unmanaged items.
- Batch efficiency without losing per-item control.
- Clear local data deletion and export.

**Failure tolerance**

Very low for a wrong update or deletion; moderate for an unmatched item or deferred action.

### 2.2 Secondary: family technical helper

**Context**

- Periodically checks another household member’s PC.
- Needs plain explanations and a record of what changed.
- May export an inventory for comparison or support.

**Needs**

- Guided review without oversimplifying evidence.
- Reversible pins/ignores.
- History and sanitized diagnostics.
- No unattended background behaviour.

### 2.3 Occasional non-technical user

Not the original primary persona, but the design should let this user:

- understand that scanning does not change the PC;
- distinguish “ready to review” from “danger”;
- choose local-only operation;
- cancel safely;
- know exactly what a confirmation will do.

### 2.4 Advanced diagnostic user

Needs efficient access to:

- install instances, scope, architecture and provider;
- package/source identity and confidence evidence;
- live/cached timestamps;
- job events, exit state and sanitized logs;
- copy/export actions.

Technical depth is available through disclosure sections and detail panes, not mixed into the default summary.

## 3. Primary use cases

1. Run a first local-only inventory without elevation.
2. Run a full approved source check.
3. Understand current, verified update, review-required, unmanaged, ignored, pinned and unavailable states.
4. Inspect an application and its installation instances.
5. Review evidence for one update.
6. Select and review a sequential update batch.
7. Understand and recover from a failed or inconclusive update.
8. Export inventory or sanitized diagnostics.
9. Clear scan data without losing settings, history, decisions or downloads.
10. Clear only PC Steward-owned downloaded files.
11. Review and reverse pins, ignores and manual mappings.
12. Configure manual/daily/weekly scan-only scheduling.
13. Factory-reset PC Steward after two deliberate confirmations.
14. In future: inspect startup entries and storage/configuration findings without granting them automatic change authority.

## 4. Risks and trust requirements

| Risk | UX control |
|---|---|
| Wrong product/package match | Show source, package ID, installed/candidate version, architecture, scope, channel, confidence and explanation before action |
| Stale evidence appears live | Persistent `Live`, `Cached`, `Local-only` or `Unavailable` label with timestamp |
| Scan appears to make changes | Onboarding and scan configuration say “Scanning only reads information”; separate scan and action pages |
| Accidental batch action | No preselection by default; explicit `Review selected`; final immutable queue review |
| Application closure/data loss | Running-app notice, no force-close control, user must close or explicitly continue |
| False success | Outcome stays `Verifying` until exact post-update evidence; separate `Inconclusive` and `Restart required` |
| Broad/unsafe deletion | Data category cards, exact delete/preserve copy, app-owned boundary, active-work lockout |
| Accidental factory reset | Visually separate zone, first scope review, second phrase confirmation, no default destructive button |
| Privacy misunderstanding | Local-first status in onboarding and Privacy; connector consent at point of enablement |
| Alarmism | No health score, fake urgency, countdown, “critical” maintenance label or red minor findings |
| Technical overload | Summary first, progressive evidence disclosure and clear advanced labels |
| Background-resource distrust | Show last/next schedule; state “No tray app or resident service”; idle UI has no activity animation |

## 5. Product mental model

PC Steward uses a four-step loop:

1. **Inspect** — read local inventory and, if approved, source metadata.
2. **Explain** — group evidence into current, actionable, review and unavailable states.
3. **Review** — user selects exact actions and sees consequences.
4. **Verify** — record what happened and confirm the resulting state.

The shell and copy should reinforce these verbs. “Clean” is not a top-level product verb.

## 6. Information architecture

### 6.1 Primary navigation

The production shell exposes five stable task destinations:

1. Overview
2. Applications
3. Updates
4. Activity
5. Settings

About & support is a footer utility. Scan, scan results, review actions, repository/current applications, storage, privacy & data, onboarding, and product detail are contextual routes. They remain reachable from the relevant principal page and map selection back to that principal destination; no supported capability is removed.

The expanded rail is 232 px and the compact rail is 56 px. It enters compact mode below 900 effective px and expands at 1100 effective px. Every item retains a complete automation name when its visible label collapses. Selected items use a leading indicator, themed subtle-brand surface, icon, and text so selection remains legible in light, dark, and High Contrast themes.

### 6.2 Native navigation destination model

| Page key | Screen |
|---|---|
| `Overview` | Current state and next best action |
| `Applications` | Searchable, filterable installed-application inventory |
| `Updates` | Verified, review-required, and unavailable update evidence |
| `Activity` | Local inventory, decision, mapping, and outcome ledger |
| `Settings` | Sources, schedule, decisions, and local data |
| `Scan` | Scan configuration or active progress |
| `ScanResults` | Completed/partial/no-findings results |
| `ReviewActions` | Cleanup/remediation/update review queue |
| `ProductDetail` | Product and install-instance detail for the selected product ID |
| `UpdateReview` | Immutable evidence review for the selected update ID |
| `RepositoryApplications` | Installed applications with concrete approved-source identity |
| `CurrentApplications` | Installed applications with exact current source evidence |
| `Storage` | PC Steward-owned scan and downloaded-artifact storage |
| `PrivacyData` | Local data inventory, export, clear and reset |
| `Onboarding` | Local-first disclosure and first inventory |
| `AboutSupport` | About, limitations, diagnostics and help |

These are native `NavigationView` destination keys in the existing WinUI host. Selection state, selected product identity, and contextual return destinations must preserve the specified navigation and return behaviour.

### 6.3 Overview hierarchy

1. Current state sentence.
2. Primary next action.
3. Freshness/source label.
4. Attention summary grouped by actionability.
5. Recent activity.
6. Privacy/resource reassurance.

Avoid a grid of equal-weight KPI cards. The first visible region of the app window should answer: “What did PC Steward learn, and do I need to decide anything?”

## 7. Core journeys

### 7.1 First launch and onboarding

**Step 1 — Welcome**

- Title: `Know what is installed. Decide what changes.`
- Explain local-first operation, no account, no scan elevation and no automatic changes.
- Primary: `Continue`.
- Secondary: `Read privacy summary`.

**Step 2 — Choose first scan**

- Recommended card: `Local-only inventory` — reads Windows inventory sources and makes no network request.
- Alternative: `Inventory and update check` — discloses that WinGet sources receive installed package identifiers.
- Connector list includes trust category and state.
- Do not pre-enable an online connector without consent.

**Step 3 — Review**

- Summarise providers, network access and storage.
- `Start first scan` is explicit.
- `Back` remains available.

**Completion**

- Move to Scan progress.
- On failure to initialise local storage, show a plain summary and `Try again` / `Open support details`.

### 7.2 Configure and start a scan

Scan page has mode cards:

- `Local inventory` — recommended when privacy/offline is the priority.
- `Inventory + trusted update sources` — only enabled after source approval.

Configuration:

- provider checklist with essential provider locked on;
- source freshness preview;
- estimated target: local under 15 s p95, full under 90 s p95 on baseline, labelled as targets rather than promises;
- explanatory note: “Scanning does not install, uninstall or delete anything.”

`Scan this PC` becomes disabled if a scan/update/cleanup lease is active and explains why.

### 7.3 Scan progress

Always show:

- current named phase;
- elapsed time;
- determinate overall progress when available;
- per-provider phase list;
- `Pause` only if orchestration genuinely supports pausing; otherwise omit in production;
- enabled `Cancel scan`;
- copy: “Your previous completed inventory remains available until this scan finishes.”

The native design prototype’s Pause fixture demonstrates the desired state. Production must not ship Pause until the orchestrator supports it safely.

**Cancellation**

- First click requests cancellation without a confirmation dialog; cancelling a read-only scan is safe.
- Button becomes `Cancelling…`.
- State announces cancellation request within two seconds.
- Final state offers `Start again` and links to previous results if available.

### 7.4 Results

Results begin with factual summary:

- `3 updates are ready to review`
- `2 items need your decision`
- `61 applications are current`
- `6 have no approved update source`

Categories:

- Ready to review.
- Needs a decision.
- Informational.
- Could not be checked.

Each finding row shows:

- category icon + text severity;
- title and one-line explanation;
- exact scope/value where relevant;
- confidence label;
- source/freshness;
- selection only when an action is supported.

No result is selected automatically. `Review selected` appears only after selection.

### 7.5 Finding detail

Open in right pane on large windows and full page on small windows.

Anatomy:

1. Title, status and scope.
2. Plain-language finding.
3. `Why this appears` evidence.
4. `What PC Steward can do`.
5. `What it will not do`.
6. Detailed metadata disclosure.
7. Context actions.

Low-confidence or future-only findings show `No automatic action is available` and never show a disabled primary action without explanation.

### 7.6 Review and apply changes

The review queue is the action boundary.

- Show exact ordered items.
- Group by action type: update, app-owned file cleanup, decision change.
- Each item shows target, effect, elevation/restart/app-closure expectation and reversibility.
- Batch updates are visibly `Sequential`.
- User can remove any item.
- No collapsed hidden items.
- `Confirm X actions` uses a count and is never labelled `Fix all`.
- If an action has no rollback, state that before confirmation.

At confirmation, the queue evidence is frozen. If evidence changes before execution, stop and return to review.

### 7.7 Update review

Side-by-side evidence:

| Installed | Candidate |
|---|---|
| Product/display name | Source package |
| Version | New version |
| Publisher | Publisher |
| Scope | Supported scope |
| Architecture | Supported architecture |
| Channel | Channel |

Below:

- source and exact package ID;
- match confidence and reasons;
- live source timestamp;
- running-application detection;
- UAC and restart expectation;
- explicit verification statement.

Primary label: `Install approved update`. Default dialog button is `Cancel`.

### 7.8 Update execution and outcome

Execution page shows sequential queue, active item, elapsed time and bounded provider message. It does not show fake percent completion when the provider has none.

Outcome statuses:

- `Verified success`
- `Restart required`
- `Verification inconclusive`
- `Failed`
- `Cancelled`
- `Skipped`

Each has one safe next action. A mixed batch never receives a single success headline.

If PC Steward-owned downloaded files remain, show:

`640 MB of PC Steward-downloaded files can be removed. This does not affect the installed update.`

Action: `Delete downloaded files`.

### 7.9 Applications

Default table columns:

- Application.
- Installed version.
- Status.
- Publisher.
- Scope.
- Source/freshness.

Filters:

- status;
- publisher;
- provider/source;
- scope;
- classification;
- decision state.

Search matches application name, publisher and exact package ID. Raw paths are not searched by default.

Product detail includes:

- canonical product summary;
- separate install instances;
- current update intelligence;
- source candidates;
- decisions;
- timeline;
- advanced raw provider diagnostics.

### 7.10 Startup applications *(future / read-only concept)*

The designed screen shows:

- startup entry name and publisher;
- enabled state;
- source location;
- measured/declared impact evidence;
- last observed run if future evidence permits;
- Microsoft/OS protection and uncertainty.

No red “high impact” solely from registry location. `Disable` remains unavailable until a dedicated product requirement, rollback model and test set exist. The native design prototype labels this surface `Future concept`.

### 7.11 Storage analysis

**Core current scope**

- PC Steward scan data.
- PC Steward-owned downloaded files.
- Source metadata.
- Logs/history.

**Future device scope**

- Read-only aggregate categories from explicitly approved locations.
- No broad deletion from a visualisation.
- Drill-down evidence before any action.
- Provider/Windows caches remain outside PC Steward authority.

The first implementation of the `Storage` page key should navigate only to PC Steward’s own data categories.

### 7.12 Privacy and locally stored data

Top reassurance:

- `Stored on this PC`.
- `No PC Steward account`.
- connector contact summary;
- last network contact.

Data cards:

1. Scan data.
2. Downloaded update files.
3. Source metadata.
4. Logs & update history.
5. Mappings, pins & ignores.
6. Settings & consent.

Each card shows:

- approximate size/count;
- last updated/cleaned;
- what it contains;
- export/clear control;
- preserved categories.

Factory reset has its own separated zone after all routine controls.

### 7.13 Clear scan data

Dialog:

- Heading: `Clear scan data?`
- Deletes: inventory, raw provider records, candidates, scan runs and provider timing.
- Preserves: settings, sources, schedule, update history, downloaded files, mappings, pins and ignores.
- Primary: `Clear scan data`.
- Default/focused action: `Cancel`.

Success returns Overview to no-scan state. A toast offers `Scan again`, not Undo; deleted scan data cannot be reliably restored.

### 7.14 Clear downloaded files

Dialog states:

- exact recoverable size;
- deletes only inactive PC Steward-owned payloads/extraction files;
- preserves installed apps, active update files, provider caches, scan data and history.

Partial success lists residual count and reason. It never says “cleared” while residuals remain.

### 7.15 Factory reset

Factory reset is unavailable while work is active.

**Confirmation 1 — scope review**

- full deletion manifest;
- explicit preservation of installed third-party apps and PC Steward binaries;
- schedule removal;
- next-launch behaviour;
- primary: `Continue to final confirmation`;
- default: `Cancel`.

**Confirmation 2 — deliberate act**

- heading: `This cannot be undone`.
- user enters `RESET`.
- destructive button remains disabled until exact entry.
- button label: `Factory reset PC Steward`.
- it is not the default button; Enter in the text field does not activate it.

After acceptance:

- show `Reset will complete when PC Steward restarts`.
- close without reopening old state.
- next launch verifies cleanup before onboarding.
- any residual produces a blocking partial-reset screen; old data never loads behind it.

### 7.16 Activity and history

Unified timeline filters:

- scans;
- update jobs;
- data cleanup;
- decisions;
- schedule changes.

Rows show:

- outcome icon + text;
- target/summary;
- trigger;
- timestamp;
- duration;
- unresolved status.

Detail shows events in plain language. `Technical details` reveals correlation ID, package ID, exit code and sanitized output. Export/copy actions warn about inventory sensitivity.

### 7.17 Settings

Settings sections:

- Sources.
- Schedule & notifications.
- Appearance.
- Scan & resource preferences.
- Retention.
- Decisions.
- Privacy & data (link to dedicated page).

Windows conventions:

- toggles change simple reversible preferences immediately;
- source enablement that affects privacy opens teaching/consent content before change;
- destructive operations never live as a bare toggle;
- page search may be added only when settings exceed roughly 25 distinct controls.

### 7.18 About and support

Show:

- version/build/channel;
- supported Windows version;
- local-first privacy summary;
- known limitations;
- links to release notes, privacy, licences and support when available;
- `Copy sanitized diagnostics`;
- `Open data folder` only if safe and genuinely useful;
- explicit statement that PC Steward is not antivirus, registry cleaner, driver updater or Windows Update manager.

## 8. State model

### 8.1 Global operation states

| State | UI behaviour |
|---|---|
| Idle/no scan | Overview empty state; primary `Scan this PC` |
| Idle/cached scan | Show last completed results and freshness |
| Configuring | Scan options editable |
| Scanning | Progress screen; conflicting actions disabled |
| Paused *(future support)* | Animation stopped; Resume/Cancel |
| Cancelling | Controls disabled except safe navigation; live announcement |
| Scan cancelled | Previous snapshot preserved; retry available |
| Scan complete | Results and source freshness shown |
| Update review | Evidence frozen for review only |
| Updating | Scan and data cleanup unavailable |
| Verifying | Do not show success yet |
| Data cleanup | Dependent controls unavailable |
| Reset pending | App will close; old data must not reopen |
| Partial reset | Blocking recovery screen before normal shell |

### 8.2 Result states

| State | Presentation |
|---|---|
| No findings | Calm completion illustration; timestamp and what was checked |
| Findings | Group by actionability, not fear |
| Partial scan failure | Successful results remain; failed provider card and safe retry |
| Permission required | Explain which action, why, when and what happens if declined |
| Offline | Local inventory/history remain available; cached source age is explicit |
| Empty filtered list | Preserve filters, say no match, offer `Clear filters` |
| Loading initial state | Skeleton only after 300 ms; never replace known cached data with blank shimmer |
| Recoverable error | Plain summary + primary recovery action |
| Blocking safety error | Explain that no change occurred; show support/diagnostics path |

## 9. Dialog rules

- Modal only for consequential confirmation, blocking input or a short bounded choice.
- Destructive dialogs use `DefaultButton = Close/Cancel`.
- First focus goes to the heading, non-destructive action or required input.
- Dialog title is a question for routine actions and a consequence for final destructive actions.
- Consequence copy is visible without expanding.
- Never stack dialogs. Close the parent before opening the next step.
- Return focus to the invoking control.
- Escape always cancels unless work has already begun, in which case it closes only the informational view.

## 10. Notifications and undo

### 10.1 In-app notifications

- Success toast: 5–7 seconds, dismissible, live region `polite`.
- Error/partial result: persistent banner or dialog, live region `assertive` only when user action is required.
- At most one toast stack in the bottom-right content area.

### 10.2 Undo policy

Use Undo only for immediately reversible local preference changes:

- pin/ignore decision;
- filter preset;
- hiding a notification.

Do not offer fake Undo for:

- installed updates;
- cleared scan data;
- deleted downloaded files;
- factory reset;
- source refresh.

Instead use explicit review, confirmation and recovery guidance.

## 11. Keyboard model

Global:

- `Alt+1…9`: optional documented shortcuts to major destinations; do not conflict with Windows.
- `Ctrl+F`: focuses page search when present.
- `Ctrl+R`: refreshes the current read-only view only; never executes an update.
- `F6`: cycles navigation, command bar, content and detail pane.
- `Escape`: closes flyout/detail pane/dialog or clears transient selection.

Lists/tables:

- Arrow keys move rows.
- Space toggles selection checkbox.
- Enter opens detail; it never directly executes a change.
- `Shift+F10` opens a context menu containing only the same actions available visibly.

Dialogs:

- Tab stays trapped within the dialog.
- Escape cancels.
- Enter activates only the designated safe default.

## 12. Screen-reader model

- One `Navigation` landmark, one `Main` landmark and contextual complementary/detail region.
- Page title receives programmatic focus after navigation.
- Status counts are a labelled list, not an unlabeled number grid.
- Progress announces phase changes, not every percent.
- Table header names and sort direction are exposed.
- Freshness is part of the accessible status name.
- Status chips have names such as `Status: Review required`.
- Charts have adjacent summary and data table equivalents.
- Dialog descriptions reference both delete and preserve lists.
- Decorative logo/empty-state SVGs are hidden from accessibility when adjacent copy conveys the same meaning.

## 13. Native prototype state fixtures

The authoritative WinUI prototype in `design/native-prototype/` contains a **Prototype state** control to make view-model states reviewable without hidden test setup:

- First launch.
- Ready/cached.
- Scanning.
- Scan complete.
- No findings.
- Partial failure.
- Offline.

The control is native design-review instrumentation only and must not appear in production. The superseded browser artifact in `design/prototype/` is not an alternative implementation or source of interaction truth.

## 14. Success criteria

A successful implementation allows:

- a first-time user to start a local-only scan without guessing;
- a non-technical user to understand that nothing changes during scanning;
- an advanced user to reach package/source evidence in one additional view;
- a keyboard user to complete scan, review, update and data-control journeys;
- a user to distinguish live, cached, local-only and unavailable data everywhere;
- no destructive or executable action from a low-confidence, stale or unreviewed finding;
- clear separation of current approved product capabilities from future concepts.
