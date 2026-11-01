# ADR-030 — Desktop UI and UX overhaul direction

**Status:** Accepted (owner choice, 2026-11-01)  
**Date:** 2026-11-01  
**Decision owners:** Product owner and implementation reviewer  
**Related requirements:** `FR-UI-01–07`, `AC-UX-01–09` (best effort, ADR-021), `FR-UPD-01/03–09`, `FR-DAT-06–15`, `FR-HIS-01–05`, `FR-POL-01–05`, SPEC §17  
**Related task:** Task 73 (Desktop UI and UX overhaul)

## Context

The owner asked for a full UI and UX overhaul once the Task 50 coding was done. A read-only [audit](../review/UI_UX_OVERHAUL_AUDIT.md) found these high-severity problems in the current Desktop:
- navigation dead ends and an orphaned page;
- outcomes flattened to "Update failed";
- spec items computed but not shown (confidence, channel, completion view, Data & reset cards, diagnostic copy);
- contradictory elevation wording;
- a wall-of-text final confirmation;
- focus loss caused by whole-window rebuilds.

[Design research](../review/UI_UX_OVERHAUL_RESEARCH.md) and [five concepts](../review/UI_UX_OVERHAUL_CONCEPTS.md), each with two-screen mockups, were presented. The owner chose the recommendation.

## Decision drivers

- Trust and the safety model must be visible: nothing is actionable without review; outcomes are truthful.
- Familiar Windows 11 conventions, which give the lowest learning cost and the best accessibility.
- Lowest implementation risk; standard WinUI controls only, with no new dependencies.
- Testability: logic lives in portable view models.

## Options considered

The five concepts are in the [concept study](../review/UI_UX_OVERHAUL_CONCEPTS.md): A Calm Native, B Update Queue, C Inspector, D Guided Steward and E Honest Ledger. C and E were not chosen; C's density can later be offered through a Simple/Advanced switch.

## Decision

1. **Base: Concept A, "Calm Native".**
   - The Windows 11 Settings-style shell: a left NavigationView in Auto mode with Home, Updates, Installed apps, Activity and Decisions, plus footer items Settings (with a Data & reset subpage) and About.
   - Mica with a custom title bar, the Segoe UI Variable type ramp, and a 4 px grid.
   - Home is a one-sentence status hero with tiles that open filtered pages.
2. **Updates page: Concept B's grouping.**
   - Ready can be selected, with nothing preselected.
   - Needs review is shown with its reason in words and has **no update control**.
   - Ignored and skipped is collapsed.
   - Version-jump badges (patch, minor, major), and per-row Skip this version, Ignore updates for this app and Pin.
3. **The update job: Concept D's stepper, used only for the job.**
   - **Review** shows the structured, immutable review sheet.
   - **Update** brings up UAC at most once, after confirmation, and shows non-modal progress with per-item states.
   - **Verify** shows the four outcomes: Verified, Restart required, Installed, not verified, and Failed. It also shows the evidence, restart guidance and **Delete downloaded files**.
   - A refused UAC prompt returns quietly to Review with the plan intact.
4. **All foundations in concept-study §2 apply:**
   - Frame and Page navigation with a back stack, and view-model binding;
   - one status vocabulary;
   - Data & reset as separate cards, with Factory reset in a danger zone that requires typing `RESET`;
   - Decisions in one view;
   - freshness in words;
   - one elevation story;
   - plain copy;
   - accessibility (best effort).
5. **SPEC §17.1 reconciliation.** §17.1 lists *recommended* sections. This ADR maps them as follows:
   - **Overview** becomes **Home**.
   - **Applications** becomes **Installed apps**.
   - **Unmanaged** becomes a saved filter inside Installed apps, which is also reachable from a Home tile.
   - **History** becomes **Activity**.
   - **Decisions** and **Settings** (with a distinct **Data & reset** area) are kept.

   No requirement is removed. The Task 35 five-item navigation choice is superseded by this ADR.
6. **Unchanged constraints.**
   - Every protected behaviour in `docs/PROTECTED_UI_BEHAVIOURS.md` and audit §6 still applies.
   - The Chocolatey reviewed route stays non-admitted, with Confirm disabled.
   - There is no "Cancel update" mid-run.
   - Exact data-control labels and the `RESET` confirmation are kept.

## Consequences

- The UI contract architecture tests are updated deliberately, with review: `UiRedesignContractTests`, `UpdateProgressUiContractTests` and `ReferenceBoundaryTests`.
- `MainWindow.cs` is split into pages and view models, which removes the whole-window rebuild.
- Runtime screenshots and a Narrator walkthrough on a real desktop remain owner-host evidence before release.

## Validation

Validation follows the phased plan in [Task 73](../review/TASK_73_UI_UX_OVERHAUL_PLAN.md): portable view-model tests, Windows CI including the Desktop build, and an independent review per phase.

## Implementation notes

- **Task 73 Phase 1 (`173b727..887094d`): navigation host deviation.** Decision item 4 names Frame and Page navigation. Phase 1 instead uses a persistent shell: the NavigationView, title bar and progress band are created once, and pages are built in code inside one persistent `ScrollViewer` host, with a portable back stack (`ShellNavigation`).
  - Why: `Frame.Navigate` needs page types known to the XAML compiler, while these pages are built in code from shared window state, and the alternative could not be validated without a Windows build. The portable back stack gets unit tests.
  - What it gives: the user-facing outcome this ADR requires (no dead ends, Back on every page, focus and scroll kept).
  - What remains: each refresh still rebuilds the current page's content, though no longer the whole window. Finer-grained view-model binding stays a goal for later phases, page by page, where it pays off.
  - Re-check: the owner or reviewer may require true Frame/Page navigation once Windows CI can validate it.
