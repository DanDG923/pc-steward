# Design delivery plan

This plan makes `design/design-tokens.json` and `design/native-prototype/` a staged implementation contract. The browser prototype remains archival only. No visual work changes the product safety boundaries, execution authority, or deletion scope.

| When | Design work | Delivery evidence |
|---|---|---|
| Complete foundation | Phase 0/1: token resources; light/dark/high-contrast semantic brushes; typography; card, button, input, select, status-chip and focus styles; selected identity assets. | Resource coverage test, Release build, full suite. Appearance alone is not accessibility acceptance. |
| Task 26 | Phase 2 shell plus Phase 6 settings: `NavigationView`, global operation bar, page header, state-preserving navigation, schedule/settings card and notification disclosure. | Navigation/state tests; minimum-window, theme, keyboard and scale checks. |
| Task 26B | Phase 3: onboarding, overview and scan states. | State-matrix, cancellation, consent and previous-snapshot tests plus visual-state evidence. |
| Task 26C | Phase 4: applications, findings, evidence detail, review queue and update outcome. | Actionability-negative fixtures, evidence-mutation stop, virtualization smoke and keyboard-detail checks. |
| Task 26A/27 | Phase 7 design gate only: reset manifest, dialog sequence and safe-state design. No implementation. | Accepted reset design with focus, high-contrast, partial-result and keyboard-accident constraints. |
| Task 28 | Phase 5 and Phase 7 implementation: activity/privacy and current data controls, then approved reset/advanced-data surfaces. | Delete/preserve, partial-cleanup, active-work lockout, hostile-path and reset-flow tests. |
| Task 29 | Phase 8: measured visual, accessibility, reliability and performance hardening across all implemented states. | Keyboard, screen reader, contrast, scaling, high-contrast, motion, resource and screenshot/manual evidence. |
| Tasks 30–31 | Packaging and release audit: identity assets at Windows sizes, clean launch, visual evidence and no unverified design claims. | Package and audit artefacts with exact evidence status. |

## Non-negotiable UI rules for every task

- Use semantic resources; do not introduce local palette, radius or font values.
- Keep status, confidence, freshness and destructive scope in visible text; colour never stands alone.
- Preserve domain/application authority for confidence, actionability, updates and deletion.
- Do not add Startup, broad Storage, configuration, generic cleanup or other future modules to production navigation.
- Treat the native prototype as the visual and interaction reference, but record real application evidence before claiming conformance.
