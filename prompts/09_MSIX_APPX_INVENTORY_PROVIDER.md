# 09 — MSIX/AppX inventory provider

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **Medium** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/1-inventory` |
| Run after | Task 08 complete |
| Expected commit | `feat: inventory current-user MSIX packages` |

**Primary requirement references:** `FR-INV-04`, `FR-INV-06`, `FR-INV-08`, `AC-INV-01`, `AC-INV-04`, `AC-INV-06`, `AC-INV-07`

## Copy/paste prompt

```text
Task 09 — implement the current-user MSIX/AppX inventory provider.

Requirements are listed under Task 09 in `docs/requirements/TRACEABILITY.md`.

Read only the Phase 1 brief, accepted provider ADRs, and relevant code.

Use supported Windows APIs behind an adapter; do not build production behaviour around shell-concatenated PowerShell output. Enumerate only packages visible to the current user unless the specification explicitly permits more. Preserve package family/full name, version, architecture, publisher identity, install location only where safe, and source/freshness metadata.

Handle framework/resource/system packages conservatively and classify rather than hide them. A single inaccessible or malformed package must produce a warning, not abort the scan.

Add adapter fixtures and Windows integration tests that can skip with an explicit reason when the environment lacks a capability. Run targeted and full tests. Update status and traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
