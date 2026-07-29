# PC Steward Codex implementation brief

**Purpose:** implement the approved UI/UX package as a native Windows 11 WinUI 3 / Windows App SDK desktop application in small, testable increments without weakening product safety or altering unapproved scope  
**Design sources:** the specifications, tokens and assets under `design/`, with `design-tokens.json` authoritative for visual values and `design/native-prototype/` authoritative for interactive design review; `design/prototype/` is a superseded browser-rendered reference only  
**Current repository state at handoff:** Phase 3 security gate passed and is merge-ready; `docs/STATUS.md` says not to begin Task 26 until that merge is separately authorised and complete

## 1. Non-negotiable implementation boundaries

1. Follow `AGENTS.md`, the product specification, threat model, ADRs and numbered phase order.
2. Do not implement until the Phase 3 branch has been merged through a separately authorised repository action and the Phase 4 branch/task is correctly established.
3. The domain/application layers remain the sole authority for confidence, actionability, update jobs, results and deletion scope.
4. UI code must never construct a command, executable path, arbitrary URL, package authority or deletion path.
5. Scanning remains non-elevated and read-only.
6. No update starts without fresh structured evidence, review and confirmation.
7. No deletion starts without explicit category review; factory reset retains its two-stage design and approved Task 27/28 gate.
8. Do not implement future Startup, general device Storage, configuration concerns, “unwanted software” or broad cleanup modules until the product specification, threat model, requirements and tests are amended.
9. Do not introduce an account, telemetry, website/web UI runtime, embedded WebView, web backend, resident tray/service, Figma dependency, font download or third-party icon pack.
10. Preserve `PCSteward` as the technical identity. Keep display name/assets centralised.
11. Keep list/table virtualization and resource budgets measurable.
12. Do not modify `docs/STATUS.md` or traceability to claim a design acceptance criterion from appearance alone; collect real evidence.

## 2. Current implementation assessment

The current desktop is a single procedural `MainWindow.cs` that rebuilds one long `StackPanel`. It already exposes important safe workflows:

- local/WinGet connector consent;
- inventory scanning and cancellation;
- source refresh/freshness;
- search and filters;
- application detail, mappings, pin and ignore;
- reviewed update execution;
- clear scan data;
- clear PC Steward-owned downloaded files;
- decisions;
- placeholder factory reset.

The implementation is functionally rich but not scalable as a multi-page product. The design integration should replace procedural rendering with:

- typed presentation models;
- XAML resource dictionaries;
- a NavigationView shell;
- page/user-control separation;
- virtualized reusable row templates;
- a global operation state;
- adaptive details/dialog patterns.

Do not rewrite application/domain logic merely to make the UI refactor convenient.

## 3. Target desktop structure

Suggested files; adapt names only when the existing project conventions require it.

```text
src/PCSteward.Desktop/
  App.xaml
  App.xaml.cs
  MainWindow.xaml
  MainWindow.xaml.cs
  Brand/
    AppIdentity.cs
    Assets/
  Resources/
    ColorTokens.xaml
    ThemeResources.xaml
    Typography.xaml
    ControlStyles.xaml
    DataTemplates.xaml
  Shell/
    AppShellViewModel.cs
    NavigationItemModel.cs
    GlobalOperationViewModel.cs
  Pages/
    OverviewPage.xaml(.cs)
    ScanPage.xaml(.cs)
    ScanResultsPage.xaml(.cs)
    ApplicationsPage.xaml(.cs)
    ProductDetailPage.xaml(.cs)
    UpdatesPage.xaml(.cs)
    UpdateReviewPage.xaml(.cs)
    ActivityPage.xaml(.cs)
    PrivacyDataPage.xaml(.cs)
    SettingsPage.xaml(.cs)
    AboutSupportPage.xaml(.cs)
  Components/
    PageHeader.xaml(.cs)
    FreshnessLabel.xaml(.cs)
    StatusChip.xaml(.cs)
    ConfidenceBadge.xaml(.cs)
    FindingRow.xaml(.cs)
    EmptyState.xaml(.cs)
    InlineBanner.xaml(.cs)
    DataCategoryCard.xaml(.cs)
    EvidenceComparison.xaml(.cs)
    GlobalOperationBar.xaml(.cs)
  Dialogs/
    ClearScanDataDialog.xaml(.cs)
    ClearDownloadedFilesDialog.xaml(.cs)
    UpdateConfirmationDialog.xaml(.cs)
    FactoryResetScopeDialog.xaml(.cs)
    FactoryResetPhraseDialog.xaml(.cs)
```

Test project additions should remain in existing architecture/unit/integration projects unless a small `PCSteward.Desktop.Tests` project is justified and approved.

## 4. Implementation sequence

### Phase 0 — repository and scope gate

**Objective:** ensure design work starts from the correct merged product state.

**Actions**

- Confirm `docs/STATUS.md` no longer blocks Phase 4.
- Confirm Phase 3 gate commit is reachable from the working branch.
- Record a design-integration task in the existing numbered workflow or explicitly attach the foundation work to Task 26.
- Review working tree and preserve unrelated changes.
- Read all seven design source files and the existing Task 26/27/28/29 prompts.

**Acceptance**

- No source change before the gate is satisfied.
- Scope explicitly lists Core, Phase 4 and Future-gated surfaces.
- Existing test baseline is green.

**Tests**

- Repository build and full suite at the starting commit.

### Phase 1 — visual foundation and identity

**Objective:** create tokens, themes, typography and reusable primitive styles without changing product behaviour.

**Required components**

- light/dark/high-contrast resources;
- central display identity and selected mark;
- button, input, card, chip, focus and typography styles;
- semantic status resources;
- original icon/illustration assets.

**Expected files**

- `App.xaml`;
- `Resources/ColorTokens.xaml`;
- `Resources/ThemeResources.xaml`;
- `Resources/Typography.xaml`;
- `Resources/ControlStyles.xaml`;
- `Brand/Assets/*`;
- narrow resource tests.

**Acceptance criteria**

- No hard-coded new colour/radius/font values outside resources.
- System/Light/Dark theme can resolve every semantic token.
- High contrast uses system colours and no shadow dependency.
- Existing UI still opens and all prior controls remain reachable.
- `AppIdentity.DisplayName` remains the display-name source.

**Visual checks**

- token specimen page or test-only harness at light/dark;
- 100%, 150% and 200% text scale;
- selected app icon at 16, 24, 32, 48 and 256 px.

**Accessibility checks**

- contrast token tests;
- focus visual on every button/input variant;
- icon-only style requires an automation name.

**Tests**

- resource dictionary load;
- missing-token failure;
- architecture boundary remains green;
- Release build and full suite.

### Phase 2 — shell and navigation

**Objective:** replace the long single page with a Windows 11 NavigationView shell while keeping existing workflows wired.

**Required components**

- AppNavigation;
- PageHeader;
- GlobalOperationBar;
- InlineBanner;
- adaptive page host;
- Back/focus state restoration.

**Expected files**

- `MainWindow.xaml(.cs)`;
- `Shell/*`;
- placeholder pages for approved core destinations only;
- navigation tests.

**Acceptance criteria**

- Overview, Scan, Applications, Updates, Activity, Privacy & data, Settings and About & support are navigable.
- Startup and general device Storage do not appear as functional production modules.
- Active scan/update/cleanup remains visible after navigation.
- Back restores page, filters and selected row.
- No old workflow is lost.
- `MainWindow.cs` no longer procedurally builds the entire application.

**Visual checks**

- expanded and compact navigation;
- 900 × 640 minimum;
- 1366 × 820 reference;
- 200% text-scale overflow.

**Accessibility checks**

- one navigation landmark and one page title;
- correct focus after navigation;
- F6 region order;
- actionable badges have full accessible names.

**Tests**

- `NavigationView` destination/page-key selection and native Back-stack behaviour;
- active operation visibility;
- theme switch;
- architecture/build/full suite.

### Phase 3 — onboarding, overview and scan

**Objective:** implement the no-scan, first-launch, configuration, progress, cancellation, completed, no-findings, partial-failure and offline states.

**Required components**

- onboarding stepper;
- RadioCardGroup;
- ScanProgress/PhaseTrack;
- EmptyState;
- FreshnessLabel;
- provider status rows.

**Expected files**

- `Pages/OverviewPage*`;
- `Pages/ScanPage*`;
- onboarding pages/dialogs;
- presentation models in `PCSteward.Application` only where UI-neutral;
- tests for current workflow state projection.

**Acceptance criteria**

- local-only is the recommended first mode;
- online source consent remains explicit and persisted;
- scan does not imply a change or elevation;
- phase, elapsed time and Cancel are always shown while scanning;
- cancellation acknowledgement and operable-state targets remain intact;
- prior completed snapshot remains visible until publish;
- live/cached/local-only/unavailable wording is exact.

**Visual checks**

- every native state fixture represented by `design/native-prototype/` in light and dark themes;
- long provider name;
- no-scan/no-findings illustrations;
- partial provider failure with completed results.

**Accessibility checks**

- keyboard-only first scan;
- bounded progress announcements;
- Cancel focus/activation;
- 200% progress layout.

**Tests**

- view-model state matrix;
- cancellation;
- source disclosure/revocation persistence;
- previous-snapshot preservation;
- narrow integration then full suite.

### Phase 4 — applications, findings, updates and review

**Objective:** implement evidence-rich lists/details and reviewed update journeys.

**Required components**

- virtualized DataTable;
- FilterBar/SearchBox;
- FindingRow;
- StatusChip/ConfidenceBadge;
- adaptive detail pane;
- EvidenceComparison;
- review queue;
- update confirmation and outcome.

**Expected files**

- `Pages/ApplicationsPage*`;
- `Pages/ProductDetailPage*`;
- `Pages/UpdatesPage*`;
- `Pages/UpdateReviewPage*`;
- `Pages/ScanResultsPage*`;
- reusable components and typed presentation models.

**Acceptance criteria**

- filters cover status, publisher, source and scope;
- multiple install instances remain separate;
- source/package/version/scope/architecture/channel/confidence/freshness are visible before action;
- review-required, low-confidence, stale and unavailable evidence has no install action;
- no result selected by default;
- batch review lists every sequential item;
- running-app/UAC/restart expectations are explicit;
- provider completion is not success until verification.

**Visual checks**

- dense 300-row fixture remains smooth;
- long/missing metadata;
- source disagreement;
- mixed batch outcome;
- detail pane and full-page fallback.

**Accessibility checks**

- keyboard table navigation/selection;
- accessible sort/filter state;
- detail focus restore;
- dialog default Cancel;
- status not colour-only.

**Tests**

- zero false-positive actionable fixture;
- source disagreement;
- stale/low confidence action absence;
- reviewed evidence mutation stop;
- batch outcome mapping;
- virtualization/performance smoke;
- Release build/full suite.

### Phase 5 — activity and current data controls

**Objective:** give history and existing clear actions dedicated, comprehensible surfaces.

**Required components**

- Activity timeline/detail;
- DataCategoryCard;
- Deletes/Preserves confirmation pattern;
- partial cleanup result;
- sanitized diagnostic disclosure.

**Expected files**

- `Pages/ActivityPage*`;
- `Pages/PrivacyDataPage*`;
- clear-data dialogs;
- activity/data presentation models.

**Acceptance criteria**

- Scan data and downloaded files remain separate;
- exact preserved categories match the product spec;
- provider caches are explicitly out of scope;
- active work disables destructive controls with reason;
- partial cleanup lists residuals and never renders success;
- export privacy warning remains;
- no factory reset implementation before Task 27/28 approval.

**Visual checks**

- zero and non-zero category sizes;
- locked-file partial cleanup;
- no history;
- long diagnostic details.

**Accessibility checks**

- Deletes/Preserves groups have headings;
- Cancel is default;
- residual list announced accurately;
- copy/export feedback announced.

**Tests**

- existing clear scan/download semantics;
- active-work lockout;
- provider-cache preservation;
- partial result mapping;
- export fidelity and redaction;
- full suite.

### Phase 6 — Task 26 scheduling and settings

**Objective:** implement the next approved Phase 4 capability in the new visual system.

**Required components**

- Schedule settings card;
- cadence radio group;
- task status/freshness;
- notification settings/permission notice;
- Windows notification presentation.

**Acceptance criteria**

- Manual, Daily and Weekly current-user schedule;
- scan-only/no-install task definition;
- no service/tray/background coordinator;
- independent notification toggle;
- attention/repeated-failure notifications only;
- single-instance/overlap protection;
- process exits after scheduled work;
- UI says `Checks only. Scheduled work never installs updates.`

**Visual/accessibility checks**

- schedule unavailable/rejected;
- long locale date/time;
- keyboard and 200% scale;
- notification content has text, not count alone.

**Tests**

- Task 26 fixture, idempotence, overlap, no-install, exit and architecture tests;
- any live schedule only with explicit approval and complete cleanup;
- Release build/full suite.

### Phase 7 — Task 27/28 factory reset and advanced data controls

**Objective:** implement only after the reset design gate accepts the real handle/order mechanism.

**Required components**

- advanced independent category controls;
- separated factory-reset zone;
- reset manifest dialog;
- phrase confirmation;
- pending/partial-reset screens.

**Acceptance criteria**

- exact Task 27-approved mechanism;
- active-work lockout;
- two separate confirmations;
- exact `RESET` phrase;
- destructive button not default/Enter-triggered;
- binaries and installed apps preserved;
- provider caches preserved;
- old state not loaded after reset marker;
- onboarding only after verified complete cleanup;
- residuals block false fresh state.

**Visual/accessibility checks**

- 200% dialog;
- keyboard accident tests;
- partial reset on next launch;
- high contrast danger zone.

**Tests**

- full destructive path safety and hostile reparse fixtures;
- locked files;
- scheduled-task cleanup;
- idempotence;
- next-launch verification;
- uninstall interaction;
- static analysis/full suite.

### Phase 8 — Task 29/30 hardening and packaging

**Objective:** complete accessibility, performance, visual QA and release assets with measured evidence.

**Acceptance criteria**

- all documented performance protocols executed;
- real keyboard and Narrator journeys;
- Accessibility Insights results recorded;
- 100/125/150/175/200% scale matrix;
- authored light/dark plus Windows High Contrast;
- no clipped primary action;
- package icon assets validated at Windows sizes;
- clean startup/close/no-resident-process;
- no release claim without real evidence.

**Tests/evidence**

- Task 29 machine-readable results and review report;
- Task 30 packaging/install/upgrade/uninstall/SBOM evidence;
- final full suite and review gate.

### Phase 9 — future module design gate

Do not implement Startup applications, general device storage analysis, configuration concerns, unnecessary-file cleanup or “software to review” actions merely because they appear in design references or the native prototype.

Before any production work:

1. amend product goals/non-goals;
2. add user journeys and exact data/action semantics;
3. add threat-model entries;
4. define permission and rollback model;
5. define deletion/change allowlists;
6. add performance budgets;
7. add acceptance/hostile fixtures;
8. approve an ADR and numbered phase.

Default is read-only absence from the production nav.

## 5. Visual review checklist for every phase

- Matches `design-tokens.json`; no local visual constants.
- Calm hierarchy; no health score or alarmist copy.
- Light, dark and high contrast.
- 900 × 640 minimum and reference desktop size.
- 100%, 150% and 200% text scale.
- long/missing data.
- focus, hover, pressed, disabled, busy and error states.
- screenshot or manual evidence for all changed primary screens.
- no unapproved future capability.

## 6. Accessibility review checklist for every phase

- page title and logical automation order;
- visible focus;
- keyboard completion;
- icon + text status;
- accessible name/role/value;
- bounded live regions;
- 4.5:1 text and 3:1 essential non-text contrast;
- target size;
- reduced motion;
- focus restoration;
- safe dialog defaults;
- partial failure not announced as success.

## 7. Required implementation stop conditions

Stop and report rather than improvising if:

- Phase 3 has not been merged/authorised;
- a UI request would make stale/low-confidence evidence executable;
- a screen requires a new arbitrary path/command/URL input;
- a proposed cleanup exceeds fixed PC Steward-owned roots;
- factory-reset mechanism has not passed Task 27;
- a future module lacks approved product/security requirements;
- a refactor would couple the UI directly to SQLite/provider/process execution;
- real accessibility/resource evidence cannot be collected but would be claimed.

## 8. Exact next prompt

Use the following only after approving this design package:

```text
Task 26 — begin implementing the approved PC Steward design while preserving the numbered Phase 4 workflow.

First read `AGENTS.md`, `docs/STATUS.md`, `docs/requirements/PHASE_4.md`, `prompts/26_SCHEDULED_CHECKS_AND_WINDOWS_NOTIFICATIONS.md`, the directly relevant ADRs, current desktop source, the design specifications/tokens/assets, and `design/native-prototype/` except generated build output. Treat `design/prototype/` as a superseded browser-rendered visual archive only: do not deploy it, copy its web architecture, or use it as the implementation base.

Precondition: Phase 3 must already be merged through a separately authorised action and the working branch must be the correct Phase 4 branch. If `docs/STATUS.md` still blocks Task 26, make no production changes and report that exact blocker.

Implement Task 26 and Design Phases 1, 2 and 6 only:

1. Add the light/dark/high-contrast token resources, typography, focus, button, card, status and input styles from `design/design-tokens.json`.
2. Add the selected centralised PC Steward identity assets without changing the stable `PCSteward` technical/package/data identity.
3. Refactor the procedural one-page desktop shell into an adaptive WinUI `NavigationView` with Overview, Scan, Applications, Updates, Activity, Privacy & data, Settings, and About & support destinations. Preserve all existing workflows; placeholder destination page keys may navigate to the current functional content until their later design phase. Do not add Startup or general device Storage to production navigation.
4. Implement Task 26 scheduled checks and Windows notifications exactly from the existing requirements: Manual/Daily/Weekly current-user schedules, typed Task Scheduler adapter, scan-only execution, lower priority/conditions where supported, independent notification toggle, attention/repeated-failure notifications only, overlap protection, and no tray/service/resident coordinator.
5. Implement the Settings schedule surface using `SCREEN_INVENTORY.md` PCS-SET-02 and the reusable component rules. It must state: “Checks only. Scheduled work never installs updates.”
6. Keep UI code outside execution/deletion authority. Do not construct commands, paths or package authority in the presentation layer. Do not implement factory reset, advanced data clearing, Startup review, broad storage analysis, configuration changes, generic cleanup, drivers, firmware, Windows Update or arbitrary web sources.

Add focused presentation/navigation/resource tests plus every Task 26 task-definition, idempotence, overlap, no-install and process-exit test required by the repository. Perform light/dark/high-contrast, keyboard, 100/150/200% scaling and minimum-window visual checks for the changed screens. Preserve requirement IDs in tests and evidence. Run the narrowest tests first, then Release build and the full suite. Review the diff for scope, UI-to-domain boundary violations, secrets, unsafe paths, missing focus/automation names and unapproved future scope. Update status and traceability only with measured evidence. Commit only if all required checks pass; do not push or merge.
```
