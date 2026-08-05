# UI current-state audit

Reviewed 2026-08-05 against the production `PCSteward.Desktop` project, current working-tree changes, product requirements, and the existing design handoff.

## Production architecture

- Native unpackaged WinUI 3 on .NET 10 and Windows App SDK; the UI is not a web wrapper.
- Real services are composed in `App.xaml.cs`: inventory, SQLite persistence, WinGet and Chocolatey evidence, reviewed execution, app-owned download storage, scheduling, reset recovery, and diagnostics.
- The production UI is built procedurally in one 1,075-line `MainWindow.cs`. It recreates the visual tree on navigation and operation-state changes.
- Semantic light, dark, and high-contrast brushes plus typography and basic control styles already exist in four XAML resource dictionaries.
- The design folder contains a richer native visual reference, but it is fixture-backed and is not production code or acceptance evidence.

## Existing production destinations

Overview, Scan, Scan results, Review actions, Applications, Updates, Repository-managed apps, Storage, Activity, Privacy & data, Settings, About & support, post-reset onboarding, application detail, startup failure, reset recovery, and data-operation-busy states are implemented.

## Behaviour that already works

- Loads the last known inventory and persisted connector, schedule, decision, mapping, source, and download-cache state.
- Runs a cancellable local inventory and retains the previous completed snapshot until replacement succeeds.
- Keeps source consent, source checking, package matching, review, approval, execution, and verification as separate stages.
- Exposes search, status/publisher/source/scope filters, sorting, evidence detail, pins, ignores, reversible mappings, exports, cleanup, scheduling, update confirmation, and factory reset.
- Shows fresh/cached/local-only wording and preserves non-actionable states.
- Uses native controls, accessible names on important actions, text status labels, system focus visuals, and high-contrast theme resources.

## Principal UX weaknesses

1. Eleven top-level navigation entries expose implementation structure instead of five clear user goals.
2. The inventory is a `ListView` of concatenated display strings, so columns, hierarchy, truncation, status scanning, and screen-reader summaries are weak.
3. Status values leak internal enum wording such as `VerifiedUpdate` and `ReviewRequired` in several views.
4. The shell operation message is passive; scan cancellation and meaningful indeterminate progress are only discoverable on the Scan page.
5. Horizontal filter/action rows do not adapt well at the documented 900 px minimum window.
6. The Overview repeats its headline and gives four equal-weight metric cards more prominence than the next decision.
7. Empty, partial, unavailable, and first-run states are mostly plain paragraphs instead of reusable state components.
8. Activity is a current summary rather than a clear recent timeline, despite real timestamps on scans, decisions, and mappings.
9. Shared visual patterns remain duplicated in page methods, making consistency and testing expensive.
10. Rebuilding the entire shell for each scan progress event creates avoidable UI work.

## Current working-tree constraint

Task 34 has uncommitted production changes, including window lifetime retention and administrator-requirement presentation in `App.xaml.cs` and `MainWindow.cs`. The redesign must preserve those exact behaviours and must not discard or mask the other Task 34 domain, test, documentation, signing, or deployment changes.

## Implementation conclusion

The stack and service architecture are suitable. The lowest-risk production route is an incremental WinUI redesign: retain application/domain authority, add typed presentation helpers and row templates, simplify the shell, strengthen global operation feedback, and refactor page composition around reusable semantic components. A framework migration is not justified.
