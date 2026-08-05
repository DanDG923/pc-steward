# PC Steward production UI design system

**Direction:** Steward Ledger
**Implemented:** 2026-08-05
**Runtime:** WinUI 3 / Windows App SDK
**Design authority:** `design/DESIGN_SYSTEM.md` and `design/design-tokens.json`

This document maps the Quiet Authority design language to the production desktop app. It supplements the design specification with the exact reusable implementation points that now exist in `src/PCSteward.Desktop`.

## Product principles

1. Lead with the observed state and the next safe action.
2. Keep freshness, source, scope, and confidence near conclusions they qualify.
3. Separate scan, review, approval, elevation, execution, and verification.
4. Use colour only with a glyph and plain-language label.
5. Show uncertainty as uncertainty; never turn it into execution authority.
6. Keep raw package IDs and diagnostics secondary but available.

## Semantic resources

`Resources/ColorTokens.xaml` owns light, dark, and High Contrast semantic brushes. Production controls consume only names such as:

- `StewardCanvasBrush`, `StewardSurfaceBrush`, and `StewardSurfaceSubtleBrush`;
- `StewardTextBrush`, `StewardSecondaryTextBrush`, and `StewardTertiaryTextBrush`;
- `StewardBrandBrush`, `StewardBrandSubtleBrush`, and `StewardFocusBrush`;
- `StewardSuccess*`, `StewardWarning*`, and `StewardDanger*` pairs;
- selected `NavigationViewItem` resources, aliased through the same themed brushes.

High Contrast entries use Windows system colours. No essential state relies on shadow, opacity, or authored colour alone.

`Resources/Typography.xaml` owns Display, Page title, Section title, Body large, Body, and Caption styles. `Resources/ThemeResources.xaml` owns shared spacing, corner radii, page padding, card padding, and control padding. `Resources/ControlStyles.xaml` owns cards, normal and primary buttons, inputs, selects, status chips, list items, and list views.

## Shell and navigation

The production shell uses a native `NavigationView` with five stable task destinations:

- Overview
- Applications
- Updates
- Activity
- Settings

About & support remains a footer utility. Scan, results, review, storage, privacy/data, repository/current, and product detail are contextual routes mapped back to their principal destination. The pane is 232 epx expanded, 56 epx compact, enters compact mode at 900 epx, and expands at 1100 epx.

The shell also owns:

- the product identity and local-storage trust badge;
- an operation band that remains visible across navigation;
- truthful indeterminate progress for work without a defensible percentage;
- a scan-only Cancel action;
- a scrollable page viewport without an ornamental outer card.

## Shared production components

`StewardVisuals` in `StewardPresentation.cs` is the shared component factory:

| Component | Purpose |
|---|---|
| `PageHeader` | Eyebrow, page title, summary, status label, and optional page action |
| `PrimaryButton` | One visually dominant safe action with an explicit accessible name |
| `StatusBadge` / `Badge` | Glyph + text + semantic tone; never colour alone |
| `InformationBanner` | Plain-language context, freshness, scope, or attention state |
| `EmptyState` | Specific reason, safe next action, and no decorative dead end |
| `MetricCard` | Count, label, explanation, and status badge without a health score |
| `EvidenceRow` | Stable label/value alignment for review and technical evidence |
| `ActivityRow` | Local timestamp, outcome title, and concise detail |

`ApplicationListItem` is the presentation boundary for production inventory rows. `Resources/DataTemplates.xaml` renders a virtualized structured row with fallback initials, application/publisher, version/source, and status. The accessible summary includes the visible identity and status.

## Status language

The UI maps domain states to these user-facing labels:

- Update ready to review
- Needs your decision
- Up to date
- Inventoried only
- Source unavailable
- Pinned
- Ignored

Status glyph, label, and semantic tone travel together. “Unmanaged,” “unsafe,” “critical,” “fix all,” overall health scores, and invented risk grades are not used.

## Density and large inventories

- The default window is 1280 × 820 epx.
- The application list uses `ListView` item virtualization and a reusable data template.
- Search, status, publisher, source, scope, and sort controls are grouped before the result count.
- Long technical values trim within rows and remain available in product detail.
- Scan progress updates the operation band and scan status directly; it does not rebuild the full shell for every progress event.

## Interaction and safety rules

- Inventory scanning remains read-only and non-elevated.
- Enabling package sources is separate from refresh, download, approval, or execution.
- Update execution remains behind immutable fresh evidence, explicit review, final confirmation, and on-demand UAC where required.
- Routine cleanup and factory reset remain visibly separate.
- Destructive confirmation behaviour and persisted state contracts are unchanged.
- Settings use real persisted source, schedule, decision, and data controls; no visual-only appearance setting was invented.

## Maintenance rules

1. Add a semantic token before adding a literal production colour.
2. Extend `StewardStatus` for any new domain state; do not display raw status identifiers.
3. Reuse the shared components before adding a one-off card or banner.
4. Preserve the five stable destinations; add specialised workflows as contextual routes.
5. Add an architecture contract when changing navigation, virtualized rows, progress update behaviour, theme dictionaries, or evidence documentation.
6. Re-run `scripts/Build.ps1`, the UI architecture tests, the UI-facing application-test slice, and the large-inventory workload after material UI changes.
