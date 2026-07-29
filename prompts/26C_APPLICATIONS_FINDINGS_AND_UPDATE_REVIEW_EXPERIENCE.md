# 26C — Applications, findings, and update review experience

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/4-beta` |
| Run after | Task 26B complete |
| Expected commit | `feat: add evidence-rich update review experience` |

**Primary requirement references:** `FR-UI-01–07`, `FR-ID-01–07`, `FR-SRC-01–05/10`, `FR-POL-01–05`, `FR-UPD-01–11`, `AC-MAT-01–07`, `AC-UPD-01–11`, `AC-UX-02–06`

## Copy/paste prompt

```text
Task 26C — implement the approved evidence-rich applications, findings and reviewed-update experience without weakening matching, freshness or execution safety.

Read AGENTS.md, STATUS.md, docs/DESIGN_DELIVERY_PLAN.md, the Phase 2/3 review evidence, design/CODEX_IMPLEMENTATION_BRIEF.md, design/COMPONENT_SPECIFICATION.md, design/native-prototype/, and current application/desktop code.

Implement Design Phase 4:
- virtualized applications/findings presentation with search and status, publisher, source and scope filters;
- reusable FindingRow, StatusChip, ConfidenceBadge, EvidenceComparison, FilterBar and adaptive detail-pane/full-page patterns;
- no default selection; keep multiple install instances distinct;
- show source, package ID, installed/candidate version, scope, architecture, channel, confidence and freshness before a review action;
- render review-required, low-confidence, stale and unavailable evidence as non-actionable, with a visible explanation;
- implement reviewed update queue, confirmation and outcome surfaces that preserve running-app, UAC, restart and post-update-verification wording;
- keep UI outside command/package/deletion authority and do not create an install action from display metadata.

Add zero-false-positive actionable fixtures, source-disagreement, stale/low-confidence action-absence, reviewed-evidence-mutation stop, batch outcome mapping and virtualization/performance smoke tests. Verify keyboard table/selection/detail focus restoration, Cancel as dialog default, text-plus-icon status, light/dark/high-contrast, long/missing metadata, 300-row density and responsive detail fallback. Run focused tests, Release build and full suite; update status/traceability only with actual evidence.
```

## Completion rule

Do not proceed to Task 26A until this task is committed and `docs/STATUS.md` points to Task 26A. Review tasks must pass; otherwise run Task 90 and repeat the same review.
