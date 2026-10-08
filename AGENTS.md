# PC Steward — Codex repository instructions

## Source of truth

1. `docs/product/PC_STEWARD_PRODUCT_SPEC.md` (v0.3) is authoritative. §27 defines the personal Patch-style scope and overrides earlier sections where they conflict.
2. For normal work, read only `AGENTS.md`, `docs/STATUS.md`, the current task prompt, the current phase brief, and directly relevant ADRs/source files.
3. Open the full product specification only for a cited requirement, ambiguity, or conflict. Do not reread it wholesale on every task.
4. Preserve requirement IDs in tests, review reports, status updates, and commit bodies.
5. Do not silently expand scope beyond spec v0.3. Anything marked Later, Optional, Desirable, or Never requires explicit task scope.
6. Under ADR-021, `AC-UX-01–09` accessibility criteria are Optional — best effort: attempt proportionate implementation/evidence, but do not fail a gate solely because they are unproven.

## Product safety boundaries

- Windows 11 x64, local-first, account-free, personal owner-only software (D-006). No distribution.
- No drivers, firmware, Windows Update, registry cleaning, generic optimisation, arbitrary website search, download portals, mirrors, or unrestricted installer URLs.
- WinGet and the fixed Chocolatey Community feed, through the trusted client at its canonical root, are the only actionable providers.
- Never run Chocolatey's PowerShell bootstrap script. Chocolatey setup is a reviewed WinGet install of `Chocolatey.Chocolatey`.
- Catalog entries, bundled or user-added, are data, never execution authority. They contain no commands, switches, scripts, paths or execution URLs. Every item is resolved live with an exact ID and version before approval.
- Never use `Win32_Product`.
- Never execute a command string, executable path, package ID, or arguments obtained blindly from remote metadata or the database.
- Low-confidence, incomparable, architecture-conflicting, or channel-conflicting candidates are not actionable.
- Scanning never requests elevation. Elevation is on demand: one UAC prompt for an immutable, user-approved operation batch, or for a user-approved Stage B policy change.
- No always-running elevated service.
- Unattended installation happens only through per-app opt-in automatic-update rules that satisfy the deterministic eligibility predicate (`FR-AUT-02`).
- Stage A automation never elevates.
- The Stage B elevated host reads only the admin-protected policy store, the bundled catalog and live provider evidence, never the per-user database.
- Clear scan data, Clear downloaded files, and Factory reset are separate actions with different deletion semantics.
- File deletion is limited to canonical, internally allowlisted PC Steward roots and must resist junction, symlink, mount-point, and other reparse-point escapes.
- A provider or installer result is not Success until post-update verification proves the expected version or a reliable restart-pending state is returned.

## Engineering workflow

- Use native Windows/PowerShell unless an ADR explicitly changes this.
- Inspect before editing. State a plan of no more than eight bullets, then execute unless there is a genuine blocker.
- At the start of each Codex task, rename the task to its numbered task and concise purpose (for example, `Task 25 — Phase 3 review`) without waiting for a reminder.
- Make the smallest coherent change for the task. Do not refactor unrelated code.
- Keep domain rules, provider adapters, process execution, persistence, privileged code, and UI separated.
- Use deterministic, unit-testable rules. AI must not make trust or execution decisions.
- Prefer cancellation tokens, bounded concurrency, structured arguments, timeouts, transactional writes, and structured logs.
- Add or update tests in the same task as production code.
- Run the narrowest relevant tests first, then the repository build and full test suite before completion.
- Personal-use process (C-12): each phase ends with one owner review plus live use on the protected owner-only installation. Separate evidence-readiness and audit tasks are not required. Hostile-input and security tests remain mandatory.
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
