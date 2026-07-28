# 03 — Spike: privilege boundary and safe deletion

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Sol** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/0-foundation` |
| Run after | Task 01 complete |
| Expected commit | `spike: validate privilege and deletion boundaries` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 03 — run the disposable privilege-boundary and path-safe-deletion spike for OD-13 and the elevated-helper design.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_0.md`
- `docs/architecture/THREAT_MODEL.md`
- proposed ADRs related to privilege, cleanup, and factory reset

Create a self-contained spike under `spikes/security-boundaries/`; do not reference it from production projects.

Demonstrate, entirely inside a newly created disposable test root:
- canonical-root validation;
- rejection of relative paths, alternate roots, crafted metadata paths, and path-prefix confusion;
- detection/rejection of junctions, symbolic links, mount points, and other reparse points;
- safe handling of locked files, partial deletion, retry, idempotence, and accurate residual reporting;
- mitigation of a path-swap/TOCTOU attempt to the extent Windows APIs allow;
- a reset manifest that names only fixed PC Steward-owned categories.

Prototype the helper boundary as a no-op or test-only protocol:
- immutable job identifier;
- allowlisted operation verb and package/provider identifiers;
- authenticated/nonce-bound request;
- no arbitrary executable path, command string, working directory, environment, or output path;
- short lifetime and explicit denial/cancellation states.

Do not delete real AppData, invoke real updates, install a service, persist elevated code, or request UAC on the host. If a reparse-point test needs privilege unavailable in the sandbox, record it as a VM/manual test with exact safe steps.

Create `spikes/security-boundaries/SPIKE_REPORT.md`, update the threat model and relevant proposed ADRs with evidence, and update `docs/STATUS.md`.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
