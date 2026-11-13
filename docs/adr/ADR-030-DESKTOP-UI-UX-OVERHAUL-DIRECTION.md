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
- **Task 73 Phases 2–5: recorded deviations.** These are listed in the [plan](../review/TASK_73_UI_UX_OVERHAUL_PLAN.md#recorded-deviations):
  - one app per job (no update-all);
  - no Skip this version;
  - a read-only activity reader;
  - Home "Check for updates" chaining the source refresh;
  - an explicit Save schedule;
  - F5 and Ctrl+F shortcuts.

  None of them changes a safety rule in this ADR.
- **UI polish: approved targets (owner sign-off 2026-11-11).** After Task 73 shipped, the owner found the running app did not match the concept A mockups the owner had approved. Three causes: the July 2026 navy palette was kept instead of Fluent defaults, every page was a centred column with a large empty band beside the navigation, and Home was built as counter tiles. The owner then approved dark, real-state target mockups ("T-*" boards on the concepts canvas: Home, Home with updates ready, Updates, Updates ready, Review, Verify, Installed apps, Activity, Decisions, Settings and About) as they stand, with the standard Windows 11 (Fluent) look. They are the acceptance reference for the polish phases. The sign-off also covers three small product changes shown in them: "Unmanaged" reads "No update source", duplicate records of one app show once, and Home's primary action when local-only is "Turn on update checks".
- **UI polish Phase 1: foundations (layout and colour).** No behaviour or safety rule changes.
  - Layout: every page is one left-aligned column beside the navigation, at most 1,000 epx of content, with 28/12/40/40 padding and 20 epx between sections. The host fits the column to its width, so it is never centred.
  - Colour: the semantic `Steward*` brushes are aliases of Windows App SDK theme brushes (card layer, card stroke, text fills, accent text, focus strokes) over Mica, so the app follows the Windows theme and accent colour. Caption text uses the secondary text fill, because the tertiary fill is below 4.5:1 on light cards. Only the status tones (success, warning, danger, information) keep custom colours, based on the Windows 11 system fills and checked at 4.5:1 against their surface and the base and card layers in light and dark. High contrast stays on system colours. `design/design-tokens.json` no longer drives the runtime palette.
  - Navigation: the pane and content are transparent over Mica, and NavigationView keeps its standard selected-item style.
  - Type: the Fluent ramp. Page descriptions are Body text in the secondary colour under the title, section headings are Body Strong, and captions are for metadata.
  - Controls: the Windows App SDK defaults (32 epx buttons and inputs). Shared pieces for the later phases: a Settings-style row card, a navigation row (one button), an icon well, the standard `InfoBar` for page messages, and pill status chips.
  - Code: `MainWindow.cs` is split into one partial file per page (`MainWindow.<Area>.cs`) with no behaviour change. This is the first part of the split this ADR's consequences call for; pages are still built in code.
  - Not claimed: the runtime look. It is checked against the targets on the owner's PC in polish phase 5.
- **UI polish Phase 2: Home.** No safety rule changes.
  - Home is one status card (round icon, one sentence, facts with icons, actions on the right, under the sentence when narrow), then "At a glance" rows in one or two columns, then a footer line saying scanning and checking never ask for administrator permission.
  - The primary action follows the state: "Turn on update checks…" when local-only (the existing consent dialog; turning on starts nothing), "Review updates" when updates are ready (it only opens Updates), otherwise "Check for updates".
  - Failed or unverified updates and pending restarts are info bars at the top that open Activity filtered to them, instead of a tile and a separate attention card. Local-only, an owner's choice, is a neutral fact, never a warning.
  - The status `Unmanaged` is shown as "No update source"; its stored key is unchanged. The Unmanaged tile is merged into the Installed apps row.
  - The visible caption under the old buttons ("Scans this PC, then checks your enabled update sources") is now the button's tooltip and help text; the scan results link moved beside the scan time.
- **UI polish Phase 3: Updates, Review and Verify.** No safety rule changes.
  - Updates: "Check for updates" (the same read-only scan and check as Home) replaces "Refresh package sources" in the header; the description counts rows and states freshness in words, and an info bar offers "Turn on update checks…" when checks are off. "Review decisions" moved from the header to a link under Needs review.
  - Records of the same app are one Updates row (owner approved, 2026-11-11). They join only when everything shown and acted on matches. Only the row's own record can be selected or reviewed; ignore, pin, skip and reversal are recorded for every record of the row inside one confirmation and one data operation.
  - Ready is one list card under a selection bar with one primary button at a time ("Review N updates" for the chosen apps, otherwise "Review all N updates"). Needs review rows give the versions and reason in words and a "Review match" button that only opens details.
  - Review: the stepper comes before the title. "What will change" shows every protected fact on the page (package ID, architecture, channel, source, provider, versions, publisher, scope, confidence); the full fact list and evidence are in an expander. Risks are "Before you update" rows. The confirm bar keeps Cancel first and focused and the shield only when administrator approval is required.
  - Verify: outcome rows in one list card per outcome group, restart guidance, a "Tidy up" row with the unchanged "Delete downloaded files" button, "View in Activity" and "Done".
- **UI polish Phase 4: Installed apps, Activity, Decisions, Settings, Data & reset and About.** No behaviour changes.
  - Installed apps: filter tabs, one toolbar, column headings and one list card. A neutral status such as "No update source" is plain text with one calm footnote, not a chip on every row. Every inventory record keeps its own row (owner decision, 2026-11-11); only Updates merges same-app records.
  - Activity: one expandable row per record, with the outcome in words in its heading and the facts and sanitized details inside.
  - Decisions: grouped list cards with unchanged reversal; the empty page explains each kind of decision.
  - Settings: Windows Settings-style sections (Update checks, Scan scope, Schedule, Privacy & data). Decisions and About are reached from the navigation pane only; Data & reset opens from Privacy & data. Copy diagnostic moved to About (same sanitized bundle).
  - Page headers have no eyebrow, so descriptions line up with their titles.
- **UI review 2026-11-13 (owner approval of all 21 items, 2026-11-13).** The owner asked for every screen to be designed around someone keeping their apps up to date: is anything out of date, is it really my app, can I update in a click or two, did it work, and if it can't update, why. Built in five parts; each is recorded in [STATUS](../STATUS.md).
  - Part 1: four layout and naming faults. No behaviour or safety rule changes, except that Windows-signed MSIX packages are classified System and listed on request.
  - Part 2: wording. The SPEC §7.2 statuses keep their meanings and stored keys; their on-screen names are now "Update ready" (Update available — verified), "Check the match" (Update available — review), "Couldn't check" (Source unavailable), "Up to date" (Current), "No update source" (Unmanaged), "Skipped" and "Not checked yet". "Review" names only the step before an update is installed. The source consent setting is "Look for updates online"; its disclosure text and default (off) are unchanged in substance. Reassurance is stated at the consent dialog, the review step and About rather than on every page.
  - Part 3a: the Updates page (decision 2 refined, not replaced). The three groups become four: Update ready, Check the match, Can't update yet, and Ignored and skipped. A ready row gains its own "Update…" button, which opens the same review for that app alone; nothing is preselected and every route still passes the review step. "Check the match" lists only rows where the owner confirming the match would settle it and something newer is on offer; its "Yes, same app" button opens the existing reversible confirmation. "Can't update yet" gives one reason a line and has no update control. Apps with nothing on offer are not Updates rows; they stay on Installed apps.
  - Part 3b: app details lead with one headline, one sentence and at most one main action, chosen by a portable presenter from the same evidence as the Updates page; other decisions are in a "More" menu and the matcher's recorded reasons are under Technical details. The details page no longer shows an administrator banner before the review: it states the requirement only when it is known, and the review step still states it before confirmation. A saved pin, ignore, skip or match is one line with Undo instead of a second dialog; the first confirmation dialog is unchanged.
  - Part 3c: the review step's reminder and buttons sit in a bar under the scrolling page (decision 3's review step is otherwise unchanged; Cancel keeps the focus). The contextual pages Scan results, Review decisions and Repository apps are retired: Updates and app details carry every match and update decision, Installed apps gains a "Has an update source" saved filter (decision 5 extended) and keeps the confirm-together action for matches already on their listed version, and Settings opens Storage.
  - Part 4: Home may now open the review step (decision 1 refined). It lists the ready updates, each with "Update…", and its main button is "Update all N…"; both call the same review as the Updates page and nothing else, so no update starts without the owner's confirmation there (FR-UPD-01, AC-UPD-01). A scan or update check shows a determinate bar whose time left is an estimate from the last run, worded as one and capped below 100%; the scan reports its step for the first run. The schedule is one choice (Off, Daily, Weekly) and its Save button shows only after a change; the stored record and the register-then-save operation are unchanged. Activity keeps the reviewed-update outcomes (admission obligation 5), every record and line, in one closed row that opens itself when a record needs attention. Row text is body size, filter tabs announce their place, badges wrap and Installed apps headings sort. Left alone on purpose: the Chocolatey client row in Settings (ADR-031 pins its entry points), and the filter tabs stay the app's own wrapping buttons.
  - Part 5a: a two-step first run. Home's empty state is step 1 (scan); straight after the first scan Home shows one question, "Look for updates online?", in place of the status card. Yes goes through the one consent dialog (unchanged, "Keep off" still the default) and, only if it was approved, starts the update check the button names; this is the one place where turning update checks on is followed by a check, because the owner asked for exactly that. "Not now" changes nothing. Decision on width refined: pages of text and switches (Settings and its subpages, About, Decisions, Scan, the bare pages) are a column of about 1,000 units; list pages keep the full window width the owner asked for.
  - Part 5b: on a content area of 1,200 units or more, an app opened from Updates or Installed apps shows in a details column beside the list (a second scroll host in the shell, 420 to 620 units wide) instead of replacing the list. The column is the details page's own content, so decision 3 (review before any update) and the confirmation dialogs are untouched. Below the width, and from Home, details open as a page.
  - Part 5c: an app's tile shows the app's own icon when one can be read under [ADR-035](ADR-035-REAL-APP-ICONS.md) (accepted, option A), and its two letters otherwise. The icon is decoration: names, statuses and every action are unchanged, and the icon is kept out of what a screen reader reads.
