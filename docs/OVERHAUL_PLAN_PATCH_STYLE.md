# PC Steward overhaul plan — Patch My PC–style updater on WinGet and Chocolatey

**Task:** 43 — Patch-style overhaul plan
**Status:** **Proposed — not approved.** This plan changes no requirement, ADR, safety boundary or code. Section 4 lists the spec, `AGENTS.md` and ADR changes that the product owner must approve (Task 44) before any implementation task starts.
**Date:** 2026-10-08
**Baseline:** commit `52b4313` (Task 42), product specification v0.2, ADR-001–022, 290/290 automated tests.
**Audience:** **The owner only, on their own PC.** The owner confirmed on 2026-10-08 that PC Steward is personal software (decision D-006). No distribution, other users or public release are planned. The protected owner-only installation (ADR-022) stays the deployment model.

---

## 1. Goal

PC Steward today puts evidence first. It produces a careful inventory and applies reviewed updates one at a time. The overhaul turns it into a **home "patch-everything" updater**, similar in use to Patch My PC Home Updater:

- one list of known applications, colour-coded **Outdated / Up to date / Not installed**;
- tick boxes and **one button** to silently install or update everything selected;
- **one administrator prompt** per run instead of one per application;
- a personal **catalog** of your own apps for installing, not only updating;
- optional **scheduled automatic updates** for applications the user opts in;
- **WinGet and Chocolatey** as the only execution engines.

The overhaul keeps the properties that make PC Steward trustworthy. Package managers stay the only source of installers. Commands are built by the provider and never read from data. The user approves an immutable plan. Success is reported only after verification.

### 1.1 What personal use changes

Because PC Steward runs only on the owner's PC, the plan is cut down. The safety rules stay. The release work goes.

| Area | Effect |
|---|---|
| Release and distribution | Production signing, distribution channels, other users and public release criteria (`FRA-14–16`) are **dropped permanently**. They no longer block anything. |
| Catalog | It is a **personal catalog**. It starts with the owner's own installed apps that WinGet or Chocolatey can manage, and grows through **Add from WinGet/Chocolatey search** in the app. Generic "top 300 apps" curation and a signed catalog download channel are dropped. |
| Automation | The owner is the only user and the PC's administrator, so unattended updates that need admin rights (Stage B, §9.3) are a reasonable goal. They follow Stage A once it has run cleanly. |
| Process | Each phase gets one owner review. Live testing replaces the separate evidence-readiness, review and audit tasks. Hostile-input tests stay, because malware or bad package data can still reach the PC. |
| Accessibility | Out of scope, as the owner already decided for Task 35 |

## 2. Target behaviours compared with today

The left column describes Patch My PC Home Updater behaviour as it is commonly described. Task 45 re-checks it against the vendor's current documentation before the UX is fixed.

| Behaviour | Patch My PC Home Updater (reference) | PC Steward today | Overhaul target |
|---|---|---|---|
| Curated list of known apps, including uninstalled ones | Yes, several hundred apps | No. Only installed software is shown. | **Personal catalog** (§7) plus **Add from search** |
| Colour-coded status | Red outdated, green current, neutral not installed | Evidence-led statuses (Verified, Review required, Unmanaged…) | Same three primary states. Evidence stays one click away. |
| Tick boxes and one "Perform" button | Yes | Batch review exists but is evidence-heavy | **Select → one review sheet → one confirmation** |
| Silent install/update | Vendor installers with silent switches | WinGet/Chocolatey `upgrade` only | WinGet/Chocolatey `install` **and** `upgrade` |
| Administrator prompts | The app runs elevated | One helper launch per machine-scope job | **One UAC prompt per approved batch** (§8.4) |
| Scheduled automatic updates | Yes | Scheduled *check* only (`FR-SCH-03`) | **Opt-in per app**: user-scope first, then unattended admin updates (§9) |
| Skip a version / ignore an app | Yes | Pin and ignore | Adds **Skip this version** |
| Close running apps first | Option | Warning only (`FR-UPD-05/06`) | **Per-app consent** to close gracefully |
| Uninstall | Yes | No | Optional later phase, through the provider only |
| Restore point before updates | Option | No | Optional later phase |
| Disable vendor auto-updaters, remove shortcuts | Options | No | **Not planned.** These are system tweaks outside the product boundary. |
| Own installer repository and vendor download URLs | Yes | No | **Never.** WinGet and Chocolatey are the only installers. |
| Enterprise Intune/ConfigMgr publishing | Separate product | No | **Never.** This is a home product. |

## 3. Carried forward unchanged

- WinGet and the fixed Chocolatey Community feed are the only providers. There is no direct download, mirror, raw URL or arbitrary website search (`FR-SRC-09`, `FR-UPD-13` stays Later).
- Providers build structured argument arrays themselves. Command strings, executable paths, package IDs and arguments are never taken from the database or remote metadata as authority (`FR-UPD-02/03`, `TM-03`).
- Confirmation creates an immutable, digest-protected operation record. Each item is revalidated live before execution (ADR-017).
- Elevation goes through the signed, one-shot, identity-checked helper. There is no resident elevated service or tray process (ADR-018). Stage B automation (§9.3) is the only proposed exception. It is a short-lived scheduled task, not a service, and has its own conditions.
- An item is Success only after provider and inventory verification, or a reliable restart-pending result (`FR-UPD-08/09`).
- Low-confidence, incomparable, architecture-conflicting or channel-conflicting candidates are not actionable (`FR-ID-04`, `FR-VER-04`).
- Scans never elevate. `Win32_Product` is never used. Drivers, firmware, Windows Update, registry cleaning and "optimisation" stay out of scope.
- The product stays local-first: no account, no telemetry, no inventory upload.
- Clear scan data, Clear downloaded files and Factory reset keep their separate semantics. New state is added to the correct deletion class (§10).
- Deployment stays the protected owner-only installation under `%ProgramFiles%\PC Steward` with the owner's local signer (ADR-022). Distribution is not pursued (§1.1).

## 4. Changes requiring owner approval (Task 44)

| # | Current rule | Proposed rule | Affected IDs / documents |
|---|---|---|---|
| C-1 | Updates apply only to installed, matched apps | Add **Install** of catalog apps, and **Add from search** of reviewed WinGet/Chocolatey results into the personal catalog | New `FR-CAT-*`, `FR-INS-*`; spec §1, §4.2, UJ-04/05 |
| C-2 | Non-goal "silently install updates"; principle 8; `FR-UPD-12` Later; `FR-SCH-03`; `AGENTS.md` "no unattended installation" | Allow **opt-in, per-app automatic updates** that meet a deterministic eligibility predicate. Default off. | `FR-UPD-12`, `FR-SCH-03`, new `FR-AUT-*`; `AGENTS.md`; ADR-020 |
| C-3 | One elevated helper launch per approved machine-scope job | One helper launch per **approved immutable batch**. Items run sequentially, with per-item revalidation and hard caps. | `FR-UPD-04/10/11`; ADR-018 revision |
| C-4 | Immutable job means one `UpgradePackage` selection | Immutable **operation** with a kind (`Install`, `Upgrade`, later `Uninstall`), grouped in a digest-protected **batch** | ADR-017 revision; helper protocol v2 |
| C-5 | Chocolatey candidates stay review-only until the user maps the exact Community ID | A **catalog-curated** Chocolatey ID, shown with its source disclosure in the batch review sheet, counts as that confirmation. Non-catalog IDs still need explicit mapping. | Spec §11.1 Chocolatey bullets; `FR-ID-05/06` |
| C-6 | `AGENTS.md`: "WinGet is the only actionable update provider" | Bring this in line with Task 32 (WinGet and fixed Chocolatey Community) | `AGENTS.md`, `FR-SRC-07` |
| C-7 | `FR-INV-11` Chocolatey inventory marked Later | Mark as delivered (Task 32) | Spec §9.2 |
| C-8 | `FR-UPD-06` never force-closes an app | Unchanged by default. Add an optional **per-app, user-granted** "close gracefully before updating" consent. Forced termination still needs a second, per-run consent. | `FR-UPD-05/06`, new `FR-BAT-*` |
| C-9 | Non-goal "uninstall applications automatically" | Keep. Add an **Optional** user-initiated, provider-based uninstall in Phase 10. | New `FR-UNI-*` (Optional) |
| C-10 | No PC Steward–owned catalog | Add an **app-bundled personal catalog**, covered by the owner-signed payload, as a new trusted data artefact. It never contains commands, URLs used for execution or installer switches. | New ADR-023; threat model |
| C-11 | Release gate `FRA-14–16`, production signing and the distribution channel stay open blockers | **Retire them permanently.** The product is personal and owner-only (D-006). | Spec §14.3, §19.8; `FINAL_RELEASE_AUDIT.md`; `STATUS.md` |
| C-12 | Every phase has an evidence-readiness task, a review and an audit | **One owner review per phase.** Owner live use on the protected install is the acceptance evidence. Automated hostile-input tests stay mandatory. | `AGENTS.md` workflow; `CODEX_BUILD_PLAYBOOK.md` |

## 5. Target product experience

### 5.1 Navigation (six destinations)

| Destination | Purpose |
|---|---|
| **Home** | Counts (Outdated, Not managed, Auto-update on), a primary **Update all safe (N)** button, last check time, provider health |
| **Updates** | Checklist of outdated apps. This is the main Patch-style screen. |
| **Catalog** | Install new apps by category or search. Shows installed state for every catalog entry. |
| **Installed** | The existing full inventory, including unmanaged software, with evidence and decisions (today's Applications page) |
| **Activity** | Run history: batches, items, results, retry |
| **Settings** | Providers, automatic updates, notifications, data and reset, catalog version, About |

### 5.2 Updates checklist

- Each row shows: checkbox, app icon/name, installed → available version, provider badge (**WinGet** / **Chocolatey**), scope badge, a UAC shield when admin is needed, and a status chip that combines colour, icon and text (colour is never the only signal).
- Toolbar: **Select all safe**, **Clear**, filter (provider/scope/auto-update), **Review and apply (N)**.
- *Safe* means the item meets the actionable predicate (exact identity, fresh live evidence, compatible channel and architecture, strictly newer, not pinned/skipped/ignored).
- Review-required items show **Resolve** instead of a checkbox. Resolve reuses the Task 38 decision actions (confirm identity, choose a candidate, pin, ignore). They can never be ticked directly.

### 5.3 Catalog

- Category rail: Browsers, Communication, Media, Imaging, Documents and office, Developer tools, Utilities, Compression, Cloud storage, Security, Runtimes, Gaming launchers.
- Each entry shows **Not installed** (neutral), **Up to date** (green), **Update available** (amber/red) or **Installed, not managed** (grey, e.g. installed outside any provider).
- Ticking a not-installed entry queues an Install. Options per entry: provider (when both exist), scope (user/machine where the provider allows), architecture.
- **Add from WinGet / Chocolatey search** (core, Phase 8) searches WinGet and the fixed Chocolatey Community feed only. Results are labelled *Not in my catalog*. Adding one needs its own review: exact ID, publisher and source. The user's confirmed choice is stored as a user-confirmed catalog entry, which can then be installed and, once opted in, updated automatically.

### 5.4 Review and apply

1. **Review sheet** (one screen) groups items as *No admin needed* and *Needs admin, one prompt*. It shows provider, source, package ID, version, scope and architecture for each item. It also shows:
   - WinGet package agreements and licences that must be accepted (shown, then recorded);
   - the Chocolatey Community disclosure: community-maintained package scripts run with administrator rights;
   - running-app warnings with a per-item **Close app for me** toggle (default off, or the stored per-app consent).
2. A single **Install and update (N)** confirmation creates the immutable batch.
3. **Progress panel**: per-item queue state, current phase (preflight, downloading/installing, verifying), and **Cancel remaining**, which takes effect between items (`FR-UPD-11`).
4. **Completion summary**: Succeeded / Restart required / Failed / Not verified, with **Retry failed** and **Delete downloaded files** where applicable (`FR-DAT-09`).

### 5.5 Per-app options

Skip this version · Pin · Ignore (indefinitely or until a date) · Automatic updates on/off · Preferred provider · Close-app consent · Install scope/architecture preference. All are reversible and shown together in Settings (`FR-POL-04/05`).

### 5.6 Settings → Providers

- **WinGet:** present or absent, version, sources enabled. Absent → link to the Microsoft Store *App Installer* page. PC Steward does not install WinGet itself.
- **Chocolatey:** present or absent, trusted-client check result (canonical path, signer, no reparse points). Absent → **Set up Chocolatey** installs the `Chocolatey.Chocolatey` package **through WinGet** as a normal machine-scope Install operation. The upstream PowerShell bootstrap script is never run.
- Provider precedence for new installs: catalog default, with WinGet preferred over Chocolatey unless the user overrides it.

## 6. Target architecture

```
 signed bundled catalog ─▶ PCSteward.Catalog ─┐
 local inventory (existing) ──────────────────┤
 provider snapshots (WinGet, Chocolatey) ─────┼─▶ Matching + AppState (Domain)
 app policies (stable decision store) ────────┘          │
                                                          ▼
                                     OperationPlanner (selection → plan)
                                                          │ user confirms once
                                                          ▼
                                     Immutable OperationBatch (digest)
                                         │                        │
                     user-scope items ◀──┘                        └──▶ machine-scope items
                NonElevated runner (WinGet)                 one UAC → ElevatedHelper v2
                                         │                        │ (WinGet machine, Chocolatey)
                                         └──────────┬─────────────┘
                                                    ▼
                                  Provider + inventory verification ─▶ history
```

| Project | Change |
|---|---|
| `PCSteward.Domain` | `CatalogEntry`, `DetectionRule`, `AppState` (NotInstalled, Current, Outdated, ReviewRequired, Pinned, Skipped, Ignored, Unsupported, Failed), `PackageOperationKind`, `PackageOperation` (generalises `UpdateJobSelection`; installed version optional for Install), `OperationBatch` (ordered, digest over the canonical text of every item), `AppPolicy`, a pure `AutomationEligibility` predicate |
| **`PCSteward.Catalog`** (new) | Loads the bundled catalog, validates it strictly (schema version, bounds, uniqueness, anchored non-backtracking patterns) and checks its hash against the signed manifest. No network access in v1. |
| `PCSteward.Application` | `CatalogWorkflow`, `OperationPlanner`, `BatchApprovalService`, `BatchExecutionCoordinator` (generalises `SequentialUpdateBatchCoordinator`), `ProviderHealthService`, `AutomaticUpdatePolicyEvaluator` |
| `PCSteward.Provider.WinGet` | `install` argument builder (user/machine), live preflight (`show --id --exact --version` or COM `FindPackages`), agreement extraction, install verification. Later: `uninstall`. |
| `PCSteward.Provider.Chocolatey` | `install` builder (machine only, fixed Community URL, exact version), live preflight against the fixed feed, install verification. Later: `uninstall`. |
| `PCSteward.Execution` / `ElevatedHelper` | Helper protocol **v2**: batch manifest, per-item framed results, cancellation between items, caps on items, duration and message size. Still one-shot. |
| `PCSteward.AutomaticUpdateHost` (new) | Stage A: capability-minimal user-scope automatic-update host with no reference to the elevated helper, enforced by an architecture test. Stage B: a separate elevated entry point that reads only the admin-protected policy store. The scan-only `ScheduledHost` stays scan-only. |
| `PCSteward.Persistence.Sqlite` | Additive migrations (§10) |
| `PCSteward.Desktop` | Split the 1,482-line `MainWindow.cs` into page views (Home, Updates, Catalog, Installed, Activity, Settings) and shared review/progress components. Extend the Steward Ledger design system. |

Separation stays as it is: domain rules, provider adapters, process execution, persistence, privileged code and UI remain in separate projects. Architecture tests are extended for the new projects.

## 7. Catalog design

### 7.1 Entry format (JSON, schema-versioned, embedded resource)

| Field | Notes |
|---|---|
| `id` | `pcs:` slug, unique, stable for life |
| `name`, `publisher`, `category`, `summary` | Display only, length-bounded |
| `homepage` | Display only. Never fetched or executed. |
| `providers[]` | `{ provider: winget\|chocolatey, source: winget\|msstore\|chocolatey-community, packageId, scopes[], architectures[], default: bool }` |
| `detection` | `arpDisplayName` (anchored pattern, ≤128 characters, evaluated with `RegexOptions.NonBacktracking` and a timeout), `arpPublisher`, `msixFamilyName`, `wingetIds[]`, `chocolateyIds[]` |
| `processNames[]` | Used only for running-app detection, bounded and validated as file names |
| `flags` | `selfUpdating`, `restartLikely`, `channel` (Stable/Preview), `automationAllowed` (curator kill switch) |

The catalog never contains install switches, URLs used for execution, scripts, paths or command text. Providers own all switches.

### 7.2 Trust and lifecycle

The personal catalog has two layers:

| Layer | Where it lives | How it changes |
|---|---|---|
| **Bundled entries** | `catalog/pcsteward-catalog.v1.json` in this repository, embedded in the owner-signed payload | The owner (or a coding task) edits the JSON and redeploys |
| **My additions** | `user_catalog_entries` table, written only by the reviewed **Add from search** flow | Added or removed in the app. Reversible, and kept by Clear scan data. |

- Both layers go through the same strict validator. A malformed bundled catalog disables catalog features (fail closed). A malformed user row is ignored and reported. The rest of the app keeps working.
- Catalog package IDs are **data, not execution authority**, in either layer. Before approval, each item is resolved live against its provider with an exact ID and version. The helper revalidates it again. This is the same rule that already applies to user-confirmed mappings (`FR-ID-05`).
- Stage B automation (§9.3) never reads the user-writable table. Entries opted into elevated automation are copied by the helper, after UAC, into the admin-protected policy store.
- **Seed:** generate the first bundled entries from the owner's own inventory. Take the apps that WinGet or Chocolatey already match exactly, then add a short wish-list of apps the owner reinstalls on a new PC. Prefer WinGet IDs. Use Chocolatey where WinGet lacks the app or its package is clearly better maintained.

### 7.3 Tooling

- `scripts/Catalog-Validate.ps1`: offline schema, uniqueness, pattern-bound and category checks. Runs in CI.
- `scripts/Catalog-Seed.ps1`: owner-run and read-only. Proposes bundled entries from the current exact WinGet/Chocolatey matches in the local database, for the owner to review before committing.
- `scripts/Catalog-VerifyLive.ps1`: owner-run and read-only. Checks that every ID exists and resolves (`winget show --exact`, `choco search --exact` against the fixed feed).
- Detection precision check: runs the catalog against the owner's inventory and counts unique/ambiguous/none matches. Ambiguous detection falls back to `ReviewRequired`.

### 7.4 Dropped for personal use

A signed catalog download channel is not needed. The catalog changes when the owner redeploys or uses **Add from search**.

## 8. Operation and safety model

### 8.1 Operation kinds

| Kind | Preconditions | Verification |
|---|---|---|
| Install | No matching installed instance. A catalog entry or a reviewed search result. Live preflight returns exactly one package at the exact version, compatible with scope and architecture. Required agreements accepted. | Provider exact list shows the package at the target version, **and** a local re-inventory finds a matching instance |
| Upgrade | As today (exact identity, live, strictly newer, compatible) | As today |
| Uninstall (Phase 10) | User-initiated only. Never automatic. Provider-managed package only. | Provider and inventory show the package absent |

### 8.2 Immutable batch

- Created only by the review-sheet confirmation. It holds ordered items (each is today's selection plus `kind` and the agreement-acceptance hash) and a batch digest.
- Caps: at most 50 items. The batch expires 30 minutes after approval, after which it must be reviewed again. Items cannot be added after approval.
- Run order: user-scope WinGet items run first without elevation. Then all machine-scope items (WinGet machine, all Chocolatey) run in **one** helper session.
- Failure policy: an item failure does not stop the batch unless it is an elevation denial or a helper protocol failure, which stop all remaining machine items.

### 8.3 Provider precedence and ownership

- An installed app keeps the provider that manages it: WinGet-matched ARP entry, or Chocolatey-registered package. The provider is never switched automatically.
- Moving an app to Chocolatey ("reinstall through Chocolatey") is an explicit, labelled Install. It is never offered for automatic updates.
- When both providers report different versions, both are shown. The app is not eligible for automation until the user sets a preferred provider (spec §10.4).

### 8.4 Elevated helper protocol v2

- Request: protocol version 2, nonce, issued time, batch ID and digest, items[] (bounded, same field allowlist as v1 plus `operation`). Messages are framed per item so each one stays under a fixed size.
- The helper validates the caller and session as today, validates the batch digest and every item, rebuilds argv itself for each item, and runs items sequentially. It streams `ItemStarted` / `ItemResult` frames, accepts `CancelRemaining` between items, enforces an overall deadline (sum of per-item timeouts, hard-capped), and exits.
- Hostile tests: substituting, reordering or appending an item; replay; expired batch; oversize frame; a cancel race; caller-identity mismatch.

### 8.5 Running applications

Detection uses the catalog `processNames` plus the instance install path. Default behaviour is unchanged: warn and skip the item. With the per-app or per-run consent, the app is asked to close gracefully (`CloseMainWindow`) and given a bounded wait. Forced termination needs a second, per-run consent and is never used by automation.

## 9. Automatic updates (staged)

### 9.1 Eligibility predicate (pure domain function, exhaustively unit-tested)

An item is eligible only when **all** of these hold:

1. The user enabled automatic updates for this app (off by default). A bulk "enable for all catalog apps" action is allowed but is still an explicit user choice.
2. The mapping is catalog-backed or user-confirmed, with Exact confidence.
3. The channel is Stable, and scope and architecture match the installed instance.
4. Live source evidence (not cached) shows a strictly newer version.
5. The app is not pinned, skipped for this version, or ignored. The catalog `automationAllowed` flag is not false.
6. The same target version has not failed in the last two attempts (backoff).
7. The app is not running. Otherwise the update is deferred, never closed.

### 9.2 Stage A — user-scope automation (recommended first)

- A separate current-user Task Scheduler task runs `PCSteward.AutomaticUpdateHost` after a successful scheduled check, inside an optional maintenance window. It skips when on battery or a metered connection.
- It runs only **user-scope WinGet** eligible items, at most N per run, with the same verification and history. It never elevates.
- Machine-scope eligible items produce one toast: *"3 updates are ready and need administrator approval"*. The toast opens Updates with them pre-ticked → one review → one UAC.
- **Pause all automatic updates** appears in Home and Settings. Factory reset removes the task.

### 9.3 Stage B — unattended machine-scope automation (planned after Stage A)

Stage B is how the PC gets Patch My PC–style "everything updates itself" behaviour, including apps that need admin rights. For a personal PC with a single administrator, this is a reasonable goal. It starts once Stage A has run cleanly for about two weeks of normal use.

The real remaining threat is malware, or any other process running as the owner, using PC Steward to get admin-level installs without a UAC prompt. The design must close that route:

- A Task Scheduler task runs as the owner's account with **highest privileges**, **only while the owner is signed in**, with **no stored password**. It runs, finishes and exits. It is not a service.
- Policy (which apps may update unattended, the per-run cap and the maintenance window) lives in an **admin-only ACL'd store** under `%ProgramData%\PC Steward`. Only the elevated helper writes it, and only after a UAC prompt. The per-user SQLite database, including user catalog additions, is user-writable and is **never** authority for an elevated run.
- The elevated host trusts only the bundled catalog and that store. It re-derives every item from live provider data and applies the same eligibility predicate (§9.1). Chocolatey items need their own per-app opt-in.
- **Pause all** works without elevation by writing a user-side pause marker. The elevated host checks the marker and can only become *more* restrictive because of it.
- Factory reset removes the task and the store through the helper, and reports honestly if that UAC prompt is declined.

## 10. Data model and migration

| Table (new) | Deletion class |
|---|---|
| `app_policies` (auto-update, skip version, preferred provider, close consent, install preference) | Stable decision store. **Kept** by Clear scan data, removed by Factory reset. |
| `user_catalog_entries` (entries added through reviewed **Add from search**) | Stable decision store. Kept by Clear scan data, removed by Factory reset. |
| `catalog_state` (catalog id, version, hash, loaded at) | Scan-derived. Rebuilt on start. |
| `operation_batches`, `operation_batch_items` | Job history class. Kept by Clear scan data, clearable with history, removed by Factory reset. |
| `agreement_acceptances` (package, version, agreement hash, accepted at) | Job history class |
| `automation_runs` | Job history class |
| Stage B policy store (`%ProgramData%\PC Steward`, outside SQLite) | Removed by Factory reset through the helper (UAC) |

Migrations are additive. Existing `approved_update_jobs` and history rows stay readable as `Upgrade` operations. The factory-reset manifest and post-reset verification are extended to the new tables, both scheduled tasks and the Stage B store. Migration tests start from the current schema version.

## 11. Delivery plan

Every task still follows `AGENTS.md` for engineering: smallest coherent change, tests in the same task, narrow tests then the full build and suite before commit, and `STATUS.md` and `TRACEABILITY.md` updated. For personal use, each phase ends with **one owner review plus live use on the protected install** (C-12). There are no separate evidence-readiness or audit tasks.

### Phase 5 — Re-baseline (documents only)

| Task | Scope | Output |
|---|---|---|
| 44 | Owner decisions (§14). Spec v0.3 for C-1…C-12. Revise `AGENTS.md`. ADR-023 catalog, ADR-017/018 revisions (operation kinds, batch helper v2), ADR-024 provider precedence and Chocolatey setup through WinGet, ADR-025 automation Stages A and B. Threat-model update. | Approved spec delta, ADRs, new requirement IDs (§12) |
| 45 | Spikes (read-only, or on a disposable app): WinGet CLI versus COM for install, preflight, agreements and progress; Chocolatey `install`/`search`/`info` with `--limit-output`; helper v2 prototype; catalog seed and detection precision on the owner inventory; Patch My PC reference re-check | `spikes/patch-style/SPIKE_REPORT.md` |

### Phase 6 — Catalog and app state

| Task | Scope | Key tests |
|---|---|---|
| 46 | `PCSteward.Catalog`: schema, loader and validator. `Catalog-Validate.ps1` in CI. Seeded personal catalog from `Catalog-Seed.ps1`, reviewed by the owner. | Hostile catalog fixtures: oversize, duplicate, ReDoS pattern, unknown fields, wrong hash |
| 47 | Catalog detection in matching. `AppState` including NotInstalled. Catalog-backed mapping origin. `app_policies` (skip version, preferred provider, auto-update, close consent). `user_catalog_entries`. Migrations. Clear/reset coverage. | Detection unique/ambiguous/none. Policies survive Clear scan data. Tampered rows cannot widen authority. |

### Phase 7 — Operation engine

| Task | Scope | Key tests |
|---|---|---|
| 48 | `PackageOperation` and `OperationBatch` domain and persistence. WinGet and Chocolatey install builders, live preflight, agreement capture, install verification. | Digest covers every field and the order. Expiry. 50-item cap. Exact argv snapshots. Hostile provider output. No Success without both verifications. |
| 49 | `BatchExecutionCoordinator` and helper protocol v2 (one UAC per batch). Running-app policy. | Cancel between items. Substitution, reorder, append, replay, expiry, oversize and caller-mismatch rejection. Running app is skipped. |
| 50 | Provider health. Chocolatey setup through WinGet as a normal Install. | Absent, untrusted and present client states. No bootstrap-script path exists (architecture test). |

### Phase 8 — Patch-style UX

| Task | Scope |
|---|---|
| 51 | Split `MainWindow` into page views. Six-destination navigation. Home and Settings. Keep protected UI behaviours (`docs/PROTECTED_UI_BEHAVIOURS.md`). |
| 52 | Updates checklist, review sheet, progress panel, completion summary |
| 53 | Catalog page, **Add from search**, per-app options menu |

### Phase 9 — Automation

| Task | Scope |
|---|---|
| 54 | Eligibility predicate, automation settings (window, battery, metered, pause, per-run cap), Stage A host, its scheduled task, toast handoff for machine items |
| 55 | Stage B: admin-protected policy store, elevated scheduled task, elevated host entry point, pause marker, reset coverage. Starts after about two weeks of clean Stage A use. |

### Phase 10 — Optional (each needs the owner's go-ahead)

| Task | Scope |
|---|---|
| 56 | User-initiated uninstall through the provider (`FR-UNI-*`) |
| 57 | Restore point before machine batches. **New PC profile**: export/import the ticked app list to rebuild a new PC. |

### Phase 11 — Hardening and live check

| Task | Scope |
|---|---|
| 58 | Security pass: hostile catalog, tampered DB and user catalog rows, helper v2 abuse, time-of-check/time-of-use, automation host isolation, Stage B store ACLs. Performance smoke test. Factory-reset verification. |
| 59 | Redeploy the protected owner-only install. Owner live check: one WinGet install, one Chocolatey install, a mixed batch with one UAC, a cancel mid-batch, a running-app skip, a Stage A update and a Stage B update (if built). |

**Dropped for personal use:** separate evidence-readiness and audit tasks, the signed catalog channel, distribution-gate reassessment, generic top-300 catalog curation and the accessibility matrix.

## 12. Proposed requirement families (IDs reserved; wording is finalised in Task 44)

| Family | Content | Proposed tier |
|---|---|---|
| `FR-CAT-01–08` | Bundled personal catalog, user additions through reviewed search, strict validation, categories, status per entry, detection rules, fail-closed load, seed and verify tooling | Yes (v0.3) |
| `FR-INS-01–07` | Install from the catalog, live preflight, agreement disclosure and recording, scope/architecture choice, install verification, provider precedence, no install for low-confidence or non-unique results | Yes (v0.3) |
| `FR-BAT-01–08` | Checklist selection, "select all safe", single review sheet, immutable batch, one UAC per batch, cancel remaining, retry failed, per-app close consent | Yes (v0.3) |
| `FR-AUT-01–10` | Per-app opt-in, eligibility predicate, Stage A user-scope host, maintenance window/battery/metered rules, pause all, backoff, approval toast for machine items, Stage B admin-protected policy store and elevated task | Stage A Yes; Stage B Yes after Stage A |
| `FR-PRV-01–04` | Provider health, guidance when WinGet is absent, Chocolatey setup through WinGet, preferred provider per app | Yes (v0.3) |
| `FR-POL-06` | Skip this version | Yes (v0.3) |
| `FR-UNI-01–03` | User-initiated provider uninstall and verification | Optional |

## 13. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Chocolatey package scripts run as administrator | Disclosed in every review sheet. Catalog prefers WinGet. Chocolatey only through the fixed feed, the trusted client and exact IDs. Not in Stage A. In Stage B only with a per-app opt-in. |
| Malware running as the owner abuses unattended admin updates (Stage B) | Admin-only policy store written only after UAC. Live re-derivation. Catalog-only. The per-user DB is never authority. The pause marker can only restrict. |
| A wrong catalog detection shows an app as installed or outdated incorrectly | Anchored, bounded patterns. Uniqueness required. Ambiguity → `ReviewRequired`. Precision check whenever the catalog changes. |
| The only test machine is the PC the owner uses every day | Try each new operation type first on a small, low-stakes app. Skip this version and Retry. Optional restore point (Task 57). History shows exactly what changed. |
| One UAC per batch widens the privileged window | Bounded items and duration. Per-item revalidation in the helper. No additions after approval. The helper exits at the end. |
| Automatic updates break an application | Off by default, per-app opt-in, Skip this version, backoff, history, pause all. Stable channel only. |
| WinGet licence/agreement acceptance done silently | Agreements shown in the review sheet and recorded per package/version. Automation runs only for packages whose agreement is already recorded. |

## 14. Decisions needed from the owner

| ID | Question | Recommendation |
|---|---|---|
| Q-004 | Catalog strategy | **A personal catalog** seeded from the owner's own installed apps, plus **Add from search** in the core release |
| Q-005 | Automatic updates: user-scope only (Stage A) or also unattended machine-scope (Stage B)? | **Both, in order.** Stage A first. Stage B after about two weeks of clean Stage A use, with the §9.3 safeguards. |
| Q-006 | Should "Set up Chocolatey" install it through WinGet when it is absent? | **Yes.** It is a normal, reviewed machine-scope Install. The PowerShell bootstrap is never used. |
| Q-007 | May PC Steward reinstall an app through Chocolatey that was installed another way ("adoption")? | **Only as an explicit, labelled Install.** Never automatic, never a silent provider switch. |
| Q-008 | May a catalog Chocolatey ID replace today's manual exact-ID confirmation (C-5)? | **Yes**, with the Chocolatey disclosure shown in the review sheet. Search results are confirmed when they are added. |
| Q-009 | Are uninstall and restore points in scope? | **Optional Phase 10**, after the core overhaul ships |
| Q-010 | Distribution | **Resolved 2026-10-08 (D-006):** personal, owner-only use. No distribution. `FRA-14–16` retired (C-11). |

## 15. Never in scope

Direct vendor or GitHub installer downloads (until `FR-UPD-13` controls exist), PC Steward–hosted installers or repackaging, disabling vendor auto-updaters, shortcut removal, registry or "performance" tweaks, drivers/firmware/Windows Update, Scoop (stays Later), enterprise Intune/ConfigMgr publishing, remote or multi-PC management, AI-made trust or execution decisions.
