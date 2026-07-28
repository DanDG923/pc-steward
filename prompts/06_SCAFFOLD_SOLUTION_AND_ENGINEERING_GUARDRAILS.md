# 06 — Scaffold solution and engineering guardrails

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **Medium** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/0-foundation` |
| Run after | Task 05 complete |
| Expected commit | `build: scaffold PC Steward solution` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 06 — scaffold the production solution and engineering guardrails from the accepted ADRs. Do not implement product features.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_0.md`
- accepted ADRs;
- `docs/architecture/COMPONENT_ARCHITECTURE.md`
- `docs/architecture/TEST_STRATEGY.md`
- `docs/environment/ENVIRONMENT_REPORT.md`

Create the smallest buildable .NET 10/WinUI solution that reflects the accepted boundaries. Include only the projects justified by the ADRs, plus:
- unit and integration test projects;
- a performance/benchmark project or harness entry point;
- versioned build settings, nullable analysis, analyzers, deterministic builds, formatting rules, and central dependency management where appropriate;
- PowerShell scripts for restore, build, test, and benchmark;
- a minimal app shell that proves startup only, with no inventory or update behaviour;
- CI configuration only if it can run on a Windows host without secrets.

Do not add placeholder services that pretend to implement requirements. Do not add telemetry, cloud services, auto-update, sample download code, or broad third-party frameworks.

Run restore, build, and the empty test suite. Record the canonical commands in `AGENTS.md` and `docs/STATUS.md`. Update traceability to show scaffolding evidence without marking feature requirements complete.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
