# PC Steward — Codex repository instructions

## Source of truth

1. `docs/product/PC_STEWARD_PRODUCT_SPEC.md` is authoritative.
2. For normal work, read only `AGENTS.md`, `docs/STATUS.md`, the current task prompt, the current phase brief, and directly relevant ADRs/source files.
3. Open the full product specification only for a cited requirement, ambiguity, or conflict. Do not reread it wholesale on every task.
4. Preserve requirement IDs in tests, review reports, status updates, and commit bodies.
5. Do not silently expand the MVP. Anything marked Later, Optional, Desirable, or Never requires explicit task scope.
6. Under ADR-021, `AC-UX-01–09` accessibility criteria are Optional — best effort: attempt proportionate implementation/evidence, but do not fail a gate solely because they are unproven.

## Product safety boundaries

- Windows 11 x64, local-first, account-free MVP.
- No drivers, firmware, Windows Update, registry cleaning, generic optimisation, arbitrary website search, download portals, mirrors, or unrestricted installer URLs.
- WinGet is the only actionable update provider in the core MVP.
- Never use `Win32_Product`.
- Never execute a command string, executable path, package ID, or arguments obtained blindly from remote metadata or the database.
- Low-confidence, incomparable, architecture-conflicting, or channel-conflicting candidates are not actionable.
- Scanning never requests elevation. Elevation is on demand for an immutable, user-approved update job.
- No always-running elevated service and no unattended installation in the MVP.
- Clear scan data, Clear downloaded files, and Factory reset are separate actions with different deletion semantics.
- File deletion is limited to canonical, internally allowlisted PC Steward roots and must resist junction, symlink, mount-point, and other reparse-point escapes.
- A provider or installer result is not Success until post-update verification proves the expected version or a reliable restart-pending state is returned.

## Engineering workflow

- Use native Windows/PowerShell unless an ADR explicitly changes this.
- Inspect before editing. State a plan of no more than eight bullets, then execute unless there is a genuine blocker.
- Make the smallest coherent change for the task. Do not refactor unrelated code.
- Keep domain rules, provider adapters, process execution, persistence, privileged code, and UI separated.
- Use deterministic, unit-testable rules. AI must not make trust or execution decisions.
- Prefer cancellation tokens, bounded concurrency, structured arguments, timeouts, transactional writes, and structured logs.
- Add or update tests in the same task as production code.
- Run the narrowest relevant tests first, then the repository build and full test suite before completion.
- Review `git diff` for scope creep, secrets, unsafe paths, command construction, and missing tests.
- Update `docs/STATUS.md` and `docs/requirements/TRACEABILITY.md`.
- Commit only when required tests pass. Never push, merge, tag, publish, sign, install system software, or update real applications without explicit user approval.
- Do not change Git identity or global configuration.

## Canonical local commands

- Restore: `pwsh -NoProfile -File .\scripts\Restore.ps1`
- Build: `pwsh -NoProfile -File .\scripts\Build.ps1`
- Test: `pwsh -NoProfile -File .\scripts\Test.ps1`
- Benchmark harness: `pwsh -NoProfile -File .\scripts\Benchmark.ps1`

## Resource and usage discipline

- Keep output concise. Do not reproduce large source files or long command logs in chat.
- Put durable findings in repository Markdown files; final chat output is at most 12 lines.
- Do not spawn subagents or use parallel worktrees unless the task prompt explicitly permits it.
- Do not browse or enable network access unless required by the task. Prefer official primary documentation.
- Do not install packages merely to make a preferred design work; document the requirement and request approval.
- If blocked after two materially different attempts, stop. Write `docs/failures/<task-id>.md` with commands, exact errors, changed files, and the smallest suspected cause.

## Standard completion report

Return only:
1. outcome;
2. requirement IDs addressed;
3. files changed;
4. tests/builds run and result;
5. measured evidence, where applicable;
6. unresolved risks or `None`;
7. commit hash, or why no commit was made.
