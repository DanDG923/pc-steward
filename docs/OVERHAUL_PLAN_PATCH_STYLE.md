# PC Steward overhaul plan — Patch My PC–style updater on WinGet and Chocolatey

**Task:** 43 — Patch-style overhaul plan
**Status:** **Proposed — not approved.** This plan changes no requirement, ADR, safety boundary or code. Section 4 lists the spec, `AGENTS.md` and ADR changes that the product owner must approve (Task 44) before any implementation task starts.
**Date:** 2026-10-08
**Baseline:** commit `52b4313` (Task 42), product specification v0.2, ADR-001–022, 290/290 automated tests.

---

## 1. Goal

PC Steward today puts evidence first. It produces a careful inventory and applies reviewed updates one at a time. The overhaul turns it into a **home "patch-everything" updater**, similar in use to Patch My PC Home Updater:

- one list of known applications, colour-coded **Outdated / Up to date / Not installed**;
- tick boxes and **one button** to silently install or update everything selected;
- **one administrator prompt** per run instead of one per application;
- a curated **catalog** for installing new applications, not only updating installed ones;
- optional **scheduled automatic updates** for applications the user opts in;
- **WinGet and Chocolatey** as the only execution engines.

The overhaul keeps the properties that make PC Steward trustworthy. Package managers stay the only source of installers. Commands are built by the provider and never read from data. The user approves an immutable plan. Success is reported only after verification.

## 2. Target behaviours compared with today

The left column describes Patch My PC Home Updater behaviour as it is commonly described. Task 45 re-checks it against the vendor's current documentation before the UX is fixed.

| Behaviour | Patch My PC Home Updater (reference) | PC Steward today | Overhaul target |
|---|---|---|---|
| Curated list of known apps, including uninstalled ones | Yes, several hundred apps | No. Only installed software is shown. | **Curated catalog** (§7) plus optional repository search |
| Colour-coded status | Red outdated, green current, neutral not installed | Evidence-led statuses (Verified, Review required, Unmanaged…) | Same three primary states. Evidence stays one click away. |
| Tick boxes and one "Perform" button | Yes | Batch review exists but is evidence-heavy | **Select → one review sheet → one confirmation** |
| Silent install/update | Vendor installers with silent switches | WinGet/Chocolatey `upgrade` only | WinGet/Chocolatey `install` **and** `upgrade` |
| Administrator prompts | The app runs elevated | One helper launch per machine-scope job | **One UAC prompt per approved batch** (§8.4) |
| Scheduled automatic updates | Yes | Scheduled *check* only (`FR-SCH-03`) | **Opt-in per app**, introduced in stages (§9) |
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
- Elevation goes through the signed, one-shot, identity-checked helper. There is no resident elevated service or tray process (ADR-018). Stage B automation (§9.3) is the only proposed exception, and it is gated separately.
- An item is Success only after provider and inventory verification, or a reliable restart-pending result (`FR-UPD-08/09`).
- Low-confidence, incomparable, architecture-conflicting or channel-conflicting candidates are not actionable (`FR-ID-04`, `FR-VER-04`).
- Scans never elevate. `Win32_Product` is never used. Drivers, firmware, Windows Update, registry cleaning and "optimisation" stay out of scope.
- The product stays local-first: no account, no telemetry, no inventory upload.
- Clear scan data, Clear downloaded files and Factory reset keep their separate semantics. New state is added to the correct deletion class (§10).
- The distribution gate (`FRA-14–16`) remains blocking. The overhaul replaces the evidence baseline. It does not close the gate.

## 4. Changes requiring owner approval (Task 44)

| # | Current rule | Proposed rule | Affected IDs / documents |
|---|---|---|---|
| C-1 | Updates apply only to installed, matched apps | Add **Install** of catalog apps and reviewed repository-search results | New `FR-CAT-*`, `FR-INS-*`; spec §1, §4.2, UJ-04/05 |
| C-2 | Non-goal "silently install updates"; principle 8; `FR-UPD-12` Later; `FR-SCH-03`; `AGENTS.md` "no unattended installation" | Allow **opt-in, per-app automatic updates** that meet a deterministic eligibility predicate. Default off. | `FR-UPD-12`, `FR-SCH-03`, new `FR-AUT-*`; `AGENTS.md`; ADR-020 |
| C-3 | One elevated helper launch per approved machine-scope job | One helper launch per **approved immutable batch**. Items run sequentially, with per-item revalidation and hard caps. | `FR-UPD-04/10/11`; ADR-018 revision |
| C-4 | Immutable job means one `UpgradePackage` selection | Immutable **operation** with a kind (`Install`, `Upgrade`, later `Uninstall`), grouped in a digest-protected **batch** | ADR-017 revision; helper protocol v2 |
| C-5 | Chocolatey candidates stay review-only until the user maps the exact Community ID | A **catalog-curated** Chocolatey ID, shown with its source disclosure in the batch review sheet, counts as that confirmation. Non-catalog IDs still need explicit mapping. | Spec §11.1 Chocolatey bullets; `FR-ID-05/06` |
| C-6 | `AGENTS.md`: "WinGet is the only actionable update provider" | Bring this in line with Task 32 (WinGet and fixed Chocolatey Community) | `AGENTS.md`, `FR-SRC-07` |
| C-7 | `FR-INV-11` Chocolatey inventory marked Later | Mark as delivered (Task 32) | Spec §9.2 |
| C-8 | `FR-UPD-06` never force-closes an app | Unchanged by default. Add an optional **per-app, user-granted** "close gracefully before updating" consent. Forced termination still needs a second, per-run consent. | `FR-UPD-05/06`, new `FR-BAT-*` |
| C-9 | Non-goal "uninstall applications automatically" | Keep. Add an **Optional** user-initiated, provider-based uninstall in Phase 10. | New `FR-UNI-*` (Optional) |
| C-10 | No PC Steward–owned catalog | Add a **signed, app-bundled catalog** as a new trusted data artefact. It never contains commands, URLs used for execution or installer switches. | New ADR-023; threat model |

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
- **Search WinGet / Chocolatey** (Phase 10, optional) shows results labelled *Not in curated catalog*. Each one needs its own review and is never eligible for automatic updates.

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
| `PCSteward.AutomaticUpdateHost` (new, Stage A) | Capability-minimal user-scope automatic-update host. It has no reference to the elevated helper, enforced by an architecture test. The scan-only `ScheduledHost` stays scan-only. |
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

- The catalog is bundled inside the signed application payload and listed in `MANIFEST.sha256`. It is loaded and fully revalidated on every start. A malformed catalog disables catalog features (fail closed). The rest of the app keeps working.
- Catalog package IDs are **curated data, not execution authority**. Before approval, each item is resolved live against its provider with an exact ID and version. The helper revalidates it again.
- The database stores only the catalog `id`, version and hash. It never stores a copy that later becomes authority.
- Starting size: about 100 high-demand apps, growing to about 300. Prefer WinGet IDs. Add Chocolatey where WinGet lacks the app or the Chocolatey package is clearly better maintained.

### 7.3 Tooling

- `scripts/Catalog-Validate.ps1`: offline schema, uniqueness, pattern-bound and category checks. Runs in CI.
- `scripts/Catalog-VerifyLive.ps1`: owner-run, read-only check that every ID exists and resolves (`winget show --exact`, `choco search --exact` against the fixed feed). Produces a dated report under `docs/review/`.
- Detection precision report: runs the catalog against the owner's sanitised inventory export (658 apps at Task 35). It counts unique/ambiguous/none matches. Ambiguous detection falls back to `ReviewRequired`.

### 7.4 Later (optional)

Signed catalog updates downloaded from this repository's GitHub Releases, with a detached signature checked against a pinned key, a monotonic version and rollback refusal. Until then, the catalog updates with the app.

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

### 9.3 Stage B — unattended machine-scope automation (gated; not recommended yet)

This would need a highest-privilege scheduled task, which is ambient privileged execution. If the owner wants it after Stage A evidence, it needs its own ADR and threat-model update. Minimum conditions:

- Policy lives in an admin-only ACL'd store under `%ProgramData%\PC Steward`, written only by the elevated helper after UAC. The per-user SQLite database is user-writable and must **never** be authority for an elevated run.
- The elevated host trusts only the signed catalog and that store, re-derives every item from live provider data, and is limited to catalog apps flagged `automationAllowed`.
- Removal needs elevation, so factory reset must report it and route it through the helper.

## 10. Data model and migration

| Table (new) | Deletion class |
|---|---|
| `app_policies` (auto-update, skip version, preferred provider, close consent, install preference) | Stable decision store. **Kept** by Clear scan data, removed by Factory reset. |
| `catalog_state` (catalog id, version, hash, loaded at) | Scan-derived. Rebuilt on start. |
| `operation_batches`, `operation_batch_items` | Job history class. Kept by Clear scan data, clearable with history, removed by Factory reset. |
| `agreement_acceptances` (package, version, agreement hash, accepted at) | Job history class |
| `automation_runs` | Job history class |

Migrations are additive. Existing `approved_update_jobs` and history rows stay readable as `Upgrade` operations. The factory-reset manifest and post-reset verification are extended to the new tables and the Stage A scheduled task. Migration tests start from the current schema version.

## 11. Delivery plan

Each numbered task follows `AGENTS.md`: smallest coherent change, tests in the same task, narrow tests then full build and suite, `STATUS.md` and `TRACEABILITY.md` updated. Phase gates stay as they are: evidence readiness, then review, then merge.

### Phase 5 — Re-baseline (documents only)

| Task | Scope | Output |
|---|---|---|
| 44 | Owner decisions (§14). Spec v0.3 for C-1…C-10. Revise `AGENTS.md`. Decision-log entries. | Approved spec delta, new requirement IDs (§12) |
| 45 | ADR-023 catalog; ADR-024 install operation; ADR-017/018 revisions (operation kinds, batch helper v2); ADR-025 provider precedence and Chocolatey setup through WinGet; ADR-026 automatic updates Stage A; threat model and data-flow updates; Patch My PC reference re-check | Accepted ADRs, updated `THREAT_MODEL.md` |
| 46 | Spikes (read-only or disposable VM): WinGet CLI versus COM for install, preflight, agreements and progress; Chocolatey `install`/`search`/`info` with `--limit-output`; helper v2 prototype; catalog detection precision on the owner inventory | `spikes/patch-style/SPIKE_REPORT.md` |

### Phase 6 — Catalog and app-state model

| Task | Scope | Key tests |
|---|---|---|
| 47 | `PCSteward.Catalog`, schema, loader/validator, first ~100 entries, `Catalog-Validate.ps1` in CI | Hostile catalog fixtures (oversize, duplicate, ReDoS pattern, unknown fields, wrong hash) |
| 48 | Catalog detection in matching. `AppState` including NotInstalled. Catalog-backed mapping origin. | Detection unique/ambiguous/none. No low-confidence mapping becomes actionable. |
| 49 | `app_policies` plus Skip this version, preferred provider, auto-update flag, close consent. Migrations. Clear/reset coverage. | Policy survives Clear scan data. Reset removes it. A tampered row cannot widen authority. |

### Phase 7 — Operation engine

| Task | Scope | Key tests |
|---|---|---|
| 50 | `PackageOperation` and `OperationBatch` domain, digests, persistence, legacy job compatibility | Digest covers every field and the order. Expiry. 50-item cap. |
| 51 | WinGet and Chocolatey install builders, live preflight, agreement capture, install verification | Exact argv snapshots. Malformed and hostile provider output. Success never claimed without both verifications. |
| 52 | `BatchExecutionCoordinator`: ordering, cancellation, running-app policy, retry failed | Cancel between items. Running app is skipped. Interrupted runs recover to VerificationRequired. |
| 53 | Helper protocol v2 with one UAC per batch | Substitution, reorder, append, replay, expiry, oversize and caller-mismatch rejection |
| 54 | Provider health. Chocolatey setup through WinGet as a normal Install operation. | Absent, untrusted and present client states. No bootstrap script path exists (architecture test). |

### Phase 8 — Patch-style UX

| Task | Scope |
|---|---|
| 55 | Split `MainWindow` into page views. New six-destination navigation. Keep protected UI behaviours (`docs/PROTECTED_UI_BEHAVIOURS.md`). |
| 56 | Updates checklist, review sheet, progress panel, completion summary |
| 57 | Catalog page (categories, search within the catalog, install options) and per-app options menu |
| 58 | Home and Settings (providers, automation placeholders). Design-system and screenshot evidence. |

### Phase 9 — Automation

| Task | Scope |
|---|---|
| 59 | `AutomationEligibility` predicate, settings (window, battery, metered, pause, per-run cap), automation history |
| 60 | Stage A: `AutomaticUpdateHost`, its scheduled task, toast handoff for machine items, reset coverage |
| 61 | Stage B **decision only** (ADR and threat model). Implemented only after separate owner approval. |

### Phase 10 — Optional extensions (each needs separate approval)

| Task | Scope |
|---|---|
| 62 | User-initiated uninstall through the provider (`FR-UNI-*`) |
| 63 | Repository search beyond the catalog (WinGet and the fixed Chocolatey feed only) |
| 64 | Signed catalog update channel |
| 65 | Restore point before machine batches. App-selection profiles (export/import a tick list for setting up a new PC). |

### Phase 11 — Hardening and evidence

| Task | Scope |
|---|---|
| 66 | Security review (hostile catalog, tampered DB, helper v2 abuse, time-of-check/time-of-use, automation host isolation). Performance (300-entry catalog render, batch overhead, idle budget). Factory-reset verification. |
| 67 | Owner-approved live evidence on the protected owner-only install: one WinGet Install, one Chocolatey Install, one mixed batch with a single UAC, one Stage A automatic update, a cancel mid-batch, and a running-app skip |
| 68 | Phase review and audit. The distribution gate (`FRA-14–16`) is reassessed, never assumed. |

## 12. Proposed requirement families (IDs reserved; wording is finalised in Task 44)

| Family | Content | Proposed tier |
|---|---|---|
| `FR-CAT-01–08` | Bundled signed catalog, strict validation, categories, catalog status per entry, detection rules, fail-closed load, curation tooling, catalog version shown | Yes (v0.3) |
| `FR-INS-01–07` | Install from catalog, live preflight, agreement disclosure and recording, scope/architecture choice, install verification, provider precedence, no install for low-confidence or non-unique results | Yes (v0.3) |
| `FR-BAT-01–08` | Checklist selection, "select all safe", single review sheet, immutable batch, one UAC per batch, cancel remaining, retry failed, per-app close consent | Yes (v0.3) |
| `FR-AUT-01–08` | Per-app opt-in, eligibility predicate, Stage A user-scope host, maintenance window/battery/metered rules, pause all, backoff, approval toast for machine items, Stage B (separate decision) | Stage A Yes; Stage B Later (separate risk decision) |
| `FR-PRV-01–04` | Provider health, WinGet absent guidance, Chocolatey setup through WinGet, preferred provider per app | Yes (v0.3) |
| `FR-POL-06` | Skip this version | Yes (v0.3) |
| `FR-UNI-01–03` | User-initiated provider uninstall and verification | Optional |
| `FR-CAT-09` | Signed catalog update channel | Later |

## 13. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Chocolatey package scripts run as administrator | Disclosed in every review sheet. Catalog prefers WinGet. Chocolatey only through the fixed feed, the trusted client and exact IDs. Never automated in Stage A. |
| A wrong catalog detection shows an app as installed or outdated incorrectly | Anchored, bounded patterns. Uniqueness required. Ambiguity → `ReviewRequired`. Precision report before shipping each catalog revision. |
| One UAC per batch widens the privileged window | Bounded items and duration. Per-item revalidation in the helper. No additions after approval. The helper exits at the end. |
| Automatic updates break an application | Off by default, per-app opt-in, Skip this version, backoff, history, pause all. Stable channel only. |
| WinGet licence/agreement acceptance done silently | Agreements shown in the review sheet and recorded per package/version. Automation runs only for packages whose agreement is already recorded. |
| Catalog maintenance burden | Small start, offline CI validation, owner-run live verification script |
| Scope creep and a long-lived branch | Phase gates as today. Each phase is mergeable on its own. Phase 10 items need separate approval. |
| The overhaul invalidates existing evidence | Expected. `STATUS.md` records the reset. Phase 11 collects new evidence. |

## 14. Decisions needed from the product owner

| ID | Question | Recommendation |
|---|---|---|
| Q-004 | Catalog strategy: curated bundled list only, full repository search only, or both? | **Both, curated first.** The curated catalog is the default experience. Repository search follows as optional Phase 10. |
| Q-005 | Automatic updates: user-scope only (Stage A) or also unattended machine-scope (Stage B)? | **Stage A first.** Machine-scope updates get a one-click approval toast. Decide Stage B only after Stage A evidence. |
| Q-006 | Should "Set up Chocolatey" install it through WinGet when it is absent? | **Yes.** It is a normal, reviewed machine-scope Install. The PowerShell bootstrap is never used. |
| Q-007 | May PC Steward reinstall an app through Chocolatey that was installed another way ("adoption")? | **Only as an explicit, labelled Install.** Never automatic, never a silent provider switch. |
| Q-008 | May a catalog-curated Chocolatey ID replace today's manual exact-ID confirmation (C-5)? | **Yes**, with the Chocolatey disclosure shown in the review sheet. Non-catalog IDs keep manual confirmation. |
| Q-009 | Are uninstall and restore points in scope? | **Optional Phase 10**, after the core overhaul ships |
| Q-010 | Is distribution still owner-only? | **Unchanged.** The overhaul does not touch the distribution gate. |

## 15. Never in scope

Direct vendor or GitHub installer downloads (until `FR-UPD-13` controls exist), PC Steward–hosted installers or repackaging, disabling vendor auto-updaters, shortcut removal, registry or "performance" tweaks, drivers/firmware/Windows Update, Scoop (stays Later), enterprise Intune/ConfigMgr publishing, remote or multi-PC management, AI-made trust or execution decisions.
