# 24A — Prepare Phase 3 security-review evidence

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/3-execution` |
| Run after | Task 24 complete |
| Expected commit | `docs: prepare phase 3 review evidence` |

**Primary requirement references:** Phase 3 mandatory criteria, especially `FR-UPD-01–11`, `FR-DAT-08–15`, `NFR-REL-03`, `NFR-OBS-01–05`, `NFR-PERF-10`, `AC-UPD-*`, `AC-DAT-*`, and `AC-NFR-10`.

## Copy/paste prompt

```text
Task 24A — prepare the Phase 3 security-review evidence. Do not add product scope or grade the phase.

Read AGENTS.md, STATUS.md, the Phase 3 brief, test strategy, ADR-017 through ADR-019, the threat model, security spike, and Task 25 review prompt. Run static analysis, hostile fixtures, targeted security suites, Release build, full suite, and safe cache/retention checks. Gather reproducible, sanitized evidence for immutable approval binding, command/path rejection, UAC/helper/IPC boundaries, batch verification and recovery, history/diagnostics redaction, cache ownership/quota, and reparse/path-swap/locked-file cleanup protection.

Create docs/review/PHASE_3_EVIDENCE_READINESS.md containing a criterion-to-evidence table with command, result, artifact path/commit, status (Proven, Missing, or Approved exception), and exact next action for each non-Proven item. List required VM/manual evidence separately; do not run UAC, real updates, or destructive tests outside approved disposable roots without product-owner approval. Update traceability and status. Do not proceed to Task 25 unless every mandatory row is Proven or carries an explicit product-owner exception recorded in the repository. Commit the evidence package.
```

## Completion rule

Do not proceed to Task 25 until this evidence task is committed and `docs/STATUS.md` points to Task 25.
