# PC Steward: curated app redesign

**Status:** Written specification for Dan's review; product implementation is not authorized by this document.
**Date:** 2026-10-09. **Inspected base:** `e6339762a0e72abcb782338df9ce1cd3295aac7f`, development branch `ccr-e4805080-7gyz6n`; upstream rechecked before writing and unchanged.
**Audience:** One owner on their Windows 11 x64 PC. Local-first, native WinUI; no account, distribution or framework migration.

## 1. Agreed intent and decisions

Dan wants a curated list of applications that PC Steward can install and keep current through exact WinGet or Chocolatey identities. Updates happen when he requests them by default, including an Update all button; he may separately opt particular apps into automatic updates. New installations always remain explicit. He approved the proposed direction and specifically requested recognizable icons for managed apps.

The selected interface combines **A's clean My Apps list**, **B's curated Add Apps card library and review tray**, and **C's optional technical details inspector**. These are design concepts, not three separate implementations. My Apps is the default screen; Add Apps, Activity and Settings are secondary destinations. Full detected inventory remains available through All installed software. This revises the six-destination layout in the October overhaul while preserving its functional outcomes.

| Decision | Default / rule |
|---|---|
| Updates | Manual; opening, selecting or checking never installs or updates. |
| Update all | Eligible, managed, installed apps in Dan's list only; no missing-app installs or blanket repository upgrade-all. |
| New installs | Explicit selection, live resolution, review, agreements and final confirmation. |
| Automatic updates | Off for every newly managed app; independent explicit per-app opt-in, delivered separately from the initial manual workflow. |
| Provider | One active provider identity per app; installed apps keep their managing provider. New installs use the validated catalog default, normally WinGet, or Dan's explicit supported choice. |
| Running apps / restarts | Defer running apps by default. No forced closure or forced restart. Manual graceful close requires its own explicit consent; automation never closes apps. |
| Removal | Remove management, not installed software. Disable future automatic permission through its proper authority. |
| Icons | Real publisher/app icons from vetted bundled assets or safe trusted local resources; deterministic fallback when unavailable. |

Authoritative references are product specification v0.3 §27, D-007, ADR-017/018/020/023/024/025 and `docs/PROTECTED_UI_BEHAVIOURS.md`. This document refines Tasks 50–53 and the later automation delivery in Tasks 54/55. Requirements addressed: `FR-CAT-*`, `FR-INS-*`, `FR-BAT-*`, `FR-PRV-01–04`, `FR-POL-06`, `FR-AUT-01–10`, `FR-RSC-01–05`, `NFR-PERF-01–10`, `AC-V3-01–11`; new design acceptance items below are `CRD-01–12`.

## 2. Four separate states, one stable app identity

Each curated app has a stable catalog-entry ID and one or more allowlisted provider/source/package identities. Display names, icons, source-table strings and database rows never grant execution authority.

1. **Managed membership:** Dan chose this catalog entry for My Apps. Membership persists even when the app is missing. It does not mean installed, approved or automatic.
2. **Detected installation:** Fresh or timestamped inventory identifies none, one exact managed instance, or an ambiguous/conflicting/unknown result. Missing requires reliable absence evidence; an unavailable provider is not evidence of absence.
3. **Queued selection:** Temporary Install or Update choices in the current review tray. Selection is reversible and is not approval. Queue state is not an automatic policy and is not persisted as unattended work.
4. **Automatic-update permission:** A separate durable per-app choice, Off by default. It only permits eligible future updates through a supported automation stage; it never permits initial installation, automatic adoption or reinstalling an app Dan removed.

Use a small additive membership store keyed by catalog-entry ID. Retain existing policy/decision and user-catalog stores; do not reset or discard the current database. Existing per-product decisions must be associated with a curated entry only through a unique exact identity. Preserve restrictive pins, skips and ignores; unresolved mappings remain visible and cannot inherit automatic permission. The implementation plan must specify that adapter/migration and its compatibility tests before coding.

Adding a card to My Apps changes membership only. Confirming an explicit Install also adds that reviewed entry to My Apps, with automatic updates Off; a failed install leaves the managed entry visible as missing/failed. Cancel before confirmation creates no new membership through this install path. Removing management drops future queue eligibility and disables automatic permission, while leaving the app installed and its historical outcomes intact. Remove/change-provider actions are disabled for an app participating in active work. Later Stage B removal uses the protected-policy operation; if elevation is declined, report removal pending and apply the restrict-only pause rather than claiming the elevated policy was removed.

## 3. Interface and user journeys

### My Apps

Open on a compact, virtualized list of managed apps, not the complete registry inventory. The header contains search, filters for Updates/Missing/Current/Needs attention, last check time, Check for updates and Update all (N). Provider/scope filters live in a compact menu. Counts summarize the list without replacing it with a dashboard.

Each row shows recognizable icon/name, installed and available versions, active provider, scope/admin badge, text status and a menu/details action. A separate checkbox queues a manual action. Automatic-update status is visibly separate, showing Off, Enabled, Paused, Deferred or Unavailable with schedule and last outcome available directly or in a row expander. Do not display Enabled while no functioning host/capability exists.

Use Current, Update available, Missing, Needs review, Installed not managed, Unavailable, Skipped, Pinned and Ignored as understandable states. Show a freshness qualifier when evidence is cached. Needs review/unknown/ambiguous rows have Resolve, not an actionable checkbox. All installed software is a secondary inventory route that preserves existing filters, evidence and exports; it does not silently add every discovered program to My Apps.

Update all creates an **updates-only review** for eligible managed installed apps. It excludes missing, current, unmanaged, running, pinned, skipped, ignored, unavailable, stale or conflicting items. It is independent of any explicitly queued installs in the tray, so pressing Update all cannot unexpectedly submit them. Duplicate package/product identities are refused. The displayed count is an estimate until live resolution; changed evidence is withheld and explained before final approval. No provider's upgrade-all command is used.

### Add Apps and review tray

The library uses category cards with genuine app icons, publisher, exact provider identity and installed/missing state. It includes the validated bundled curated catalog and reviewed user additions. The current 20 starter entries are not assumed to be Dan's final reviewed list. Curate identities and icon assets during implementation; do not introduce a generic download directory or silently adopt a larger unverified catalog.

Add to My Apps manages an entry without executing it. Install selected queues only explicitly chosen missing entries. Installed apps offer Manage or Open in My Apps; unmanaged detected apps explain the mismatch. Explicit provider adoption remains a separately labelled Install and is deferred from the initial release.

Repository search is a deliberate action against WinGet or the fixed Chocolatey Community feed after source consent. Distinguish curated entries from Not in my catalog results. Show exact ID, publisher, provider and source before accepting a result. Reject ambiguous, mismatched or unavailable evidence. Adding a search result does not download or install it, and a raw search/display result cannot authorize execution.

The review tray carries explicit Install/Update labels and counts. Remove/clear only edits the temporary queue. A tray may contain a deliberately selected mixed batch; its review and confirmation say Install and update (N), not Update all. Scope and architecture come from supported live preflight, not guesses from the host or icon.

### Review, progress and outcome

Resolve queued entries live into exact provider/source/package/version/scope/architecture selections. Group one review into No admin needed and Needs admin, one prompt. Show old/target versions (Not installed for Install), required agreements/licensing, Chocolatey Community administrator-script disclosure, trust/readiness failures and running-app status. Required agreement acceptance starts unchecked and is recorded for the exact version/hash. Cancel is the default; final explicit confirmation alone creates the immutable approved batch.

Newly running apps are deferred/skipped. Manual graceful close may be requested separately before review, with a bounded wait and no forced-close control in this redesign; existing low-level force capability is never enabled by these UI actions. The helper never closes apps. Preserve cancellation after awaited preflight/history/trust checks and the fresh running-app check immediately before each elevated provider.

During a run, keep one compact global operation strip and per-item queued/preflight/installing/verifying/result state; show truthful phases, not fabricated percentages. Cancel remaining prevents unstarted items while allowing an already started provider to finish; interruption becomes Verification required. Use one existing data-operation lease, permitting only controlled verification scans during execution. Navigation can continue, but conflicting scans/settings/membership changes are disabled with an explanation.

Results distinguish Succeeded, Restart required, Failed, Not verified and Skipped/deferred, with per-item reasons. Exit zero alone never means success. Retry creates a new live-resolved review, never replays an expired batch. Restart required reports the requirement and leaves timing to Dan; PC Steward never initiates a forced reboot. Activity records these results with timestamps and exact identities while keeping raw diagnostics in details.

### Optional inspector and Settings

Details show identity, provider/source, scope/architecture, freshness/provenance, current policy, history and a reason an action is withheld. A side pane on wider windows becomes a dedicated detail view when narrow; it is never a mandatory evidence workbench for routine exact operations.

Settings separates Providers, Scheduled checks, Automatic updates, Notifications, and Data & reset. Retain existing scan/export/cleanup/reset capabilities and their distinct deletion/confirmation semantics. A source toggle retains its disclosure and starts no operation. About is contextual. Pause all automatic updates appears only with the implemented automation capability and is restrictive, not execution permission.

## 4. Architecture and reusable foundation

| Module | Responsibility and interface |
|---|---|
| Native shell / page views | Navigation, global operation state, adaptive layout and retained window lifetime. Split the current 1,482-line MainWindow by page; preserve semantic resources, virtualized templates and existing progress improvements. |
| Presentation models | Project immutable app/evidence/policy/operation snapshots into list/card/tray/inspector state. No process calls, provider commands or trust decisions. |
| Curated membership / policy orchestration | Compose validated catalog and user additions, membership, inventory and existing per-app choices. Membership changes have no installer side effects. |
| Provider health and preparation | Report typed presence/trust/version/source state; use existing `InventoryWorkflow.PrepareInstallSelectionAsync`, live update selection and `IInstallPreflightAuthority`. Read-only reports never become execution authority. |
| Review / approval / batch execution | Reuse `OperationBatchReview`, `OperationBatchApprovalService`, immutable persistence, `BatchExecutionCoordinator`, desktop/helper executors and v2 IPC. Enforce one active run, exact revalidation, safety gates and verification. |
| Icon resolver | Return bounded image data plus provenance or a fallback through an injected, cancellable interface. No provider or install authority; no arbitrary file/URL activation. |
| Later automation adapters | Reuse per-app policies and Windows scheduling infrastructure, then add the approved capability-minimal hosts. Keep scan-only schedules separate from automatic execution. |

Current Application/Domain/catalog/provider/SQLite/execution/reset boundaries remain. App.xaml.cs presently composes the older `SequentialUpdateBatchCoordinator` and upgrade review path; wire the existing catalog/install/new batch path deliberately, preserving current functionality during the transition. Do not mistake implemented engine classes for an already live desktop feature.

Persist membership/policy/history using existing transaction patterns plus an additive membership schema. New presentation/icon metadata is not command authority. Revalidate catalog/user rows on every read; invalid bundled catalog disables catalog actions while inventory remains usable. Clear scan data preserves stable membership/choices; factory reset removes them with existing safeguards. No icon work can block or widen the privileged helper.

## 5. Provider-health prerequisite and optional setup

Task 50 precedes actionable curated workflow delivery. Show WinGet presence/version/configured sources and Chocolatey presence/strict trusted-client result independently from connector consent. Differentiate checking, absent, untrusted, partial/unavailable and timestamped stale evidence. A failed probe is Unknown, not Absent. Configured sources do not prove remote reachability.

Use existing fixed-root, signature/signer, no-reparse and protected-layout checks. Chocolatey health must request the strict caller-cannot-modify-layout check; do not infer it from the default permissive resolver option. Untrusted client executables are not run to obtain a version.

Absent WinGet links to the fixed Microsoft Store App Installer listing; PC Steward does not install/repair WinGet. Positively absent Chocolatey may offer Set up Chocolatey only with trusted WinGet, source consent and protected machine capability. Setup is a normal explicit reviewed machine-scope WinGet Install of `Chocolatey.Chocolatey`, with live version/agreements, ordinary verification and post-install strict trust. It never enables source consent implicitly or runs a bootstrap script. The published setup wrapper is x86/machine even on the supported x64 PC; preserve applicable installer architecture in preflight/review rather than weakening architecture checks. Existing or untrusted Chocolatey gets explanation, not automatic reinstall.

## 6. Real app icons: provenance, extraction and bounded caching

Icons are explicitly in initial manual-workflow scope. They identify apps visually, not trust or readiness. Use a neutral placeholder/initials when no safe recognizable icon exists; never invent a logo or hide that it is a fallback.

Preferred source order is (1) vetted publisher icons bundled as normalized raster assets, keyed to exact curated identities in a presentation-only provenance manifest; (2) safe extraction from an already installed trusted local app/package; (3) deterministic fallback. The provenance manifest records publisher/source, review date, content hash and permission/source-use notes. Keep icon asset references outside execution metadata; current catalog validation must not gain arbitrary commands, paths or runtime URLs.

Remote icon discovery/fetch at runtime is excluded from the initial design. Publisher assets may be collected and vetted during development, then shipped with the owner-reviewed payload. Search metadata, web favicons and database-supplied URLs cannot be loaded automatically. User-added entries use safely identified local icons or fallback until a vetted bundled asset exists.

For local extraction, resolve a resource only from a trusted installation/package identity associated with the exact app. Treat ARP DisplayIcon strings as untrusted hints: parse resource identifiers as data, require a canonical permitted local path within that verified installation, reject UNC/device/alternate-stream/reparse escapes, validate signature/publisher where executable resources are used, and fail to fallback on any uncertainty. Extract resources using a non-executing Windows resource path; never launch the executable, shell a command, load code/DLL initializers, activate shortcuts/shell extensions or visit a URL to obtain an icon. Do not scan unrelated folders or users' private files.

Design limits: at most 1 MiB encoded input, 512×512 decoded pixels, two concurrent extraction/decoding tasks, 16 MiB in-memory image cache and 32 MiB disk cache using LRU eviction. Normalize to raster PNG and render logical 32px list/64px library icons with appropriate DPI variants. Remote SVG/HTML/animation is never accepted; approved vector assets are rasterized at development time. Reject malformed/oversized payloads; errors affect only that app's icon. Load visible/near-visible rows lazily, deduplicate requests, cancel abandoned work and negative-cache failures until source identity changes or explicit refresh. No idle polling.

Cache keys include exact app/provider identity, source/content hash and asset version so an update cannot retain an unrelated icon. Store derived cache only under an internally allowlisted PC Steward cache subdirectory, use atomic writes and existing reparse-resistant deletion rules, never delete source app resources. Factory reset removes derived cache; Clear downloaded files remains installer-cache-only. Clear scan data may invalidate derived icon mappings without removing vetted bundled assets or membership. Removing management evicts unused derived icons. Cache/provenance is display data, never executable input.

## 7. Responsive native layout and accessibility

Support a resizable native window with the existing semantic light/dark/high-contrast resources. Preserve the 900 epx compact and 1100 epx expanded navigation thresholds; validate the documented 900px minimum layout. The list and tray remain usable at that width; collapse nonessential columns into row details instead of horizontal control overflow. The inspector overlays/becomes a detail page; Add Apps cards reflow; review actions remain visible with long text and Windows text scaling.

Use keyboard navigation, Space for selection, Enter to open details, named buttons/checkboxes and visible focus. Enter/navigation never supplies final install consent; Cancel remains default. Screen readers receive app name, state, version/action/admin requirement independently of icon. Decorative icons do not repeat the row name. Status uses label/icon plus colour. Announce progress/errors without repeatedly rebuilding or stealing focus. Keyboard reachability, focus retention and truthful statuses are explicit redesign requirements; the broader optional ADR-021 accessibility matrix remains best effort rather than an invented release gate.

## 8. Separate automatic-update delivery

Initial delivery is the complete manual curated workflow. Retain existing stored opt-ins, but show execution capability accurately; unavailable automation controls explain that scheduled checks do not yet install updates. Do not silently activate old opt-ins on upgrade: review the actual automatic schedule and capability once before enabling the new execution host.

Existing `ScheduledCheckSettings` already provides Manual/Daily/Weekly, IsEnabled, notifications, weekly day and local start time; `ScheduledCheckStatus` stores last completion/result/failure count. The current Windows task is `PCSteward.ScheduledCheck`, interactive owner context, LIMITED privilege and fixed scan-host action. Its host checks/notifications only. Preserve those settings/task semantics and outcomes. `ProductUpdatePolicy.AutomaticUpdates` already defaults false, and user decisions persist opt-in/preferred provider/skip/close consent. The eligibility and automatic-update hosts remain Tasks 54/55; they are not new gaps in scan scheduling.

Later Stage A uses the separate current-user capability-minimal host in ADR-025: eligible user-scope WinGet updates only, no elevation, no resident process. Require per-app opt-in, exact catalog/user-confirmed mapping, Stable compatible installed scope/architecture, strictly newer live evidence, no pin/skip/ignore, catalog automation permission, recorded required agreements, target-version failure backoff and app not running. Skip on battery/metered connection, honour window/cap, record verification/history and expose the actual schedule/timezone and last per-app outcome. Machine items require notification/manual review. No initial install or automatic provider adoption.

Automatic schedule is a separate execution choice, not a relabelled scan schedule. On first enabling a supported app, explicitly review a Daily/Weekly local-time schedule; suggest an existing non-Manual check cadence/time when available, otherwise Daily 09:00 local, with no task enabled until confirmed. Share schedule between opted-in apps while preserving independent per-app permission. Changes preserve existing scan settings. Pause immediately restricts future work; it never grants permission. A missed/blocked run has a truthful status, not a promised completed update.

Stage B is deferred until about two weeks of clean Stage A use and a separate secure implementation plan/review. As ADR-025 requires, policy changes occur via UAC-approved one-shot helper; policy is administrator-protected under ProgramData, task runs highest privilege only while owner is signed in, no stored password, fixed protected host/no arbitrary arguments. The host trusts only protected policy, bundled catalog and fresh provider evidence, never per-user DB. Per-user pause is restrictive only. Chocolatey requires separate per-app Stage B consent. Reset/removal reports partial failure if UAC is declined. Do not present unattended elevated updates as available before this design is implemented, tested and explicitly enabled.

## 9. Errors, recovery and resource measurement

Keep per-provider failure independent and preserve the last good inventory. Unknown identity, ambiguous matching, provider disagreement, unavailable source, failed trust, changed agreement/selection, duplicate job, expiration and incomplete process snapshot all withhold actions with a plain-language reason and safe retry/details. No silent provider switch, installer fallback or trust exception. Interrupted work is Verification required; app startup recovers from persisted execution state before offering retry. Invalid icon only yields a fallback, not failed updates.

Reuse existing baseline performance harness and Windows measurements; record exact commit/build, host, fixture counts, cold/warm/icon-cache conditions and median/p95 results. Compare the same 300-record inventory and 1,000-entry/large-list fixtures before/after; include scrolling, resize, filtering, background icon decoding and active progress. Use virtualized list/card controls and bounded icon tasks; no full icon extraction at startup and no recurring disk/network work while idle.

Preserve `NFR-PERF`: local inventory p95 ≤15s; healthy online check p95 ≤90s; interactive cold shell ≤5s target; five-minute idle average ≤1% total CPU and working set ≤300 MB, no recurring disk/network; local scan average ≤20% CPU and peak <500 MB, no >50% CPU sustained >5s; scan cancellation acknowledged ≤2s and operable ≤5s; no resident UI process after closing without active work; scheduled host exits ≤30s after completion/cancellation/timeout; connector concurrency defaults to three. Report measurements and regressions honestly. Installer time is distinguished from PC Steward-owned work; no claims of meeting budgets without Windows evidence.

## 10. Acceptance and test plan

| ID | Required observable evidence |
|---|---|
| CRD-01 | Membership, installed state, queue and auto permission vary independently; Add/Remove management never runs uninstall/install. Existing choices/history survive migration and Clear scan data. |
| CRD-02 | Update all produces only live-eligible managed installed upgrades, ignores queued explicit installs, refuses duplicates/conflicts and never calls a provider upgrade-all. |
| CRD-03 | Missing selected apps produce exact explicit Install review; cached/unavailable absence cannot authorize it. Cancel/default/off agreement controls create no approval/installer call. |
| CRD-04 | One active provider per app; installed provider preserved; conflicting identity/preference requires resolution. Catalog/search rows and DB tampering cannot supply execution arguments. |
| CRD-05 | Shared review creates one digest-bound batch with exact licensing/agreements; mixed user/machine fixture yields at most one fake UAC launch. Changed/expired/replayed/substituted evidence is refused. |
| CRD-06 | Preserve last-precheck/pre-UAC/after-await cancellation and fresh per-elevated-item process regressions; running/unreadable snapshots skip. UI never enables forced close/reboot. |
| CRD-07 | Exit zero without provider/local verification is Not verified; restart remains user-controlled; interruption recovers to Verification required; retry requires a new review. |
| CRD-08 | Provider health fixtures distinguish absent/untrusted/partial/current versus consent. Optional Chocolatey setup uses only reviewed WinGet `Chocolatey.Chocolatey`; no bootstrap path; post-setup trust required. |
| CRD-09 | Recognizable vetted/local icons appear for reviewed managed fixtures. Wrong identity/signature/path/reparse/UNC/ADS/URL, malformed/oversize images and cache tampering yield fallback with no code/network execution. Verify quotas, eviction, DPI, cancellation and no source-file deletion. |
| CRD-10 | Keyboard/accessibility and 900px/scaled/high-contrast layout checks retain row selection, tray/review actions, Cancel default and focus. Unknown/error states remain legible and actionable only through safe resolution. |
| CRD-11 | Initial build never claims active automatic execution from scan-only settings. Later Stage A tests cover per-app Off default, explicit schedule review, eligibility/backoff/battery/metered/window/running deferral and history; no initial installs/elevation. Stage B has separate protected-policy/host threat tests before delivery. |
| CRD-12 | Windows benchmark evidence covers cold/warm/icon-cache/large-list/idle/scan/cancel resource budgets; closing leaves no unnecessary resident process. |

Use existing injected process/preflight/revalidator/executor/history/clock fixtures and MSTest; add membership/presentation/icon seams for pure executable workflow tests. Real installer, UAC acceptance, task mutation and provider setup are not part of fixture tests. Existing architecture scans supplement runtime tests for execution/no-bootstrap/host separation. Preserve the merged safety baseline; Windows CI passed all 593 tests before and after the safety-fix merge. Run focused suites then full Windows restore/build/test CI with the repository SDK pin. The local Windows machine presently lacks SDK 10.0.302; no relaxation or dependency installation is authorized by this design. A later owner live check requires separately authorized safe targets.

## 11. Delivery boundary and deferred items

First implement provider health/optional reviewed setup and the complete manual curated My Apps/Add Apps/inspector/review/progress/Activity flow, including genuine icons and stable membership. Then deliver separately reviewed per-app Stage A automation; Stage B remains a later secure milestone. Task-by-task implementation details, migrations and test commands belong in the written implementation plan after this spec is approved.

Deferred: unattended initial installs/reinstall/desired-state reconciliation; automatic provider adoption; uninstall; forced closes/restarts; restore points/New PC profile; custom installer sources/direct downloads; runtime remote icon discovery; generic large curated catalog feeds; distribution and framework replacement. Existing protected data/reset/export functionality is retained, not deferred or removed.

Self-review completed: no unresolved placeholders; manual defaults, explicit installs and automatic permission are separate throughout; icon data has no execution/network authority; existing scan scheduling/policy support is accounted for; Stage B is not promised; scope splits manual delivery from automation. This document is the review artifact, not approval to code. Next: review this specification, then prepare the implementation plan.
