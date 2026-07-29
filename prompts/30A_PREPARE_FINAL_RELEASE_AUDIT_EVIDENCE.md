# 30A — Prepare final release-audit evidence

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/4-beta` |
| Run after | Task 30 complete |
| Expected commit | `docs: prepare final release audit evidence` |

**Primary requirement references:** All mandatory requirements and release-definition criteria.

## Copy/paste prompt

```text
Task 30A — prepare final release-audit evidence. Do not grade release readiness or publish anything.

Read AGENTS.md, STATUS.md, every phase brief, test strategy, all ADRs, phase-review reports, Task 30 artifacts, and Task 31. Run safe critical suites, Release build, full suite, benchmark commands, and static/dependency/secret/SBOM checks available in the repository. Assemble only reproducible, sanitized evidence for every mandatory acceptance criterion, packaging/uninstall/reset behavior, performance/raw measurements, and all approved VM/manual observations.

Create docs/review/FINAL_RELEASE_EVIDENCE_READINESS.md with a criterion-to-evidence table: requirement ID, command/artifact/commit, result, status (Proven, Missing, or Approved exception), owner, and exact next action. Missing or unapproved evidence blocks Task 31; do not relabel it as Proven. Update traceability and status, then commit the evidence package.
```

## Completion rule

Do not proceed to Task 31 until this evidence task is committed and `docs/STATUS.md` points to Task 31.
