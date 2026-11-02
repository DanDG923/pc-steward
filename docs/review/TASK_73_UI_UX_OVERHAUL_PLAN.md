# Task 73 — Desktop UI and UX overhaul (A + B + D): phased plan

Direction: [ADR-030](../adr/ADR-030-DESKTOP-UI-UX-OVERHAUL-DIRECTION.md), accepted by the owner on 2026-11-01. Evidence: [concepts](UI_UX_OVERHAUL_CONCEPTS.md), [audit](UI_UX_OVERHAUL_AUDIT.md), [research](UI_UX_OVERHAUL_RESEARCH.md).

Scope rules:
- Presentation and view-model work only, with no new dependencies.
- No change to providers, execution, elevation, persistence semantics or the Task 50 admission state.
- Every protected behaviour (audit §6) is preserved.
- Each phase:
  - lands portable view-model tests and deliberate contract-test updates;
  - gets Windows CI green, including the Desktop build;
  - gets a fresh independent review;
  - updates STATUS and traceability.

## Phases

1. **Foundations.**
   - The `Frame`/`Page` shell (Home, Updates, Installed apps, Activity, Decisions; footer Settings and About) with a back stack, `ItemInvoked`, and Back on every page.
   - View-model binding with incremental updates, replacing whole-window rebuilds.
   - The status-chip component and the single status vocabulary.
   - Mica, a custom title bar and type-ramp tokens.
   - One consistent elevation story in all copy.
   - Requirements: `FR-UI-01/02/05`, `AC-UX-01–04`.
2. **Updates and the job (B + D).**
   - Updates grouped into Ready, Needs review and Ignored/skipped. Nothing is preselected, and needs-review items have no update control.
   - Version-jump badges, and the Skip, Ignore and Pin overflow actions.
   - Then the job stepper:
     - **Review:** the structured sheet with facts, confidence in words, risk callouts and a specific commit button with the shield only when needed;
     - **Update:** non-modal progress with per-item states; a refused UAC prompt returns quietly to Review;
     - **Verify:** the four honest outcomes, evidence, restart guidance and Delete downloaded files.
   - Requirements: `FR-UPD-01/03–09`, `FR-UI-03/04`, `FR-DAT-09`, UJ-04.
3. **Home, Installed apps, Activity and Decisions.**
   - The Home status hero and tiles, each opening a filtered page.
   - Installed apps with saved filters, including Unmanaged.
   - Activity as a filterable history, with reviewed-outcome fields and the per-product timeline.
   - Decisions as one reversible view.
   - Requirements: `FR-UI-01/06`, `FR-HIS-01–03`, `FR-POL-01–05`.
4. **Settings and Data & reset.**
   - Settings uses SettingsCard groups with a single save model.
   - Data & reset is five cards (size, last cleaned, what is kept) plus a Factory reset danger zone: type `RESET`, no Enter activation, locked out during active work.
   - Copy diagnostic (`FR-HIS-05`).
   - Requirements: `FR-DAT-06–15`, `AC-UX-07/08`.
5. **Accessibility and copy pass, then final review.**
   - Heading semantics, live announcements, label-in-name, F6 and access keys, selectable evidence, text scaling and contrast fixes (best effort under ADR-021).
   - A copy sweep against the vocabulary.
   - A fresh integrated review.
   - Owner-host runtime screenshots and a Narrator walkthrough as release evidence.

## Branching

Implementation goes on its own branch and PR, separate from the Task 50 PR (DanDG923/pc-steward#12). The branch name and base need owner approval; the proposal is to stack on the Task 50 branch so Task 50's Desktop surfaces are included.

## Recorded deviations

- **Phase 1:** the navigation host is a persistent `ScrollViewer` with a portable back stack instead of `Frame`/`Page` (see ADR-030 implementation notes).
- **Phase 2:** omitted "Skip this version", because no domain operation exists for it. Update-all is not offered, because the existing workflow reviews one app per job, so the commit button reads "Update 1 app".
- **Phase 3:** a read-only SQLite activity reader was added, because no query existed across all products. Home "Check for updates" chains the existing source refresh after a completed scan when sources are already enabled. The plan said scan only. Both were accepted by independent review.
- **Phase 4:** the schedule keeps an explicit **Save schedule** (all other settings apply immediately), because it registers a Windows scheduled task and WinUI spurious-selection events could not be validated without a Windows runtime. A failed settings save after a successful registration re-registers the previous schedule. Schedule saving counts as active work, so other actions wait for it.
- **Phase 5:** two shortcuts beyond the plan (F6 and access keys), borrowed from concept E:
  - **Ctrl+F** focuses search or the filters;
  - **F5** runs the same "Check for updates" (or "Scan again") action as the Home button, which includes the existing source refresh when sources are already enabled.

  Both are explicit user key presses. F5 is blocked by `ShortcutGuard` during active work, an open review, an open dialog or before the shell is shown. No shortcut or access key can reach confirmation or execution, and the confirm button has none. Accepted by the final integrated review, provided it is recorded here.

## Follow-ups after Task 73

- Split `MainWindow.cs` (≈2,800 lines after the overhaul) into partial classes per page or area, with no behaviour change, and update the source-scanning contract tests to follow the files.
- Possible product additions that need domain work, for owner decision: "Skip this version", update-all for ready apps, and an Activity search box.
- The shared diagnostic sanitizer does not redact Windows paths containing spaces outside the profile folder.
