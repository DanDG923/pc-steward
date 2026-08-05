# Task 35 production UI redesign changelog

## Before

- A broad destination list exposed workflow stages as peer navigation choices.
- Important pages relied on repeated procedural cards with inconsistent hierarchy.
- Inventory rows collapsed name, publisher, version, scope, provider, and status into dense strings.
- Raw/internal status wording reached primary UI surfaces.
- Scan progress could rebuild the full shell on progress events.
- Activity was a thin destination rather than a useful local record.
- Settings mixed controls without a strong source/schedule/decision/data hierarchy.
- Design evidence existed, but the production implementation map and visual validation record were incomplete.

## After: Steward Ledger

- Five stable task destinations: Overview, Applications, Updates, Activity, Settings.
- Contextual Scan, results, review, repository/current, storage, privacy/data, detail, onboarding, and About routes preserve every supported capability.
- Decision-first Overview with one primary action, evidence scope, automatic-change disclosure, and status counts.
- Structured, virtualized application list with search, status, publisher, source, scope, and sort controls.
- Evidence-led Updates page with freshness, actionable/review/unavailable separation, and plain-language administrator expectations at final review.
- Real local Activity ledger for inventory completion, decisions, and confirmed package mappings.
- Grouped Settings for source consent, schedule, reversible decisions, and local data.
- Persistent global operation band with truthful stage text, indeterminate progress, and scan cancellation.
- Shared page headers, buttons, badges, banners, empty states, metric cards, evidence rows, and activity rows.
- Theme-aware selected navigation, semantic light/dark/High Contrast resources, and explicit automation names.

## Existing behaviour preserved

- local inventory remains read-only and non-elevated;
- package-source consent remains separate and persisted;
- filters remain view-only and do not change saved data;
- uncertain/stale source evidence remains non-executable;
- update review, immutable approval, on-demand UAC, sequential execution, and post-update verification remain intact;
- current-version pins, ignores, manual mappings, and their reversal remain intact;
- routine data cleanup, exports, downloaded-artifact cleanup, and factory reset remain separately scoped and confirmed;
- schedule semantics and the no-resident-service boundary remain intact;
- startup/reset recovery and administrator-requirement changes already in the working tree are preserved.

## Main implementation files

- `src/PCSteward.Desktop/MainWindow.cs`
- `src/PCSteward.Desktop/StewardPresentation.cs`
- `src/PCSteward.Desktop/App.xaml`
- `src/PCSteward.Desktop/Resources/ColorTokens.xaml`
- `src/PCSteward.Desktop/Resources/ControlStyles.xaml`
- `src/PCSteward.Desktop/Resources/DataTemplates.xaml`
- `src/PCSteward.Desktop/Resources/Typography.xaml`
- `tests/PCSteward.ArchitectureTests/UiRedesignContractTests.cs`
- `tests/PCSteward.Performance/Program.cs`

## Design and evidence records

- `docs/UI_CURRENT_STATE_AUDIT.md`
- `docs/PROTECTED_UI_BEHAVIOURS.md`
- `docs/COMPETITOR_UI_RESEARCH.md`
- `docs/UI_INSPIRATION_REFERENCE_MAP.md`
- `docs/UI_DESIGN_DIRECTIONS.md`
- `docs/SELECTED_UI_DIRECTION.md`
- `docs/UI_DESIGN_SYSTEM.md`
- `docs/UI_VALIDATION_REPORT.md`
- `docs/review/screenshots/*.png`
- `docs/review/raw/task-35-ui-performance.json`

## Task 38 decision-action correction

- Review-required cards on both Inventory results and Review actions reuse one confirm identity, choose identity, pin, ignore, and details control set instead of only linking to evidence.
- Cached, offline, or unavailable exact matches expose `Refresh source evidence` on both card entry points and the detail page; the refreshed detail stays open and is rebound to the new result.
- A live unique exact result may unlock identity confirmation, while a failed refresh remains retryable and a live incomparable version remains pin/ignore-only.
- A live exact package identity with no repository-supplied available version is `Update evidence unavailable`, not a user decision; it moves to the Unavailable result group, shows the partial-source warning, and offers refresh without map/pin/ignore decision controls.
- Ambiguous source candidates remain typed source/package records and are selected explicitly; display strings never become mapping authority.
- Incomparable versions remain non-executable and offer only reversible policy decisions.
- Confirmation, pin, and ignore actions state that no application changes and no update starts.
- The production build and 271-test normal-host suite pass; post-fix live visual evidence and deployment remain separate work.
