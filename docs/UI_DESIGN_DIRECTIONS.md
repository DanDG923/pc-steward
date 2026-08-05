# UI design directions

Three production-compatible directions were evaluated on 2026-08-05. Scores use a 10-point scale and the mandated weighting.

## A. Steward Ledger

- **Concept:** a calm native record of installed software, evidence, decisions, and verified outcomes.
- **Navigation:** five stable goals—Overview, Applications, Updates, Activity, Settings—with contextual Scan, Review, Data, and About views.
- **Dashboard:** one decision sentence and primary action, then freshness and restrained counts.
- **Density:** comfortable structured rows; technical evidence expands on demand.
- **Typography/surfaces:** Segoe UI Variable, semantic light/dark/high-contrast resources, bordered 12 px surfaces, minimal shadow.
- **Status:** icon + plain-English label + source/freshness; warning and danger reserved for real attention/failure.
- **Inventory/detail:** virtualized ledger with filters; installed and repository evidence compared side by side.
- **Update review:** explicit immutable evidence summary followed by a separate approval control.
- **Settings:** grouped real controls with consequence copy; destructive data controls are isolated.
- **Strengths:** strongest trust, compatibility, information hierarchy, accessibility, and maintainability.
- **Trade-off:** less visually dramatic than consumer “optimizer” products.

## B. Guided Maintenance

- **Concept:** a novice-first sequence centred on the next safe step.
- **Navigation:** Overview, Guided check, Results, History, Settings.
- **Dashboard:** large primary action and a staged Inspect → Explain → Review → Verify track.
- **Density:** spacious cards and short summaries; details open as drawers.
- **Typography/surfaces:** larger headings, softer containers, more onboarding help.
- **Status:** sentence-based summaries with limited metrics.
- **Inventory/detail:** list is secondary to guided results and decisions.
- **Update review:** wizard-like evidence and approval sequence.
- **Settings:** simple/advanced split.
- **Strengths:** easiest first run and strongest cognitive simplicity.
- **Trade-off:** repeated users and technical users need more navigation; wizard state adds implementation complexity and can obscure the complete inventory.

## C. Evidence Workbench

- **Concept:** a dense split-pane technical console for rapid comparison and triage.
- **Navigation:** Inventory, Evidence, Review queue, Operations, History, Configuration.
- **Dashboard:** dense status table and configurable filters.
- **Density:** compact rows, persistent right evidence pane, extensive keyboard commands.
- **Typography/surfaces:** flatter surfaces, compact 44 px rows, more metadata visible at once.
- **Status:** detailed confidence, source, scope, channel, and architecture columns.
- **Inventory/detail:** master-detail table optimized for power users.
- **Update review:** queue-first workflow with multi-selection.
- **Settings:** source and policy controls exposed prominently.
- **Strengths:** fastest expert triage and strongest simultaneous evidence comparison.
- **Trade-off:** resembles an enterprise console, raises accessibility and small-window risk, and is least compatible with the current single-item reviewed-update workflow.

## Weighted decision

| Criterion | Weight | Steward Ledger | Guided Maintenance | Evidence Workbench |
|---|---:|---:|---:|---:|
| Trust and transparency | 25% | 9.7 | 9.1 | 9.4 |
| Usability and clarity | 20% | 9.4 | 9.6 | 8.0 |
| Current-app compatibility | 15% | 9.6 | 8.5 | 8.1 |
| Accessibility | 15% | 9.4 | 9.5 | 8.1 |
| Performance | 10% | 9.3 | 8.8 | 8.2 |
| Maintainability | 10% | 9.3 | 8.4 | 7.8 |
| Distinctiveness and polish | 5% | 8.8 | 8.9 | 9.0 |
| **Weighted total** | **100%** | **9.47** | **9.07** | **8.43** |

**Selected:** Steward Ledger. It best expresses PC Steward's evidence-before-action identity while fitting the current WinUI architecture and real workflow boundaries.
