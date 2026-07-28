# 14A — Prepare Phase 1 review evidence

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/1-inventory` |
| Run after | Task 14 complete |
| Expected commit | `docs: prepare phase 1 review evidence` |

**Primary requirement references:** Phase 1 mandatory criteria, especially `FR-INV-01–09`, `FR-RSC-01–05`, `FR-HIS-01`, `FR-DAT-01–07`, `NFR-REL-02`, `NFR-OBS-01–02`, `AC-INV-*`, and `AC-NFR-03–09`.

## Copy/paste prompt

```text
Task 14A — prepare the Phase 1 review evidence. Do not add product scope or grade the phase.

Read AGENTS.md, STATUS.md, the Phase 1 brief, test strategy, relevant ADRs, and the Task 15 review prompt. Run the required safe fixture, Release build, full-suite, and benchmark commands. Gather only reproducible, sanitized evidence for every Task 15 mandatory check: inventory coverage, no Win32_Product, local-only/no-elevation boundaries, cancellation/recovery, persistence and clear-scan preservation, diagnostics, and measured performance.

Create docs/review/PHASE_1_EVIDENCE_READINESS.md containing a criterion-to-evidence table with command, result, artifact path/commit, status (Proven, Missing, or Approved exception), and exact next action for each non-Proven item. Update traceability and status. Do not proceed to Task 15 unless every mandatory row is Proven or carries an explicit product-owner exception recorded in the repository. Commit the evidence package.
```

## Completion rule

Do not proceed to Task 15 until this evidence task is committed and `docs/STATUS.md` points to Task 15.
