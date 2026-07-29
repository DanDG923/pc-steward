# 31 — Final release audit

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Sol** |
| Reasoning | **Extra High** |
| Speed | **Standard** |
| Mode | **Review** |
| Branch | `phase/4-beta` |
| Run after | Task 30A complete |
| Expected commit | `review: perform final MVP release audit` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 31 — perform the final MVP release audit. Start with `/review`. Review the complete repository and all evidence; do not modify production code.

Read:
- full product specification;
- `docs/review/FINAL_RELEASE_EVIDENCE_READINESS.md`;
- all accepted ADRs and architecture documents;
- all phase review reports;
- traceability matrix;
- threat model;
- test, benchmark, VM/manual, SBOM, static-analysis, dependency, secret-scan, packaging, and uninstall evidence;
- complete diff/history from the initial product-spec commit.
- `docs/DESIGN_DELIVERY_PLAN.md`, the approved native design package, and measured real-app visual/accessibility evidence.

Independently verify every mandatory acceptance criterion and release-definition item. Re-run safe critical suites, especially:
- zero false-positive actionable mapping;
- arbitrary command/path rejection;
- helper/IPC negative tests;
- post-update Success verification;
- destructive cleanup and factory-reset escape tests;
- cancellation/crash recovery;
- performance/resource budgets;
- no-resident-process and scheduled-exit tests.

Create `docs/review/FINAL_RELEASE_AUDIT.md` with:
- READY or NOT READY;
- criterion-by-criterion status: Proven, Failed, or Unverified;
- Blocker/High/Medium/Low findings with file/line and requirement IDs;
- exact remediation and verification;
- residual risks requiring explicit product-owner acceptance.

Any Blocker/High finding, executable false positive, privilege/deletion escape, false Success, failed mandatory criterion, material Unverified criterion, or missing/stale readiness package means NOT READY. Only update the audit report and status.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
