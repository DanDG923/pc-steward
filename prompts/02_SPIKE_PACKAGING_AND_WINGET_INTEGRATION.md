# 02 — Spike: packaging and WinGet integration

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/0-foundation` |
| Run after | Task 01 complete |
| Expected commit | `spike: evaluate WinGet and packaging approaches` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 02 — run the disposable packaging and WinGet integration spike for OD-02 and OD-03.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_0.md`
- `docs/architecture/COMPONENT_ARCHITECTURE.md`
- the proposed ADRs for OD-02 and OD-03
- `docs/environment/ENVIRONMENT_REPORT.md`

Create a self-contained spike under `spikes/winget-packaging/`. It must not be referenced by production projects.

Collect evidence for:
- packaged MSIX versus unpackaged signed deployment;
- whether each option can meet current-user storage, Task Scheduler, WinGet, on-demand UAC, uninstall-data choice, and update-helper requirements;
- WinGet CLI versus a supported programmatic route;
- capability/version detection;
- structured output or the most stable parseable output available;
- installed inventory, available-upgrade metadata, Microsoft Store source behaviour, scope/architecture fields, timeouts, cancellation, exit codes, and sanitized diagnostics;
- command construction using an executable plus an argument list, never shell-concatenated text.

Safety:
- run only read-only WinGet operations on the host;
- do not install, upgrade, uninstall, reset sources, change source agreements, invoke UAC, or alter package-manager state;
- any update-invocation experiment must be represented as a non-executed fixture or be deferred to an explicitly approved disposable VM.

Create `spikes/winget-packaging/SPIKE_REPORT.md` with commands, results, limitations, and a recommendation for each decision. Add automated parser/capability tests where practical. Update the proposed ADRs with evidence but leave final status for Task 05.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
