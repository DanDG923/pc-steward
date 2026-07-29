# PC Steward component specification

**Target framework:** WinUI 3 / Windows App SDK  
**Design source:** `design-tokens.json`  
**Interactive reference:** native `design/native-prototype/`; the browser-rendered `design/prototype/` archive is superseded and non-authoritative  
**Rule:** components display application/domain state; they never calculate confidence, authorize execution or derive deletion targets

## 1. Component architecture

Components are organised into four layers:

1. **Primitives:** typography, icon, button, input, divider and surface.
2. **Composites:** status chip, freshness label, finding row, data card and filter bar.
3. **Patterns:** page header, review queue, evidence comparison, empty state and confirmation dialog.
4. **Screens:** composed pages in `SCREEN_INVENTORY.md`.

WinUI implementation should prefer templates/styles and small typed view models over procedural construction in `MainWindow.cs`.

## 2. Navigation

### 2.1 AppNavigation

**Recommended WinUI base:** `NavigationView`

**Anatomy**

- product mark and display name;
- grouped navigation items;
- icon + text label;
- optional actionable-count badge;
- optional `Future` text tag;
- Settings item;
- collapse control.

**States**

- expanded;
- compact;
- selected;
- hover/pressed;
- keyboard focus;
- disabled with explanation;
- high contrast.

**Behaviour**

- the selected `NavigationView` destination/page key persists through view-model refresh and native page reactivation;
- navigation never cancels active work;
- active operation appears in GlobalOperationBar;
- Back returns from details to the prior filter/scroll/selection state;
- collapsed tooltips use the full visible label.

**Accessibility**

- role/name from `NavigationView`;
- badge value included in accessible name only when non-zero;
- future tag read after destination label;
- no duplicate tooltip announcement.

### 2.2 PageHeader

Contains one page title, optional state summary, optional freshness label and command area. At narrow widths commands wrap/overflow while title remains first in reading order.

## 3. Buttons

### 3.1 Variants

| Variant | Use | Example |
|---|---|---|
| Primary | One preferred safe action per region | `Scan this PC` |
| Secondary | Alternative or supporting action | `Choose scan options` |
| Subtle | Low-emphasis contextual action | `View details` |
| Icon | Compact non-destructive utility | Copy, overflow |
| Danger outline | Entry to destructive review | `Factory reset PC Steward` |
| Danger filled | Final destructive confirmation only | Final reset button |

### 3.2 States

Default, hover, pressed, focus, disabled, busy.

- Busy retains width, shows compact progress and changes accessible name.
- Disabled buttons must have adjacent explanation or a teaching tip reachable by keyboard.
- A disabled action is not used simply to advertise an unavailable future feature; use explanatory content instead.
- Only one filled primary button appears in a dialog.

### 3.3 Sizing

- Default: 40 px high, 16 px horizontal padding.
- Compact: 32 px high for command bars only.
- Minimum icon target: 32 × 32 px.
- Text truncation is not allowed for destructive actions.

## 4. Cards and surfaces

### 4.1 InfoCard

**Anatomy:** optional icon; eyebrow; title; summary; body; footer actions.

Variants:

- default;
- selected/actionable;
- success outcome;
- attention;
- unavailable;
- future concept;
- destructive zone.

Cards do not behave as buttons unless the whole card has one clear navigation target. If a card includes multiple actions, the surface itself is not clickable.

### 4.2 SummaryCard

Displays a count, label and interpretation. Counts are never arranged into a score. Screen reader name is `3, Updates ready to review`, not `3`.

### 4.3 EvidenceCard

Displays source, timestamp, stable ID and confidence. Uses definition-list semantics and monospace only for identifiers.

## 5. Tables and lists

### 5.1 DataTable

**Recommended WinUI base:** `ListView` or `ItemsRepeater` with a grid row template; use virtualization.

**Anatomy**

- optional selection checkbox;
- primary cell;
- secondary cells;
- status/freshness cell;
- optional chevron/context menu.

**Behaviour**

- row activation opens detail, never executes a change;
- header sort is single-column by default;
- current sort has visible arrow and accessible direction;
- keyboard Space toggles selection;
- Enter opens detail;
- filters/sort/scroll position survive Back navigation.

**Responsive**

- hide lowest-priority columns into a row details line;
- below 720 px main content, switch to stacked row cards;
- package ID uses middle ellipsis plus copy and accessible full string.

### 5.2 TimelineList

Ordered newest first. Each item has icon + outcome text, timestamp, summary and optional unresolved marker. Technical identifiers remain within the detail view.

### 5.3 EmptyFilteredState

Inline row or panel:

- `No applications match these filters.`
- `Clear filters`.
- Never reuse the product-level no-findings illustration.

## 6. FindingRow

**Purpose:** present an observation with actionability, severity and confidence clearly separated.

**Anatomy**

1. selection checkbox when supported;
2. category/severity icon;
3. title;
4. one-line explanation;
5. scope/value;
6. confidence badge;
7. freshness/source;
8. detail chevron.

**Variants**

- update ready;
- decision required;
- unmanaged;
- app-owned storage opportunity;
- source unavailable;
- future/read-only.

**Rules**

- No selection control for non-actionable findings.
- `Review required` uses amber text/icon, not a red row.
- Low confidence label includes `No action`.
- Future findings include a visible `Future concept` tag.
- Entire row can open detail; checkbox has an independent accessible name.

## 7. Status, severity and confidence

### 7.1 StatusChip

Text + icon + optional subtle fill. Minimum 24 px high. Use the semantic label from domain/presentation state.

Approved labels:

- Current
- Verified update
- Review required
- Unmanaged
- Pinned
- Ignored
- Source unavailable
- Verified success
- Restart required
- Inconclusive
- Failed
- Cancelled
- Local-only
- Cached
- Live

### 7.2 SeverityIndicator

Uses S0–S3 visual mapping from the design system but exposes only user-facing labels. It must not be used as update match confidence.

### 7.3 ConfidenceBadge

Displays the complete confidence label. Clicking/activating opens a teaching tip with the deterministic evidence summary. Low/medium/review states never gain execution authority through the control.

### 7.4 FreshnessLabel

Format:

- `Live · checked 4 min ago`
- `Cached · last checked 2 days ago`
- `Local-only · no source contacted`
- `Unavailable · cached result from 2 days ago`

Tooltip/detail supplies exact timestamp. Colour is secondary to icon and label.

## 8. Progress controls

### 8.1 ScanProgress

**Anatomy**

- overall progress bar when determinate;
- current phase heading;
- elapsed time;
- provider phase list;
- supporting reassurance;
- Cancel;
- Pause/Resume only if supported.

**ARIA/automation**

- accessible name includes phase;
- value announces at meaningful intervals, not every tick;
- phase changes use polite live announcement;
- cancellation request uses assertive announcement only if the current provider cannot stop promptly.

### 8.2 QueueProgress

Displays ordered items with states: queued, running, verifying, complete, failed, skipped, cancelled. Progress is based on item count, not fabricated installer percent.

### 8.3 IndeterminateProgress

Use only when work is active and no measurable progress exists. Always pair with a text description and elapsed time. Stop when paused/cancelled.

## 9. Charts

### 9.1 StorageRing

**Use:** PC Steward-owned data categories.

- direct numeric total in centre;
- adjacent text legend with exact sizes;
- segment click filters the accessible category list;
- list/table is the canonical accessible representation;
- high contrast uses pattern/markers and borders.

### 9.2 DistributionBar

**Use:** application/update status distribution.

- horizontal, labelled segments;
- no single “good/bad” colour continuum;
- zero categories omitted visually but available in summary.

### 9.3 PhaseTrack

**Use:** scan progress. Displays completed, current and pending named phases. Current phase has a ring and text, not motion alone.

## 10. Filters and search

### 10.1 SearchBox

**Recommended WinUI base:** `AutoSuggestBox` or `TextBox` with search icon.

- placeholder names the dataset: `Search applications`.
- clear button is keyboard accessible.
- results update after a short debounce, not on a background system query.
- `Ctrl+F` focuses it.
- accessible result-count announcement is debounced.

### 10.2 FilterBar

Contains status, publisher, source, scope, classification and decision filters as needed.

- shows active filter count;
- supports `Clear all`;
- overflows into a flyout at narrow widths;
- filter chips repeat label and value, such as `Status: Unmanaged`;
- state persists during detail navigation.

### 10.3 Dropdown

Use for bounded enumerations. Never use for final destructive confirmation or long technical identifiers.

## 11. Toggles and settings controls

### 11.1 SettingToggle

**Anatomy:** title, explanation, switch, optional status/last changed.

- simple reversible setting saves immediately;
- busy state prevents repeat input;
- error returns switch to last persisted state and explains why;
- source disclosure happens before enabling, not after.

### 11.2 RadioCardGroup

Use for mutually exclusive scan mode, theme, schedule cadence or density. Each card has title, explanation and optional `Recommended` badge.

### 11.3 Date/Time controls

Use Windows locale and system components. Ignore expiry and schedule controls must expose the chosen timezone/locale semantics.

## 12. Tooltips and teaching tips

### Tooltip

Use for icon-only utilities or truncated values. Keep under two short lines. Tooltip is never the only place for destructive consequence, disabled reason or status.

### TeachingTip

Use for confidence evidence, source disclosure or first-use explanation. It must be dismissible and not block navigation.

## 13. Dialogs

### 13.1 ConfirmationDialog

**Anatomy**

- consequence-oriented title;
- one-paragraph summary;
- optional Deletes/Preserves columns;
- optional warning/permission note;
- primary specific action;
- Cancel.

**Rules**

- Cancel is default and receives initial focus for destructive actions.
- Enter does not activate danger action unless focus is deliberately on it.
- no nested dialogs;
- restore invoking focus.

### 13.2 FactoryResetDialog

Two separate steps:

1. reset manifest review;
2. phrase confirmation requiring exact `RESET`.

The second danger button has no access key and is disabled until the phrase matches. Pasting may be allowed; the protection is deliberate confirmation, not typing difficulty.

### 13.3 PermissionDialog

Use for online source disclosure and reviewed elevation expectation. Never imitate the Windows UAC visual. The app explains when UAC will appear, then Windows owns the actual prompt.

### 13.4 ResultDialog

Use only for short final result when a dedicated page/panel would be excessive. Partial failure uses accurate title and residual list.

## 14. Notifications

### 14.1 InAppToast

Variants: success, information, reversible preference, non-blocking warning.

- 5–7 seconds unless action is present;
- pause dismissal on pointer/focus;
- dismiss button;
- live region polite.

### 14.2 InlineBanner

Variants: offline, cached, partial failure, restart required, safety block.

- icon + heading + body + action;
- persistent while context applies;
- not dismissible if it carries required safety state.

### 14.3 WindowsNotification

Phase 4 only. Shows verified/review counts or repeated failure. It never claims an update was installed and never offers a direct install action.

## 15. Empty states

### 15.1 EmptyState

**Anatomy:** optional original illustration, title, one paragraph, primary action, optional secondary link.

Variants:

- first scan;
- no findings;
- no applications due to cleared data;
- no history;
- no decisions;
- offline local mode.

Rules:

- illustration is decorative when adjacent copy duplicates meaning;
- no confetti or exaggerated success;
- no `Everything is perfect` claim;
- distinguish dataset empty from filter empty.

## 16. Permission notices

### PermissionNotice

Inline notice used before source access or execution:

- operation;
- why permission/network/elevation is needed;
- data shared or system effect;
- duration/scope;
- what happens on decline.

Variants:

- source disclosure;
- UAC expected at execution;
- Windows notifications unavailable;
- future user-selected folder access.

Scan does not use an elevation notice because scanning must remain standard-user.

## 17. Evidence comparison

### UpdateEvidenceComparison

Two aligned definition lists: Installed and Candidate.

Required fields:

- name/package;
- version;
- publisher;
- scope;
- architecture;
- channel;
- source;
- package ID;
- source freshness.

Below the lists:

- confidence badge;
- reason list;
- source disagreement when present;
- actionability explanation.

Missing values display `Unknown`, never an empty cell.

## 18. DataCategoryCard

Used in Privacy & data.

**Anatomy**

- category icon/title;
- size/count;
- last updated/cleaned;
- contains;
- clear/delete action;
- preservation summary;
- busy/partial state.

Variants:

- scan data;
- downloaded files;
- source metadata;
- logs/history;
- decisions;
- settings/consent.

Do not combine categories into one `Clear all` routine action.

## 19. Detail pane

**Recommended WinUI base:** `SplitView` or page-level Grid.

- 360–420 px at large width;
- overlays at narrower widths;
- heading receives focus;
- close button is first in visual top-right but follows heading in reading order;
- Esc closes;
- closing restores row focus and scroll position;
- a full-page fallback uses normal Back navigation.

## 20. Loading

- Retain known cached content while refreshing.
- Show skeleton only after 300 ms to avoid flash.
- Skeleton matches eventual structure and is `aria-hidden`.
- Screen reader receives one `Loading …` status.
- Spinner never appears without a label.
- Tables virtualize; do not show a full-page blocking spinner for incremental detail retrieval.

## 21. Component-to-WinUI mapping

| Design component | Preferred WinUI control |
|---|---|
| AppNavigation | `NavigationView` |
| PageHeader | `Grid` + `TextBlock` + `CommandBar` |
| SearchBox | `AutoSuggestBox` or named `TextBox` |
| FilterBar | `ItemsRepeater`/`StackPanel` + `ComboBox`/`MenuFlyout` |
| DataTable | Virtualized `ListView`/`ItemsRepeater` |
| DetailPane | `SplitView` or adaptive Grid |
| StatusChip | Styled `Border` + `FontIcon` + `TextBlock` |
| Progress | `ProgressBar` + typed phase list |
| Toggle | `ToggleSwitch` |
| RadioCard | `RadioButtons` with custom item template |
| Dialog | `ContentDialog` with custom body |
| Teaching tip | `TeachingTip` |
| Toast | `InfoBar` or custom non-modal presenter |
| Definition list | Two-column adaptive `Grid` |
| Disclosure | `Expander` |
| Empty state | `StackPanel`/Grid with decorative asset |

## 22. View-model contracts

Suggested presentation records:

- `AppShellState`
- `PageHeaderModel`
- `FreshnessPresentation`
- `StatusPresentation`
- `OverviewSummaryModel`
- `ScanConfigurationModel`
- `ScanProgressModel`
- `FindingRowModel`
- `FindingDetailModel`
- `ApplicationRowModel`
- `ProductDetailModel`
- `UpdateReviewModel`
- `UpdateQueueItemModel`
- `ActivityRowModel`
- `DataCategoryModel`
- `ConfirmationModel`

Each record uses already-decided domain/application state. It must not expose command strings, executable paths, arbitrary deletion paths or mutable action authority.

## 23. Testing obligations

Every reusable control needs:

- light/dark/high-contrast snapshot or visual inspection;
- keyboard focus and activation test;
- accessible name/state/value check;
- 100/150/200% text-scale check;
- long-name/localisation stress;
- disabled/busy/error state;
- theme change without restart where supported;
- no semantic reliance on colour alone.

Safety components additionally need tests that:

- low/review confidence never produces an enabled install action;
- stale/cached evidence cannot appear live;
- destructive default is Cancel;
- Reset action stays disabled until exact phrase;
- partial cleanup never renders as complete success.
