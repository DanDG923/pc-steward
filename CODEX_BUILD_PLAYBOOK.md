# PC Steward — Codex build playbook

This is the exact execution order for turning the approved PC Steward specification into a reviewable MVP while controlling Codex context and model usage.

## 1. Initial repository setup

Extract this pack into a new Windows folder, open PowerShell in that folder, and run:

```powershell
git init
git branch -M main
git add .
git commit -m "docs: initialise PC Steward product and Codex build plan"
git switch -c phase/0-foundation
```

If Git asks for an identity, configure it yourself before continuing. Do not ask Codex to change global Git settings.

Open the folder as a **local project** in the ChatGPT desktop app and select **Codex**. Use:

- Windows-native agent with PowerShell;
- the native Windows sandbox;
- **Ask for approval**, not Full access;
- Standard speed;
- the project-scoped `.codex/config.toml`;
- a trusted-project setting so project configuration can load.

Run `/status` and `/fast status`. Turn Fast off when necessary.

## 2. Usage strategy

The project defaults to **GPT-5.6 Terra / Medium**. Change only for the numbered prompt:

- **Luna / Low:** environment or highly mechanical work.
- **Terra / Medium:** ordinary bounded implementation.
- **Terra / High:** integration, debugging, matching, process orchestration, and performance work.
- **Sol / High:** architecture, threat modelling, privilege boundaries, destructive operations, and phase reviews.
- **Sol / Extra High:** Phase 3 security gate and final release audit only.

The core sequence contains **27 Terra tasks, 1 Luna task, and 9 Sol tasks; only 2 use Extra High**. Evidence-readiness tasks deliberately use Terra/High before every independent gate so reviewers receive a complete, reproducible evidence package.

Do not use Fast, Max, Ultra, subagents, or parallel worktrees by default. They increase usage or coordination overhead without helping this dependency-heavy build.

## 3. Context discipline

For every numbered task:

1. Start a **new chat**.
2. Select the model/reasoning in the task header.
3. Paste only that task's prompt.
4. Let Codex read `AGENTS.md`, `docs/STATUS.md`, the phase brief, relevant ADRs, and relevant source files.
5. Do not paste the 90 KB product specification into the chat.
6. Keep the chat until that one outcome is green and committed.
7. Review the diff and concise completion report.
8. Start the next task in a new chat.

Durable state belongs in Git, `docs/STATUS.md`, ADRs, review reports, test evidence, and failure reports—not in a long-running chat.

## 4. Exact task order

Follow [`prompts/INDEX.md`](prompts/INDEX.md) from Task 00 through Task 31.

### Phase 0

Run Tasks **00–06** on `phase/0-foundation`.

After Task 06 is green:

```powershell
git switch main
git merge --no-ff phase/0-foundation -m "merge: phase 0 architecture and scaffolding"
git switch -c phase/1-inventory
```

### Phase 1

Run Tasks **07–14**, then **14A**, then Task **15** on `phase/1-inventory`.

Task 15 is a review gate. If it fails, run Task 90 in a fresh Terra/High chat, then repeat Task 15 in a fresh Sol/High chat. When it passes:

```powershell
git switch main
git merge --no-ff phase/1-inventory -m "merge: phase 1 local inventory"
git switch -c phase/2-intelligence
```

### Phase 2

Run Tasks **16–19**, then **19A**, then Task **20** on `phase/2-intelligence`.

Task 20 is a review gate. Use Task 90 and repeat Task 20 until it passes.

The optional GitHub Releases read-only connector is deliberately excluded from the core path. To include it, run Task 18B **after the first successful Task 20 review but before merging**, then repeat Task 20.

When Phase 2 passes:

```powershell
git switch main
git merge --no-ff phase/2-intelligence -m "merge: phase 2 update intelligence"
git switch -c phase/3-execution
```

### Phase 3

Run Tasks **21–24**, then **24A**, then Task **25** on `phase/3-execution`.

Task 25 is the security gate. Any arbitrary-execution, privilege-boundary, deletion-escape, false-success, or missing mandatory evidence finding blocks progression. Use Task 90 and repeat Task 25 until it passes.

Then:

```powershell
git switch main
git merge --no-ff phase/3-execution -m "merge: phase 3 safe update execution"
git switch -c phase/4-beta
```

### Phase 4

Run Task **26**, then **26A** and Task **27**; run Tasks **28–30**, then **30A** and Task **31** on `phase/4-beta`.

Task 31 must say **READY** before the branch is merged. If it says NOT READY, use Task 90 for review findings or Task 91 for a documented technical blocker, then repeat Task 31.

When ready:

```powershell
git switch main
git merge --no-ff phase/4-beta -m "merge: phase 4 private beta candidate"
git tag -a v0.1.0-private-beta -m "PC Steward v0.1.0 private beta"
```

Create the tag only after personally reading the final audit and confirming all residual risks you accept. Publishing or pushing remains a separate explicit action.

## 5. Review-gate rule

Before every phase or design/release gate, complete its named `*A` evidence-readiness task. That task gathers a sanitized criterion-to-evidence table, runs the safe required commands, records missing evidence or explicit product-owner exceptions, and blocks the gate until every mandatory row is Proven or explicitly accepted. It prepares evidence only; the separate Sol gate remains the independent decision-maker.

This control applies to the next gate or repeat after this plan change; it does not retroactively reopen a previously passed, committed gate.

A phase passes only when:

- no Blocker or High finding remains;
- every mandatory acceptance criterion is Proven;
- there is no false-positive actionable update mapping;
- no privileged or deletion boundary is unproven;
- required build/test/benchmark evidence is present;
- `docs/STATUS.md` and traceability are current.

Do not ask an implementation chat to grade its own phase. Use the separate Sol review task.

## 6. Failure and escalation rule

If a task fails once, let the same chat inspect the exact failure and make one materially different correction.

If it remains blocked:

1. Have the current chat create `docs/failures/<task-id>.md`.
2. Stop that chat.
3. Start a fresh Sol/High chat with Task 91.
4. Read only the failure handoff, current phase brief, cited ADRs, and cited files.
5. Return to the original task's completion criteria.

This avoids feeding an increasingly large failed conversation back into every attempt.

## 7. Human approval points

You—not Codex—approve:

- system software installation;
- network access when restore/research requires it;
- disposable VM use;
- any harmless UAC integration test;
- any real software update;
- certificate creation/use;
- release signing;
- Git push, merge conflict resolution you do not understand, tag publication, or public release;
- acceptance of residual risk.

## 8. Completion definition

The project is not complete because the UI launches. It is complete only when Task 31 independently verifies the specification, threat model, zero-false-positive matching fixture, update verification, destructive-operation tests, performance evidence, packaging, uninstall/reset behaviour, and release checklist.
