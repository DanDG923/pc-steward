# 00 — Environment and repository audit

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Luna** |
| Reasoning | **Low** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/0-foundation` |
| Run after | Initial repository commit |
| Expected commit | `docs: record Windows development environment` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 00 — audit the Windows development environment and establish the repository baseline.

Read only:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_0.md`
- `docs/product/PC_STEWARD_PRODUCT_SPEC.md`, section 14

Do not write production code. Do not install, upgrade, enable, or reconfigure system software. Do not change global Git or Codex settings.

Inspect the Windows host with read-only commands and record:
- Windows edition/build and x64 architecture;
- PowerShell version;
- Git and Git identity availability;
- .NET SDKs and runtimes;
- Visual Studio/Build Tools detection through `vswhere` when available;
- Windows SDK and WinUI/Windows App SDK workload evidence;
- WinGet version, source availability, and whether read-only `winget list` works;
- Developer Mode status where it can be read safely;
- signing/package tooling already present;
- ability to create and run a minimal test process;
- availability of a disposable Windows test VM or sandbox, without creating one.

Create:
- `docs/environment/ENVIRONMENT_REPORT.md` with Pass/Warning/Blocker status, exact commands, concise outputs, and redacted machine-specific data;
- `docs/environment/SETUP_ACTIONS.md` containing only the missing prerequisites, ordered by necessity, and linking them to an official vendor source by name;
- an updated `docs/STATUS.md`.

Run `git status`. Commit only the documentation changes if Git is already initialized, identity is configured, and the tree contains no unrelated changes. Otherwise leave the changes uncommitted and state the exact blocker.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
