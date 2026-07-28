# 19A — Prepare Phase 2 review evidence

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/2-intelligence` |
| Run after | Task 19 complete |
| Expected commit | `docs: prepare phase 2 review evidence` |

**Primary requirement references:** Phase 2 mandatory criteria, especially `FR-VER-01–05`, `FR-ID-01–07`, `FR-SRC-01–05/10`, `FR-POL-01–05`, `NFR-REL-01/04`, `NFR-MNT-02/03`, `AC-MAT-*`, `AC-NFR-02/05`, and `AC-DAT-04`.

## Copy/paste prompt

```text
Task 19A — prepare the Phase 2 review evidence. Do not add product scope or grade the phase.

Read AGENTS.md, STATUS.md, the Phase 2 brief, test strategy, relevant ADRs, prior review findings, and the Task 20 review prompt. Run the required safe fixture, Release build, full-suite, and source-cache/benchmark commands. Gather only reproducible, sanitized evidence for version comparison, zero false-positive actionability, source qualification/disagreement, cache freshness and offline behavior, connector isolation/disclosure, and any approved local or isolated-environment observations.

Create docs/review/PHASE_2_EVIDENCE_READINESS.md containing a criterion-to-evidence table with command, result, artifact path/commit, status (Proven, Missing, or Approved exception), and exact next action for each non-Proven item. Preserve and cross-reference prior Task 90 evidence rather than duplicating it. Update traceability and status. Do not proceed to Task 20 unless every mandatory row is Proven or carries an explicit product-owner exception recorded in the repository. Commit the evidence package.
```

## Completion rule

Do not proceed to Task 20 until this evidence task is committed and `docs/STATUS.md` points to Task 20.
