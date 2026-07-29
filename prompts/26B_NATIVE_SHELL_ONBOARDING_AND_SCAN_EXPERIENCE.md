# 26B — Native shell, onboarding, and scan experience

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/4-beta` |
| Run after | Task 26 complete |
| Expected commit | `feat: add native shell and scan experience` |

**Primary requirement references:** `FR-ONB-01–04`, `FR-UI-02–03/05/07`, `FR-RSC-01–03/05`, `FR-HIS-01`, `AC-UX-01–04/06/09`

## Copy/paste prompt

```text
Task 26B — implement the native PC Steward shell, onboarding, overview and scan experience from the approved design without altering scan authority or product scope.

Read AGENTS.md, STATUS.md, docs/DESIGN_DELIVERY_PLAN.md, design/CODEX_IMPLEMENTATION_BRIEF.md, design/design-tokens.json, design/native-prototype/, the current desktop source, Task 26, and the listed requirement sources. The native prototype is the interaction reference; do not use design/prototype/ as an implementation base.

Implement Design Phases 2 and 3:
- replace the procedural single-page host with an adaptive WinUI NavigationView and page host;
- provide Overview, Scan, Applications, Updates, Activity, Privacy & data, Settings, and About & support destinations, using safe placeholder routing only where a later page has not yet been implemented;
- do not expose Startup or general device Storage in production navigation;
- preserve every existing workflow, filter and selection; active scan/update/cleanup must stay visible after navigation and Back restores relevant state/focus;
- implement first-launch/local-only guidance, explicit source consent, scan progress/cancellation, no-findings, partial-failure and offline states using PageHeader, InlineBanner, EmptyState, FreshnessLabel and named scan phases;
- preserve the last completed snapshot until a replacement scan is published; scanning remains non-elevated and read-only;
- use only the shared semantic resources and selected identity assets. Status, freshness and provider failure must always have text as well as colour/icon.

Add navigation/back-state, operation-visibility, view-model state-matrix, consent/revocation, cancellation and previous-snapshot tests. Capture real light/dark, high-contrast, keyboard, 100/150/200% text-scale, 900 x 640 and long-provider-name evidence for changed screens. Do not claim accessibility from design appearance alone. Run focused tests, Release build and full suite; update status and traceability with measured evidence only.
```

## Completion rule

Do not proceed to Task 26C until this task is committed and `docs/STATUS.md` points to Task 26C. Review tasks must pass; otherwise run Task 90 and repeat the same review.
